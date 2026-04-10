using Microsoft.EntityFrameworkCore;
using PingMonitor.Web.Contracts.Results;
using PingMonitor.Web.Data;
using PingMonitor.Web.Models;
using PingMonitor.Web.Options;
using PingMonitor.Web.Services.BufferedResults;
using PingMonitor.Web.Services.EventLogs;
using PingMonitor.Web.Services.Metrics;
using PingMonitor.Web.Support;
using Microsoft.Extensions.Options;

namespace PingMonitor.Web.Services;

internal sealed class ResultIngestionService : IResultIngestionService
{
    private readonly PingMonitorDbContext _dbContext;
    private readonly IStateEvaluationService _stateEvaluationService;
    private readonly IBufferedResultIngestionService _bufferedResultIngestionService;
    private readonly IEventLogService _eventLogService;
    private readonly IngestRateTracker _ingestRateTracker;
    private readonly ILogger<ResultIngestionService> _logger;
    private readonly ResultBufferOptions _resultBufferOptions;

    public ResultIngestionService(
        PingMonitorDbContext dbContext,
        IStateEvaluationService stateEvaluationService,
        IBufferedResultIngestionService bufferedResultIngestionService,
        IEventLogService eventLogService,
        IngestRateTracker ingestRateTracker,
        IOptions<ResultBufferOptions> resultBufferOptions,
        ILogger<ResultIngestionService> logger)
    {
        _dbContext = dbContext;
        _stateEvaluationService = stateEvaluationService;
        _bufferedResultIngestionService = bufferedResultIngestionService;
        _eventLogService = eventLogService;
        _ingestRateTracker = ingestRateTracker;
        _resultBufferOptions = resultBufferOptions.Value;
        _logger = logger;
    }

    public async Task<SubmitResultsResponse> IngestAsync(Agent agent, SubmitResultsRequest request, CancellationToken cancellationToken)
    {
        var validationErrors = ValidateRequest(request);
        if (validationErrors.Count > 0)
        {
            throw new ResultIngestionValidationException(validationErrors);
        }

        var normalizedBatchId = request.BatchId.Trim();
        var serverTimeUtc = DateTimeOffset.UtcNow;
        var duplicateBatch = await _dbContext.ResultBatches
            .SingleOrDefaultAsync(x => x.AgentId == agent.AgentId && x.BatchId == normalizedBatchId, cancellationToken);

        if (duplicateBatch is not null)
        {
            return new SubmitResultsResponse(
                Accepted: true,
                AcceptedCount: duplicateBatch.AcceptedCount,
                Duplicate: true,
                ServerTimeUtc: serverTimeUtc);
        }

        var normalizedAssignmentIds = request.Results
            .Select(result => result.AssignmentId.Trim())
            .Distinct(StringComparer.Ordinal)
            .ToArray();

        var assignmentsForAgent = await _dbContext.MonitorAssignments
            .Where(x => x.AgentId == agent.AgentId)
            .ToListAsync(cancellationToken);

        var assignments = assignmentsForAgent
            .Where(x => normalizedAssignmentIds.Contains(x.AssignmentId, StringComparer.Ordinal))
            .ToDictionary(x => x.AssignmentId, StringComparer.Ordinal);

        for (var index = 0; index < request.Results.Count; index++)
        {
            var result = request.Results[index];
            if (!assignments.TryGetValue(result.AssignmentId.Trim(), out var assignment))
            {
                validationErrors.Add(new ApiErrorDetail($"results[{index}].assignmentId", "Assignment does not belong to the authenticated agent."));
                continue;
            }

            if (!string.Equals(result.EndpointId.Trim(), assignment.EndpointId, StringComparison.Ordinal))
            {
                validationErrors.Add(new ApiErrorDetail($"results[{index}].endpointId", "Endpoint must match the referenced assignment."));
            }

            if (!string.Equals(result.CheckType.Trim(), "icmp", StringComparison.Ordinal))
            {
                validationErrors.Add(new ApiErrorDetail($"results[{index}].checkType", "Check type 'icmp' is required for v1."));
            }
            else if (assignment.CheckType != CheckType.Icmp)
            {
                validationErrors.Add(new ApiErrorDetail($"results[{index}].checkType", "Check type does not match the referenced assignment."));
            }
        }

        if (validationErrors.Count > 0)
        {
            throw new ResultIngestionValidationException(validationErrors);
        }

        await using var transaction = await _dbContext.Database.BeginTransactionAsync(cancellationToken);
        try
        {
            var existingBatch = await _dbContext.ResultBatches
                .SingleOrDefaultAsync(x => x.AgentId == agent.AgentId && x.BatchId == normalizedBatchId, cancellationToken);
            if (existingBatch is not null)
            {
                await transaction.CommitAsync(cancellationToken);
                return new SubmitResultsResponse(true, existingBatch.AcceptedCount, true, serverTimeUtc);
            }

            var receivedAtUtc = DateTimeOffset.UtcNow;
            var storedResults = request.Results.Select(result =>
            {
                var assignment = assignments[result.AssignmentId.Trim()];
                return new CheckResult
                {
                    CheckResultId = Guid.NewGuid().ToString(),
                    AssignmentId = assignment.AssignmentId,
                    AgentId = agent.AgentId,
                    EndpointId = assignment.EndpointId,
                    CheckedAtUtc = result.CheckedAtUtc,
                    Success = result.Success,
                    RoundTripMs = result.RoundTripMs,
                    ErrorCode = NormalizeNullable(result.ErrorCode),
                    ErrorMessage = NormalizeNullable(result.ErrorMessage),
                    ReceivedAtUtc = receivedAtUtc,
                    BatchId = normalizedBatchId
                };
            }).ToArray();

            var batch = new ResultBatch
            {
                ResultBatchId = Guid.NewGuid().ToString(),
                AgentId = agent.AgentId,
                BatchId = normalizedBatchId,
                ReceivedAtUtc = receivedAtUtc,
                AcceptedCount = storedResults.Length
            };

            _dbContext.ResultBatches.Add(batch);

            var wasOnline = agent.Status == AgentHealthStatus.Online;
            agent.LastSeenUtc = receivedAtUtc;
            agent.Status = AgentHealthStatus.Online;

            await _dbContext.SaveChangesAsync(cancellationToken);
            await transaction.CommitAsync(cancellationToken);

            var bufferedResults = storedResults.Select(x => new BufferedCheckResult
            {
                CheckResultId = x.CheckResultId,
                AssignmentId = x.AssignmentId,
                CheckedAtUtc = x.CheckedAtUtc,
                Success = x.Success,
                RoundTripMs = x.RoundTripMs,
                ErrorCode = x.ErrorCode,
                ErrorMessage = x.ErrorMessage,
                ReceivedAtUtc = x.ReceivedAtUtc,
                BatchId = x.BatchId
            }).ToArray();

            _ingestRateTracker.RecordIngest(storedResults.Length);

            // Buffering boundary: only raw agent-ingested CheckResults use in-memory buffering.
            // Critical writes (event logs, auth/security/admin/config changes) remain direct DB writes.
            if (_resultBufferOptions.ResultBufferEnabled)
            {
                _bufferedResultIngestionService.Enqueue(bufferedResults);
            }
            else
            {
                _dbContext.CheckResults.AddRange(storedResults);
                await _dbContext.SaveChangesAsync(cancellationToken);
            }

            if (!wasOnline)
            {
                await _eventLogService.WriteAsync(new EventLogWriteRequest
                {
                    OccurredAtUtc = receivedAtUtc,
                    Category = EventCategory.Agent,
                    EventType = EventType.AgentBecameOnline,
                    Severity = EventSeverity.Info,
                    AgentId = agent.AgentId,
                    Message = $"Agent \"{agent.Name ?? agent.InstanceId}\" became online."
                }, cancellationToken);
            }

            if (!_resultBufferOptions.ResultBufferEnabled)
            {
                var affectedAssignmentIds = storedResults
                    .Select(x => x.AssignmentId)
                    .Distinct(StringComparer.Ordinal)
                    .ToArray();

                try
                {
                    await _stateEvaluationService.EvaluateAssignmentsAsync(affectedAssignmentIds, CancellationToken.None);
                }
                catch (Exception ex)
                {
                    _logger.LogError(
                        ex,
                        "Result ingestion persisted batch {BatchId} for agent {AgentId}, but state evaluation failed for assignments: {AssignmentIds}.",
                        normalizedBatchId,
                        agent.AgentId,
                        affectedAssignmentIds);
                }
            }

            return new SubmitResultsResponse(true, storedResults.Length, false, serverTimeUtc);
        }
        catch (DbUpdateException)
        {
            await transaction.RollbackAsync(cancellationToken);

            var acceptedBatch = await _dbContext.ResultBatches
                .SingleOrDefaultAsync(x => x.AgentId == agent.AgentId && x.BatchId == normalizedBatchId, cancellationToken);

            if (acceptedBatch is not null)
            {
                return new SubmitResultsResponse(true, acceptedBatch.AcceptedCount, true, DateTimeOffset.UtcNow);
            }

            throw;
        }
    }

    private static List<ApiErrorDetail> ValidateRequest(SubmitResultsRequest request)
    {
        var errors = new List<ApiErrorDetail>();

        if (!IsUtc(request.SentAtUtc))
        {
            errors.Add(new ApiErrorDetail("sentAtUtc", "Value must be a valid UTC ISO-8601 timestamp."));
        }

        if (string.IsNullOrWhiteSpace(request.BatchId))
        {
            errors.Add(new ApiErrorDetail("batchId", "Batch ID is required."));
        }

        if (request.Results is null || request.Results.Count == 0)
        {
            errors.Add(new ApiErrorDetail("results", "At least one result is required."));
            return errors;
        }

        for (var index = 0; index < request.Results.Count; index++)
        {
            var result = request.Results[index];
            var prefix = $"results[{index}]";

            if (string.IsNullOrWhiteSpace(result.AssignmentId))
            {
                errors.Add(new ApiErrorDetail($"{prefix}.assignmentId", "Assignment ID is required."));
            }

            if (string.IsNullOrWhiteSpace(result.EndpointId))
            {
                errors.Add(new ApiErrorDetail($"{prefix}.endpointId", "Endpoint ID is required."));
            }

            if (string.IsNullOrWhiteSpace(result.CheckType))
            {
                errors.Add(new ApiErrorDetail($"{prefix}.checkType", "Check type is required."));
            }

            if (!IsUtc(result.CheckedAtUtc))
            {
                errors.Add(new ApiErrorDetail($"{prefix}.checkedAtUtc", "Value must be a valid UTC ISO-8601 timestamp."));
            }

            if (result.Success)
            {
                if (result.RoundTripMs is not null && result.RoundTripMs < 0)
                {
                    errors.Add(new ApiErrorDetail($"{prefix}.roundTripMs", "Round-trip time must be zero or greater when present."));
                }

                if (!string.IsNullOrWhiteSpace(result.ErrorCode))
                {
                    errors.Add(new ApiErrorDetail($"{prefix}.errorCode", "Error code must be null on success."));
                }

                if (!string.IsNullOrWhiteSpace(result.ErrorMessage))
                {
                    errors.Add(new ApiErrorDetail($"{prefix}.errorMessage", "Error message must be null on success."));
                }
            }
            else
            {
                if (result.RoundTripMs is not null)
                {
                    errors.Add(new ApiErrorDetail($"{prefix}.roundTripMs", "Round-trip time must be null on failure."));
                }
            }

            if (result.RoundTripMs is not null && result.RoundTripMs < 0)
            {
                errors.Add(new ApiErrorDetail($"{prefix}.roundTripMs", "Round-trip time must be zero or greater when present."));
            }
        }

        return errors;
    }

    private static bool IsUtc(DateTimeOffset value)
    {
        return value.Offset == TimeSpan.Zero;
    }

    private static string? NormalizeNullable(string? value)
    {
        return string.IsNullOrWhiteSpace(value) ? null : value.Trim();
    }
}

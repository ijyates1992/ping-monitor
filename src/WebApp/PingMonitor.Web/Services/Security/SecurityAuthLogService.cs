using System.Text.Json;
using PingMonitor.Web.Data;
using PingMonitor.Web.Models;

namespace PingMonitor.Web.Services.Security;

internal sealed class SecurityAuthLogService : ISecurityAuthLogService
{
    private readonly PingMonitorDbContext _dbContext;

    public SecurityAuthLogService(PingMonitorDbContext dbContext)
    {
        _dbContext = dbContext;
    }

    public async Task LogUserAttemptAsync(UserAuthLogWriteRequest request, CancellationToken cancellationToken)
    {
        var log = new SecurityAuthLog
        {
            OccurredAtUtc = DateTimeOffset.UtcNow,
            AuthType = SecurityAuthType.User,
            SubjectIdentifier = Normalize(request.SubjectIdentifier, fallback: "unknown_user"),
            SourceIpAddress = Normalize(request.SourceIpAddress),
            Success = request.Success,
            FailureReason = Normalize(request.FailureReason),
            UserId = Normalize(request.UserId),
            AgentId = null,
            DetailsJson = null
        };

        _dbContext.SecurityAuthLogs.Add(log);
        await _dbContext.SaveChangesAsync(cancellationToken);
    }

    public async Task LogAgentAttemptAsync(AgentAuthLogWriteRequest request, CancellationToken cancellationToken)
    {
        var details = string.IsNullOrWhiteSpace(request.RequestPath)
            ? null
            : JsonSerializer.Serialize(new { requestPath = request.RequestPath });

        var log = new SecurityAuthLog
        {
            OccurredAtUtc = DateTimeOffset.UtcNow,
            AuthType = SecurityAuthType.Agent,
            SubjectIdentifier = Normalize(request.SubjectIdentifier, fallback: "unknown_agent"),
            SourceIpAddress = Normalize(request.SourceIpAddress),
            Success = request.Success,
            FailureReason = Normalize(request.FailureReason),
            UserId = null,
            AgentId = Normalize(request.AgentId),
            DetailsJson = details
        };

        _dbContext.SecurityAuthLogs.Add(log);
        await _dbContext.SaveChangesAsync(cancellationToken);
    }

    private static string Normalize(string? value, string fallback = "unknown")
    {
        var normalized = value?.Trim();
        return string.IsNullOrWhiteSpace(normalized) ? fallback : normalized;
    }
}

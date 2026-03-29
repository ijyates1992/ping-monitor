using Microsoft.EntityFrameworkCore;
using PingMonitor.Web.Data;
using PingMonitor.Web.Models;
using PingMonitor.Web.Services.EventLogs;

namespace PingMonitor.Web.Services.Security;

internal sealed class SecurityLogRetentionService : ISecurityLogRetentionService
{
    public const string ManualPruneConfirmationKeyword = "PRUNE";

    private readonly PingMonitorDbContext _dbContext;
    private readonly ISecuritySettingsService _securitySettingsService;
    private readonly IEventLogService _eventLogService;
    private readonly ILogger<SecurityLogRetentionService> _logger;

    public SecurityLogRetentionService(
        PingMonitorDbContext dbContext,
        ISecuritySettingsService securitySettingsService,
        IEventLogService eventLogService,
        ILogger<SecurityLogRetentionService> logger)
    {
        _dbContext = dbContext;
        _securitySettingsService = securitySettingsService;
        _eventLogService = eventLogService;
        _logger = logger;
    }

    public async Task<SecurityLogRetentionPreview> GetPreviewAsync(CancellationToken cancellationToken)
    {
        var settings = await _securitySettingsService.GetCurrentAsync(cancellationToken);

        if (!settings.SecurityLogRetentionEnabled)
        {
            return new SecurityLogRetentionPreview
            {
                RetentionEnabled = false,
                RetentionDays = settings.SecurityLogRetentionDays,
                AutoPruneEnabled = settings.SecurityLogAutoPruneEnabled,
                PruneSkipped = true,
                SkipReason = "Security auth log retention is disabled."
            };
        }

        if (settings.SecurityLogRetentionDays < 1)
        {
            return new SecurityLogRetentionPreview
            {
                RetentionEnabled = true,
                RetentionDays = settings.SecurityLogRetentionDays,
                AutoPruneEnabled = settings.SecurityLogAutoPruneEnabled,
                PruneSkipped = true,
                SkipReason = "Retention days must be greater than zero."
            };
        }

        var cutoffUtc = DateTimeOffset.UtcNow.AddDays(-settings.SecurityLogRetentionDays);
        var eligibleRows = await _dbContext.SecurityAuthLogs
            .AsNoTracking()
            .Where(x => x.OccurredAtUtc < cutoffUtc)
            .CountAsync(cancellationToken);

        return new SecurityLogRetentionPreview
        {
            RetentionEnabled = true,
            RetentionDays = settings.SecurityLogRetentionDays,
            AutoPruneEnabled = settings.SecurityLogAutoPruneEnabled,
            CutoffUtc = cutoffUtc,
            EligibleAuthLogRows = eligibleRows,
            PruneSkipped = false,
            SkipReason = null
        };
    }

    public async Task<SecurityLogPruneResult> PruneAsync(SecurityLogPruneRequest request, CancellationToken cancellationToken)
    {
        var preview = await GetPreviewAsync(cancellationToken);
        if (preview.PruneSkipped)
        {
            return new SecurityLogPruneResult
            {
                Preview = preview,
                Succeeded = false,
                Error = preview.SkipReason,
                RowsDeleted = 0,
                RowsRemaining = await _dbContext.SecurityAuthLogs.AsNoTracking().CountAsync(cancellationToken)
            };
        }

        if (!string.Equals(request.ConfirmationText?.Trim(), ManualPruneConfirmationKeyword, StringComparison.Ordinal))
        {
            return new SecurityLogPruneResult
            {
                Preview = preview,
                Succeeded = false,
                Error = $"Type {ManualPruneConfirmationKeyword} to confirm pruning security auth logs.",
                RowsDeleted = 0,
                RowsRemaining = await _dbContext.SecurityAuthLogs.AsNoTracking().CountAsync(cancellationToken)
            };
        }

        var cutoffUtc = preview.CutoffUtc!.Value;

        await _eventLogService.WriteAsync(new EventLogWriteRequest
        {
            Category = EventCategory.Security,
            EventType = EventType.SecurityAuthLogManualPruneRequested,
            Severity = EventSeverity.Warning,
            Message = "Manual security auth log prune was requested.",
            DetailsJson = $"{{\"requestedByUserId\":{FormatJsonString(request.RequestedByUserId)},\"cutoffUtc\":\"{cutoffUtc:O}\",\"eligibleRows\":{preview.EligibleAuthLogRows}}}"
        }, cancellationToken);

        var rowsDeleted = await _dbContext.SecurityAuthLogs
            .Where(x => x.OccurredAtUtc < cutoffUtc)
            .ExecuteDeleteAsync(cancellationToken);

        var rowsRemaining = await _dbContext.SecurityAuthLogs
            .AsNoTracking()
            .CountAsync(cancellationToken);

        _logger.LogInformation(
            "Security auth log prune completed. Manual: {IsManual}. CutoffUtc: {CutoffUtc}. RowsDeleted: {RowsDeleted}. RowsRemaining: {RowsRemaining}.",
            request.IsManual,
            cutoffUtc,
            rowsDeleted,
            rowsRemaining);

        await _eventLogService.WriteAsync(new EventLogWriteRequest
        {
            Category = EventCategory.Security,
            EventType = request.IsManual ? EventType.SecurityAuthLogManualPruneCompleted : EventType.SecurityAuthLogAutomaticPruneCompleted,
            Severity = EventSeverity.Info,
            Message = request.IsManual
                ? "Manual security auth log prune completed."
                : "Automatic security auth log prune completed.",
            DetailsJson = $"{{\"requestedByUserId\":{FormatJsonString(request.RequestedByUserId)},\"cutoffUtc\":\"{cutoffUtc:O}\",\"rowsDeleted\":{rowsDeleted},\"rowsRemaining\":{rowsRemaining}}}"
        }, cancellationToken);

        return new SecurityLogPruneResult
        {
            Preview = preview,
            Succeeded = true,
            Error = null,
            RowsDeleted = rowsDeleted,
            RowsRemaining = rowsRemaining
        };
    }

    private static string FormatJsonString(string? value)
    {
        if (string.IsNullOrWhiteSpace(value))
        {
            return "null";
        }

        return $"\"{value.Replace("\\", "\\\\", StringComparison.Ordinal).Replace("\"", "\\\"", StringComparison.Ordinal)}\"";
    }
}

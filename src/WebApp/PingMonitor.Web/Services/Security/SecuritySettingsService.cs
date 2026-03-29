using Microsoft.EntityFrameworkCore;
using PingMonitor.Web.Data;
using PingMonitor.Web.Models;
using PingMonitor.Web.Services.EventLogs;

namespace PingMonitor.Web.Services.Security;

internal sealed class SecuritySettingsService : ISecuritySettingsService
{
    private readonly PingMonitorDbContext _dbContext;
    private readonly IEventLogService _eventLogService;

    public SecuritySettingsService(PingMonitorDbContext dbContext, IEventLogService eventLogService)
    {
        _dbContext = dbContext;
        _eventLogService = eventLogService;
    }

    public async Task<SecuritySettingsDto> GetCurrentAsync(CancellationToken cancellationToken)
    {
        var settings = await GetOrCreateEntityAsync(cancellationToken);
        return ToDto(settings);
    }

    public async Task<SecuritySettingsDto> UpdateAsync(UpdateSecuritySettingsCommand command, CancellationToken cancellationToken)
    {
        Validate(command);

        var settings = await GetOrCreateEntityAsync(cancellationToken);
        settings.AgentFailedAttemptsBeforeTemporaryIpBlock = command.AgentFailedAttemptsBeforeTemporaryIpBlock;
        settings.AgentTemporaryIpBlockDurationMinutes = command.AgentTemporaryIpBlockDurationMinutes;
        settings.AgentFailedAttemptsBeforePermanentIpBlock = command.AgentFailedAttemptsBeforePermanentIpBlock;
        settings.UserFailedAttemptsBeforeTemporaryIpBlock = command.UserFailedAttemptsBeforeTemporaryIpBlock;
        settings.UserTemporaryIpBlockDurationMinutes = command.UserTemporaryIpBlockDurationMinutes;
        settings.UserFailedAttemptsBeforePermanentIpBlock = command.UserFailedAttemptsBeforePermanentIpBlock;
        settings.UserFailedAttemptsBeforeTemporaryAccountLockout = command.UserFailedAttemptsBeforeTemporaryAccountLockout;
        settings.UserTemporaryAccountLockoutDurationMinutes = command.UserTemporaryAccountLockoutDurationMinutes;
        settings.UpdatedAtUtc = DateTimeOffset.UtcNow;

        await _dbContext.SaveChangesAsync(cancellationToken);
        await _eventLogService.WriteAsync(new EventLogWriteRequest
        {
            Category = EventCategory.Security,
            EventType = EventType.SecuritySettingsUpdated,
            Severity = EventSeverity.Info,
            Message = "Security settings were updated.",
            DetailsJson = $"{{\"updatedAtUtc\":\"{settings.UpdatedAtUtc:O}\"}}"
        }, cancellationToken);

        return ToDto(settings);
    }

    private async Task<SecuritySettings> GetOrCreateEntityAsync(CancellationToken cancellationToken)
    {
        var settings = await _dbContext.SecuritySettings
            .SingleOrDefaultAsync(x => x.SecuritySettingsId == SecuritySettings.SingletonId, cancellationToken);

        if (settings is not null)
        {
            return settings;
        }

        settings = new SecuritySettings
        {
            SecuritySettingsId = SecuritySettings.SingletonId,
            UpdatedAtUtc = DateTimeOffset.UtcNow
        };

        _dbContext.SecuritySettings.Add(settings);
        await _dbContext.SaveChangesAsync(cancellationToken);
        return settings;
    }

    private static void Validate(UpdateSecuritySettingsCommand command)
    {
        EnsurePositive(command.AgentFailedAttemptsBeforeTemporaryIpBlock, nameof(command.AgentFailedAttemptsBeforeTemporaryIpBlock));
        EnsurePositive(command.AgentTemporaryIpBlockDurationMinutes, nameof(command.AgentTemporaryIpBlockDurationMinutes));
        EnsurePositive(command.AgentFailedAttemptsBeforePermanentIpBlock, nameof(command.AgentFailedAttemptsBeforePermanentIpBlock));
        EnsurePositive(command.UserFailedAttemptsBeforeTemporaryIpBlock, nameof(command.UserFailedAttemptsBeforeTemporaryIpBlock));
        EnsurePositive(command.UserTemporaryIpBlockDurationMinutes, nameof(command.UserTemporaryIpBlockDurationMinutes));
        EnsurePositive(command.UserFailedAttemptsBeforePermanentIpBlock, nameof(command.UserFailedAttemptsBeforePermanentIpBlock));
        EnsurePositive(command.UserFailedAttemptsBeforeTemporaryAccountLockout, nameof(command.UserFailedAttemptsBeforeTemporaryAccountLockout));
        EnsurePositive(command.UserTemporaryAccountLockoutDurationMinutes, nameof(command.UserTemporaryAccountLockoutDurationMinutes));
    }

    private static void EnsurePositive(int value, string name)
    {
        if (value < 1)
        {
            throw new ArgumentOutOfRangeException(name, "Value must be at least 1.");
        }
    }

    private static SecuritySettingsDto ToDto(SecuritySettings settings)
    {
        return new SecuritySettingsDto
        {
            AgentFailedAttemptsBeforeTemporaryIpBlock = settings.AgentFailedAttemptsBeforeTemporaryIpBlock,
            AgentTemporaryIpBlockDurationMinutes = settings.AgentTemporaryIpBlockDurationMinutes,
            AgentFailedAttemptsBeforePermanentIpBlock = settings.AgentFailedAttemptsBeforePermanentIpBlock,
            UserFailedAttemptsBeforeTemporaryIpBlock = settings.UserFailedAttemptsBeforeTemporaryIpBlock,
            UserTemporaryIpBlockDurationMinutes = settings.UserTemporaryIpBlockDurationMinutes,
            UserFailedAttemptsBeforePermanentIpBlock = settings.UserFailedAttemptsBeforePermanentIpBlock,
            UserFailedAttemptsBeforeTemporaryAccountLockout = settings.UserFailedAttemptsBeforeTemporaryAccountLockout,
            UserTemporaryAccountLockoutDurationMinutes = settings.UserTemporaryAccountLockoutDurationMinutes,
            UpdatedAtUtc = settings.UpdatedAtUtc
        };
    }
}

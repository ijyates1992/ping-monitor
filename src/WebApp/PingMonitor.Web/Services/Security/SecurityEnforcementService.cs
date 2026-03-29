using System.Text.Json;
using Microsoft.AspNetCore.Identity;
using Microsoft.EntityFrameworkCore;
using PingMonitor.Web.Data;
using PingMonitor.Web.Models;
using PingMonitor.Web.Models.Identity;
using PingMonitor.Web.Services.EventLogs;

namespace PingMonitor.Web.Services.Security;

internal sealed class SecurityEnforcementService : ISecurityEnforcementService
{
    private const int FailedAttemptLookbackHours = 24;

    private readonly PingMonitorDbContext _dbContext;
    private readonly UserManager<ApplicationUser> _userManager;
    private readonly IEventLogService _eventLogService;
    private readonly ILogger<SecurityEnforcementService> _logger;

    public SecurityEnforcementService(
        PingMonitorDbContext dbContext,
        UserManager<ApplicationUser> userManager,
        IEventLogService eventLogService,
        ILogger<SecurityEnforcementService> logger)
    {
        _dbContext = dbContext;
        _userManager = userManager;
        _eventLogService = eventLogService;
        _logger = logger;
    }

    public async Task<SecurityIpBlockStatus> GetIpBlockStatusAsync(SecurityAuthType authType, string? sourceIpAddress, CancellationToken cancellationToken)
    {
        var normalizedIp = NormalizeIpAddress(sourceIpAddress);
        if (normalizedIp is null)
        {
            return SecurityIpBlockStatus.NotBlocked();
        }

        var now = DateTimeOffset.UtcNow;
        var activeBlock = await _dbContext.SecurityIpBlocks
            .AsNoTracking()
            .Where(x => x.AuthType == authType)
            .Where(x => x.IpAddress == normalizedIp)
            .Where(x => x.RemovedAtUtc == null)
            .Where(x => x.ExpiresAtUtc == null || x.ExpiresAtUtc > now)
            .OrderByDescending(x => x.BlockType == SecurityIpBlockType.Permanent || x.BlockType == SecurityIpBlockType.Manual)
            .ThenByDescending(x => x.BlockedAtUtc)
            .FirstOrDefaultAsync(cancellationToken);

        if (activeBlock is null)
        {
            return SecurityIpBlockStatus.NotBlocked();
        }

        return SecurityIpBlockStatus.Blocked(activeBlock.BlockType, activeBlock.ExpiresAtUtc);
    }

    public Task<UserLockoutStatus> GetUserLockoutStatusAsync(ApplicationUser user, CancellationToken cancellationToken)
    {
        if (user.LockoutEnd.HasValue && user.LockoutEnd.Value > DateTimeOffset.UtcNow)
        {
            return Task.FromResult(UserLockoutStatus.Locked(user.LockoutEnd.Value));
        }

        return Task.FromResult(UserLockoutStatus.NotLockedOut());
    }

    public async Task EvaluateFailedAttemptAsync(SecurityAuthType authType, string? sourceIpAddress, CancellationToken cancellationToken)
    {
        var normalizedIp = NormalizeIpAddress(sourceIpAddress);
        if (normalizedIp is null)
        {
            return;
        }

        var now = DateTimeOffset.UtcNow;
        var settings = await GetSecuritySettingsAsync(cancellationToken);
        var lookbackStartUtc = now.AddHours(-FailedAttemptLookbackHours);

        var failedAttempts = await _dbContext.SecurityAuthLogs
            .AsNoTracking()
            .Where(x => x.AuthType == authType)
            .Where(x => x.SourceIpAddress == normalizedIp)
            .Where(x => !x.Success)
            .Where(x => x.OccurredAtUtc >= lookbackStartUtc)
            .CountAsync(cancellationToken);

        var permanentThreshold = authType == SecurityAuthType.Agent
            ? settings.AgentFailedAttemptsBeforePermanentIpBlock
            : settings.UserFailedAttemptsBeforePermanentIpBlock;

        var temporaryThreshold = authType == SecurityAuthType.Agent
            ? settings.AgentFailedAttemptsBeforeTemporaryIpBlock
            : settings.UserFailedAttemptsBeforeTemporaryIpBlock;

        if (failedAttempts >= permanentThreshold)
        {
            await EnsureIpBlockAsync(authType, normalizedIp, SecurityIpBlockType.Permanent, now, expiresAtUtc: null, cancellationToken);
            return;
        }

        if (failedAttempts >= temporaryThreshold)
        {
            var durationMinutes = authType == SecurityAuthType.Agent
                ? settings.AgentTemporaryIpBlockDurationMinutes
                : settings.UserTemporaryIpBlockDurationMinutes;
            var expiresAtUtc = now.AddMinutes(durationMinutes);
            await EnsureIpBlockAsync(authType, normalizedIp, SecurityIpBlockType.Temporary, now, expiresAtUtc, cancellationToken);
        }
    }

    public async Task EvaluateFailedUserLockoutAsync(ApplicationUser user, CancellationToken cancellationToken)
    {
        var now = DateTimeOffset.UtcNow;
        if (user.LockoutEnd.HasValue && user.LockoutEnd.Value > now)
        {
            return;
        }

        var settings = await GetSecuritySettingsAsync(cancellationToken);
        var lookbackStartUtc = now.AddHours(-FailedAttemptLookbackHours);

        var failedAttempts = await _dbContext.SecurityAuthLogs
            .AsNoTracking()
            .Where(x => x.AuthType == SecurityAuthType.User)
            .Where(x => x.UserId == user.Id)
            .Where(x => !x.Success)
            .Where(x => x.OccurredAtUtc >= lookbackStartUtc)
            .CountAsync(cancellationToken);

        if (failedAttempts < settings.UserFailedAttemptsBeforeTemporaryAccountLockout)
        {
            return;
        }

        var lockoutEndUtc = now.AddMinutes(settings.UserTemporaryAccountLockoutDurationMinutes);

        if (!user.LockoutEnabled)
        {
            user.LockoutEnabled = true;
            await _userManager.UpdateAsync(user);
        }

        await _userManager.SetLockoutEndDateAsync(user, lockoutEndUtc);

        await _eventLogService.WriteAsync(new EventLogWriteRequest
        {
            Category = EventCategory.Security,
            EventType = EventType.SecurityAutomaticUserLockoutApplied,
            Severity = EventSeverity.Warning,
            Message = "Automatic temporary account lockout applied after failed user authentication attempts.",
            DetailsJson = JsonSerializer.Serialize(new
            {
                userId = user.Id,
                lockoutEndUtc,
                lookbackWindowHours = FailedAttemptLookbackHours,
                threshold = settings.UserFailedAttemptsBeforeTemporaryAccountLockout,
                failedAttempts
            })
        }, cancellationToken);
    }

    private async Task EnsureIpBlockAsync(
        SecurityAuthType authType,
        string sourceIpAddress,
        SecurityIpBlockType blockType,
        DateTimeOffset now,
        DateTimeOffset? expiresAtUtc,
        CancellationToken cancellationToken)
    {
        var activeBlock = await _dbContext.SecurityIpBlocks
            .Where(x => x.AuthType == authType)
            .Where(x => x.IpAddress == sourceIpAddress)
            .Where(x => x.RemovedAtUtc == null)
            .Where(x => x.ExpiresAtUtc == null || x.ExpiresAtUtc > now)
            .OrderByDescending(x => x.BlockedAtUtc)
            .FirstOrDefaultAsync(cancellationToken);

        if (activeBlock is not null)
        {
            if (activeBlock.BlockType == SecurityIpBlockType.Permanent || activeBlock.BlockType == SecurityIpBlockType.Manual)
            {
                return;
            }

            if (blockType == SecurityIpBlockType.Temporary)
            {
                return;
            }

            activeBlock.RemovedAtUtc = now;
            activeBlock.RemovedByUserId = null;
        }

        var entity = new SecurityIpBlock
        {
            AuthType = authType,
            IpAddress = sourceIpAddress,
            BlockType = blockType,
            BlockedAtUtc = now,
            ExpiresAtUtc = expiresAtUtc,
            Reason = blockType == SecurityIpBlockType.Temporary
                ? "Automatic temporary block after failed authentication attempts."
                : "Automatic permanent block after failed authentication attempts.",
            CreatedByUserId = null
        };

        _dbContext.SecurityIpBlocks.Add(entity);
        await _dbContext.SaveChangesAsync(cancellationToken);

        await _eventLogService.WriteAsync(new EventLogWriteRequest
        {
            Category = EventCategory.Security,
            EventType = blockType == SecurityIpBlockType.Temporary
                ? EventType.SecurityAutomaticTemporaryIpBlockAdded
                : EventType.SecurityAutomaticPermanentIpBlockAdded,
            Severity = EventSeverity.Warning,
            Message = blockType == SecurityIpBlockType.Temporary
                ? "Automatic temporary IP block added after failed authentication attempts."
                : "Automatic permanent IP block added after failed authentication attempts.",
            DetailsJson = JsonSerializer.Serialize(new
            {
                entity.SecurityIpBlockId,
                entity.AuthType,
                entity.IpAddress,
                entity.BlockType,
                entity.BlockedAtUtc,
                entity.ExpiresAtUtc,
                lookbackWindowHours = FailedAttemptLookbackHours
            })
        }, cancellationToken);

        _logger.LogWarning("Automatic {BlockType} IP block applied for {AuthType} auth on {IpAddress}.", blockType, authType, sourceIpAddress);
    }

    private async Task<SecuritySettings> GetSecuritySettingsAsync(CancellationToken cancellationToken)
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

    private static string? NormalizeIpAddress(string? sourceIpAddress)
    {
        var value = sourceIpAddress?.Trim();
        return string.IsNullOrWhiteSpace(value) ? null : value;
    }
}

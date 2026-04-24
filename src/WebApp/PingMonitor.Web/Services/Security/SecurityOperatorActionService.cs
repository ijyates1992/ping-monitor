using System.Text.Json;
using Microsoft.AspNetCore.Identity;
using Microsoft.EntityFrameworkCore;
using PingMonitor.Web.Data;
using PingMonitor.Web.Models;
using PingMonitor.Web.Support;
using PingMonitor.Web.Models.Identity;
using PingMonitor.Web.Services.EventLogs;

namespace PingMonitor.Web.Services.Security;

internal sealed class SecurityOperatorActionService : ISecurityOperatorActionService
{
    private readonly PingMonitorDbContext _dbContext;
    private readonly UserManager<ApplicationUser> _userManager;
    private readonly IEventLogService _eventLogService;
    private readonly ILogger<SecurityOperatorActionService> _logger;

    public SecurityOperatorActionService(
        PingMonitorDbContext dbContext,
        UserManager<ApplicationUser> userManager,
        IEventLogService eventLogService,
        ILogger<SecurityOperatorActionService> logger)
    {
        _dbContext = dbContext;
        _userManager = userManager;
        _eventLogService = eventLogService;
        _logger = logger;
    }

    public async Task<ActiveSecurityIpBlockDetails?> GetActiveIpBlockAsync(string securityIpBlockId, CancellationToken cancellationToken)
    {
        var blockId = securityIpBlockId.Trim();
        if (string.IsNullOrWhiteSpace(blockId))
        {
            return null;
        }

        var now = DateTimeOffset.UtcNow;
        return await _dbContext.SecurityIpBlocks
            .AsNoTracking()
            .Where(x => x.SecurityIpBlockId == blockId)
            .Where(x => x.RemovedAtUtc == null)
            .Where(x => x.ExpiresAtUtc == null || x.ExpiresAtUtc > now)
            .Select(x => new ActiveSecurityIpBlockDetails
            {
                SecurityIpBlockId = x.SecurityIpBlockId,
                AuthType = x.AuthType,
                IpAddress = x.IpAddress,
                BlockType = x.BlockType,
                BlockedAtUtc = x.BlockedAtUtc,
                ExpiresAtUtc = x.ExpiresAtUtc
            })
            .SingleOrDefaultAsync(cancellationToken);
    }

    public async Task<LockedOutUserDetails?> GetLockedOutUserAsync(string userId, CancellationToken cancellationToken)
    {
        var normalizedUserId = userId.Trim();
        if (string.IsNullOrWhiteSpace(normalizedUserId))
        {
            return null;
        }

        var now = DateTimeOffset.UtcNow;
        return await _userManager.Users
            .AsNoTracking()
            .Where(x => x.Id == normalizedUserId)
            .Where(x => x.LockoutEnd != null && x.LockoutEnd > now)
            .Select(x => new LockedOutUserDetails
            {
                UserId = x.Id,
                UserName = x.UserName ?? string.Empty,
                Email = x.Email ?? string.Empty,
                LockoutEndUtc = x.LockoutEnd!.Value
            })
            .SingleOrDefaultAsync(cancellationToken);
    }

    public async Task<SecurityIpBlockOperationResult> RemoveIpBlockAsync(RemoveSecurityIpBlockRequest request, CancellationToken cancellationToken)
    {
        var blockId = request.SecurityIpBlockId.Trim();
        if (string.IsNullOrWhiteSpace(blockId))
        {
            return SecurityIpBlockOperationResult.Failure("Blocked IP record id is required.");
        }

        var entity = await _dbContext.SecurityIpBlocks
            .SingleOrDefaultAsync(x => x.SecurityIpBlockId == blockId, cancellationToken);

        if (entity is null)
        {
            await WriteRemoveIpBlockAuditAsync(blockId, request.RemovedByUserId, success: false, "not_found", cancellationToken);
            return SecurityIpBlockOperationResult.Failure("Blocked IP record was not found.");
        }

        var now = DateTimeOffset.UtcNow;
        if (entity.RemovedAtUtc is not null || (entity.ExpiresAtUtc.HasValue && entity.ExpiresAtUtc <= now))
        {
            await WriteRemoveIpBlockAuditAsync(entity.SecurityIpBlockId, request.RemovedByUserId, success: false, "not_active", cancellationToken, entity.AuthType, entity.IpAddress);
            return SecurityIpBlockOperationResult.Failure("Blocked IP record is no longer active.");
        }

        entity.RemovedAtUtc = now;
        entity.RemovedByUserId = string.IsNullOrWhiteSpace(request.RemovedByUserId) ? null : request.RemovedByUserId.Trim();
        await _dbContext.SaveChangesAsync(cancellationToken);

        await _eventLogService.WriteAsync(new EventLogWriteRequest
        {
            Category = EventCategory.Security,
            EventType = EventType.SecurityIpBlockRemoved,
            Severity = EventSeverity.Info,
            Message = $"Security IP block manually removed for {entity.AuthType} auth.",
            DetailsJson = JsonSerializer.Serialize(new
            {
                success = true,
                action = "manual_ip_unblock",
                entity.SecurityIpBlockId,
                entity.AuthType,
                entity.IpAddress,
                entity.BlockType,
                entity.BlockedAtUtc,
                entity.RemovedAtUtc,
                operatorUserId = entity.RemovedByUserId
            })
        }, cancellationToken);

        _logger.LogInformation(
            "Manual IP unblock succeeded. SecurityIpBlockId={SecurityIpBlockId} AuthType={AuthType} IpAddress={IpAddress} OperatorUserId={OperatorUserId}",
            entity.SecurityIpBlockId,
            entity.AuthType,
            entity.IpAddress,
            entity.RemovedByUserId ?? "n/a");

        return SecurityIpBlockOperationResult.Success();
    }

    public async Task<SecurityUserUnlockOperationResult> UnlockUserAsync(UnlockSecurityUserRequest request, CancellationToken cancellationToken)
    {
        var userId = request.UserId.Trim();
        if (string.IsNullOrWhiteSpace(userId))
        {
            return SecurityUserUnlockOperationResult.Failure("User id is required.");
        }

        var user = await _userManager.FindByIdAsync(userId);
        if (user is null)
        {
            await WriteUnlockAuditAsync(userId, null, request.OperatorUserId, success: false, "not_found", cancellationToken);
            return SecurityUserUnlockOperationResult.Failure("User was not found.");
        }

        var now = DateTimeOffset.UtcNow;
        if (user.LockoutEnd is null || user.LockoutEnd <= now)
        {
            await WriteUnlockAuditAsync(user.Id, user.UserName, request.OperatorUserId, success: false, "not_locked", cancellationToken);
            return SecurityUserUnlockOperationResult.Failure("User is not currently locked out.");
        }

        var unlockResult = await _userManager.SetLockoutEndDateAsync(user, null);
        if (!unlockResult.Succeeded)
        {
            await WriteUnlockAuditAsync(user.Id, user.UserName, request.OperatorUserId, success: false, "unlock_failed", cancellationToken);
            return SecurityUserUnlockOperationResult.Failure("Unable to update lockout state.");
        }

        var resetResult = await _userManager.ResetAccessFailedCountAsync(user);
        if (!resetResult.Succeeded)
        {
            await WriteUnlockAuditAsync(user.Id, user.UserName, request.OperatorUserId, success: false, "reset_failed_count_failed", cancellationToken);
            return SecurityUserUnlockOperationResult.Failure("User unlock was applied, but failed-count reset failed.");
        }

        await _eventLogService.WriteAsync(new EventLogWriteRequest
        {
            Category = EventCategory.Security,
            EventType = EventType.SecurityManualUserUnlockApplied,
            Severity = EventSeverity.Info,
            Message = "User lockout was manually cleared by an operator.",
            DetailsJson = JsonSerializer.Serialize(new
            {
                success = true,
                action = "manual_user_unlock",
                userId = user.Id,
                userName = user.UserName,
                operatorUserId = string.IsNullOrWhiteSpace(request.OperatorUserId) ? null : request.OperatorUserId.Trim(),
                unlockedAtUtc = DateTimeOffset.UtcNow,
                accessFailedCountReset = true
            })
        }, cancellationToken);

        _logger.LogInformation(
            "Manual user unlock succeeded. UserId={UserId} UserName={UserName} OperatorUserId={OperatorUserId}",
            user.Id,
            user.UserName ?? "n/a",
            string.IsNullOrWhiteSpace(request.OperatorUserId) ? "n/a" : request.OperatorUserId.Trim());

        return SecurityUserUnlockOperationResult.Success();
    }

    private async Task WriteRemoveIpBlockAuditAsync(
        string securityIpBlockId,
        string? operatorUserId,
        bool success,
        string failureReason,
        CancellationToken cancellationToken,
        SecurityAuthType? authType = null,
        string? ipAddress = null)
    {
        await _eventLogService.WriteAsync(new EventLogWriteRequest
        {
            Category = EventCategory.Security,
            EventType = EventType.SecurityManualIpBlockRemoveRejected,
            Severity = EventSeverity.Warning,
            Message = "Manual IP unblock request was rejected.",
            DetailsJson = JsonSerializer.Serialize(new
            {
                success,
                action = "manual_ip_unblock",
                failureReason,
                securityIpBlockId,
                authType,
                ipAddress,
                operatorUserId = string.IsNullOrWhiteSpace(operatorUserId) ? null : operatorUserId.Trim()
            })
        }, cancellationToken);

        _logger.LogWarning(
            "Manual IP unblock rejected. SecurityIpBlockId={SecurityIpBlockId} Reason={FailureReason} OperatorUserId={OperatorUserId}",
            LogValueSanitizer.ForLog(securityIpBlockId),
            LogValueSanitizer.ForLog(failureReason),
            string.IsNullOrWhiteSpace(operatorUserId) ? "n/a" : LogValueSanitizer.ForLog(operatorUserId.Trim()));
    }

    private async Task WriteUnlockAuditAsync(
        string userId,
        string? userName,
        string? operatorUserId,
        bool success,
        string failureReason,
        CancellationToken cancellationToken)
    {
        await _eventLogService.WriteAsync(new EventLogWriteRequest
        {
            Category = EventCategory.Security,
            EventType = EventType.SecurityManualUserUnlockRejected,
            Severity = EventSeverity.Warning,
            Message = "Manual user unlock request was rejected.",
            DetailsJson = JsonSerializer.Serialize(new
            {
                success,
                action = "manual_user_unlock",
                failureReason,
                userId,
                userName,
                operatorUserId = string.IsNullOrWhiteSpace(operatorUserId) ? null : operatorUserId.Trim()
            })
        }, cancellationToken);

        _logger.LogWarning(
            "Manual user unlock rejected. UserId={UserId} UserName={UserName} Reason={FailureReason} OperatorUserId={OperatorUserId}",
            LogValueSanitizer.ForLog(userId),
            string.IsNullOrWhiteSpace(userName) ? "n/a" : LogValueSanitizer.ForLog(userName),
            LogValueSanitizer.ForLog(failureReason),
            string.IsNullOrWhiteSpace(operatorUserId) ? "n/a" : LogValueSanitizer.ForLog(operatorUserId.Trim()));
    }
}

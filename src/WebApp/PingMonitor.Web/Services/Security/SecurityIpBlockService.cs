using System.Net;
using System.Text.Json;
using Microsoft.EntityFrameworkCore;
using PingMonitor.Web.Data;
using PingMonitor.Web.Models;
using PingMonitor.Web.Services.EventLogs;

namespace PingMonitor.Web.Services.Security;

internal sealed class SecurityIpBlockService : ISecurityIpBlockService
{
    private readonly PingMonitorDbContext _dbContext;
    private readonly IEventLogService _eventLogService;

    public SecurityIpBlockService(PingMonitorDbContext dbContext, IEventLogService eventLogService)
    {
        _dbContext = dbContext;
        _eventLogService = eventLogService;
    }

    public async Task<IReadOnlyList<SecurityIpBlockListItem>> ListActiveAsync(CancellationToken cancellationToken)
    {
        var now = DateTimeOffset.UtcNow;
        return await _dbContext.SecurityIpBlocks
            .AsNoTracking()
            .Where(x => x.RemovedAtUtc == null)
            .Where(x => x.ExpiresAtUtc == null || x.ExpiresAtUtc > now)
            .OrderByDescending(x => x.BlockedAtUtc)
            .Select(x => new SecurityIpBlockListItem
            {
                SecurityIpBlockId = x.SecurityIpBlockId,
                AuthType = x.AuthType,
                IpAddress = x.IpAddress,
                BlockType = x.BlockType,
                BlockedAtUtc = x.BlockedAtUtc,
                ExpiresAtUtc = x.ExpiresAtUtc,
                Reason = x.Reason
            })
            .ToListAsync(cancellationToken);
    }

    public async Task<SecurityIpBlockOperationResult> AddManualBlockAsync(ManualSecurityIpBlockRequest request, CancellationToken cancellationToken)
    {
        var ipAddress = request.IpAddress.Trim();
        if (!IPAddress.TryParse(ipAddress, out _))
        {
            return SecurityIpBlockOperationResult.Failure("IP address is not valid.");
        }

        var now = DateTimeOffset.UtcNow;
        var duplicateExists = await _dbContext.SecurityIpBlocks
            .AnyAsync(x => x.AuthType == request.AuthType
                        && x.IpAddress == ipAddress
                        && x.RemovedAtUtc == null
                        && (x.ExpiresAtUtc == null || x.ExpiresAtUtc > now), cancellationToken);

        if (duplicateExists)
        {
            return SecurityIpBlockOperationResult.Failure("An active block already exists for this auth type and IP address.");
        }

        var entity = new SecurityIpBlock
        {
            AuthType = request.AuthType,
            IpAddress = ipAddress,
            BlockType = SecurityIpBlockType.Manual,
            BlockedAtUtc = now,
            ExpiresAtUtc = null,
            Reason = string.IsNullOrWhiteSpace(request.Reason) ? "Manual operator block" : request.Reason.Trim(),
            CreatedByUserId = string.IsNullOrWhiteSpace(request.CreatedByUserId) ? null : request.CreatedByUserId.Trim()
        };

        _dbContext.SecurityIpBlocks.Add(entity);
        await _dbContext.SaveChangesAsync(cancellationToken);

        await _eventLogService.WriteAsync(new EventLogWriteRequest
        {
            Category = EventCategory.Security,
            EventType = EventType.SecurityManualIpBlockAdded,
            Severity = EventSeverity.Warning,
            Message = $"Manual security IP block added for {entity.AuthType} auth.",
            DetailsJson = JsonSerializer.Serialize(new
            {
                entity.SecurityIpBlockId,
                entity.AuthType,
                entity.IpAddress,
                entity.BlockType,
                entity.BlockedAtUtc
            })
        }, cancellationToken);

        return SecurityIpBlockOperationResult.Success();
    }

    public async Task<SecurityIpBlockOperationResult> RemoveAsync(RemoveSecurityIpBlockRequest request, CancellationToken cancellationToken)
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
            return SecurityIpBlockOperationResult.Failure("Blocked IP record was not found.");
        }

        if (entity.RemovedAtUtc is not null)
        {
            return SecurityIpBlockOperationResult.Failure("Blocked IP record is already removed.");
        }

        entity.RemovedAtUtc = DateTimeOffset.UtcNow;
        entity.RemovedByUserId = string.IsNullOrWhiteSpace(request.RemovedByUserId) ? null : request.RemovedByUserId.Trim();

        await _dbContext.SaveChangesAsync(cancellationToken);

        await _eventLogService.WriteAsync(new EventLogWriteRequest
        {
            Category = EventCategory.Security,
            EventType = EventType.SecurityIpBlockRemoved,
            Severity = EventSeverity.Info,
            Message = $"Security IP block removed for {entity.AuthType} auth.",
            DetailsJson = JsonSerializer.Serialize(new
            {
                entity.SecurityIpBlockId,
                entity.AuthType,
                entity.IpAddress,
                entity.BlockType,
                entity.BlockedAtUtc,
                entity.RemovedAtUtc
            })
        }, cancellationToken);

        return SecurityIpBlockOperationResult.Success();
    }
}

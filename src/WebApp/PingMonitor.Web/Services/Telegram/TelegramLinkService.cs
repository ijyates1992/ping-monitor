using Microsoft.EntityFrameworkCore;
using PingMonitor.Web.Data;
using PingMonitor.Web.Models;
using System.Security.Cryptography;

namespace PingMonitor.Web.Services.Telegram;

internal sealed class TelegramLinkService : ITelegramLinkService
{
    private readonly PingMonitorDbContext _dbContext;
    private readonly ILogger<TelegramLinkService> _logger;

    public TelegramLinkService(PingMonitorDbContext dbContext, ILogger<TelegramLinkService> logger)
    {
        _dbContext = dbContext;
        _logger = logger;
    }

    public async Task<PendingTelegramLinkDto> GenerateCodeAsync(string userId, CancellationToken cancellationToken)
    {
        var now = DateTimeOffset.UtcNow;
        var normalizedUserId = userId.Trim();

        var stale = await _dbContext.PendingTelegramLinks
            .Where(x => x.UserId == normalizedUserId && x.Status == PendingTelegramLinkStatus.Pending)
            .ToListAsync(cancellationToken);

        foreach (var pending in stale)
        {
            pending.Status = PendingTelegramLinkStatus.Expired;
        }

        var code = RandomNumberGenerator.GetInt32(0, 100_000_000).ToString("D8");
        var row = new PendingTelegramLink
        {
            PendingTelegramLinkId = $"tgl_{Guid.NewGuid():N}",
            UserId = normalizedUserId,
            Code = code,
            CreatedAtUtc = now,
            ExpiresAtUtc = now.AddMinutes(15),
            Status = PendingTelegramLinkStatus.Pending
        };

        _dbContext.PendingTelegramLinks.Add(row);
        await _dbContext.SaveChangesAsync(cancellationToken);
        _logger.LogInformation("Telegram link code generated for user {UserId}.", normalizedUserId);

        return new PendingTelegramLinkDto { Code = code, ExpiresAtUtc = row.ExpiresAtUtc };
    }

    public async Task<PendingTelegramLinkDto?> GetActiveCodeAsync(string userId, CancellationToken cancellationToken)
    {
        var now = DateTimeOffset.UtcNow;
        var normalizedUserId = userId.Trim();
        var row = await _dbContext.PendingTelegramLinks.AsNoTracking()
            .Where(x => x.UserId == normalizedUserId && x.Status == PendingTelegramLinkStatus.Pending && x.ExpiresAtUtc > now)
            .OrderByDescending(x => x.CreatedAtUtc)
            .FirstOrDefaultAsync(cancellationToken);

        if (row is null)
        {
            return null;
        }

        return new PendingTelegramLinkDto { Code = row.Code, ExpiresAtUtc = row.ExpiresAtUtc };
    }

    public async Task<TelegramLinkConsumeResult> ConsumeCodeAsync(string code, string chatId, string chatType, string? username, string? displayName, CancellationToken cancellationToken)
    {
        if (!string.Equals(chatType, "private", StringComparison.OrdinalIgnoreCase))
        {
            return new TelegramLinkConsumeResult { Success = false, Message = "Linking is only supported from a private chat." };
        }

        var trimmedCode = code.Trim();
        var now = DateTimeOffset.UtcNow;

        await using var tx = await _dbContext.Database.BeginTransactionAsync(cancellationToken);
        var pending = await _dbContext.PendingTelegramLinks
            .Where(x => x.Code == trimmedCode && x.Status == PendingTelegramLinkStatus.Pending)
            .OrderByDescending(x => x.CreatedAtUtc)
            .FirstOrDefaultAsync(cancellationToken);

        if (pending is null)
        {
            return new TelegramLinkConsumeResult { Success = false, Message = "Code was not found." };
        }

        if (pending.ExpiresAtUtc <= now)
        {
            pending.Status = PendingTelegramLinkStatus.Expired;
            await _dbContext.SaveChangesAsync(cancellationToken);
            await tx.CommitAsync(cancellationToken);
            return new TelegramLinkConsumeResult { Success = false, Message = "Code is expired." };
        }

        pending.Status = PendingTelegramLinkStatus.Verified;
        pending.UsedAtUtc = now;
        pending.ConsumedByChatId = chatId;

        var existing = await _dbContext.TelegramAccounts.SingleOrDefaultAsync(x => x.UserId == pending.UserId, cancellationToken);
        if (existing is null)
        {
            existing = new TelegramAccount
            {
                TelegramAccountId = $"tga_{Guid.NewGuid():N}",
                UserId = pending.UserId
            };
            _dbContext.TelegramAccounts.Add(existing);
        }

        existing.ChatId = chatId;
        existing.Verified = true;
        existing.LinkedAtUtc = now;
        existing.Username = string.IsNullOrWhiteSpace(username) ? null : username.Trim();
        existing.DisplayName = string.IsNullOrWhiteSpace(displayName) ? null : displayName.Trim();
        existing.IsActive = true;

        await _dbContext.SaveChangesAsync(cancellationToken);
        await tx.CommitAsync(cancellationToken);

        _logger.LogInformation("Telegram account linked for user {UserId}.", pending.UserId);
        return new TelegramLinkConsumeResult { Success = true, Message = "Telegram account linked." };
    }

    public async Task<TelegramAccountStatusDto?> GetAccountStatusAsync(string userId, CancellationToken cancellationToken)
    {
        var row = await _dbContext.TelegramAccounts.AsNoTracking()
            .SingleOrDefaultAsync(x => x.UserId == userId && x.IsActive, cancellationToken);

        if (row is null)
        {
            return null;
        }

        return new TelegramAccountStatusDto
        {
            Verified = row.Verified,
            ChatId = row.ChatId,
            Username = row.Username,
            DisplayName = row.DisplayName,
            LinkedAtUtc = row.LinkedAtUtc
        };
    }
}

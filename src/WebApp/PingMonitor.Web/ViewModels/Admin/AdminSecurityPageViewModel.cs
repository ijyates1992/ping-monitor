using System.ComponentModel.DataAnnotations;
using PingMonitor.Web.Models;
using PingMonitor.Web.Services.Security;

namespace PingMonitor.Web.ViewModels.Admin;

public sealed class AdminSecurityPageViewModel
{
    public bool IncludeSuccessfulUserAttempts { get; init; }
    public bool IncludeSuccessfulAgentAttempts { get; init; }
    public IReadOnlyList<SecurityAuthLogListItem> UserAttempts { get; init; } = [];
    public IReadOnlyList<SecurityAuthLogListItem> AgentAttempts { get; init; } = [];
    public IReadOnlyList<SecurityIpBlockListItem> ActiveIpBlocks { get; init; } = [];
    public IReadOnlyList<LockedOutUserListItem> LockedOutUsers { get; init; } = [];
    public SecuritySettingsForm SettingsForm { get; init; } = new();
    public ManualIpBlockForm ManualIpBlockForm { get; init; } = new();
    public bool SettingsSaved { get; init; }
    public bool BlockSaved { get; init; }
    public bool UnblockSaved { get; init; }
}

public sealed class LockedOutUserListItem
{
    public string UserId { get; init; } = string.Empty;
    public string UserName { get; init; } = string.Empty;
    public string Email { get; init; } = string.Empty;
    public DateTimeOffset LockoutEndUtc { get; init; }
}

public sealed class SecuritySettingsForm
{
    [Range(1, int.MaxValue, ErrorMessage = "Agent failed attempts before temporary IP block must be at least 1.")]
    [Display(Name = "Failed attempts before temporary IP block")]
    public int AgentFailedAttemptsBeforeTemporaryIpBlock { get; set; }

    [Range(1, int.MaxValue, ErrorMessage = "Agent temporary IP block duration must be greater than 0 minutes.")]
    [Display(Name = "Temporary IP block duration (minutes)")]
    public int AgentTemporaryIpBlockDurationMinutes { get; set; }

    [Range(1, int.MaxValue, ErrorMessage = "Agent failed attempts before permanent IP block must be at least 1.")]
    [Display(Name = "Failed attempts before permanent IP block")]
    public int AgentFailedAttemptsBeforePermanentIpBlock { get; set; }

    [Range(1, int.MaxValue, ErrorMessage = "User failed attempts before temporary IP block must be at least 1.")]
    [Display(Name = "Failed attempts before temporary IP block")]
    public int UserFailedAttemptsBeforeTemporaryIpBlock { get; set; }

    [Range(1, int.MaxValue, ErrorMessage = "User temporary IP block duration must be greater than 0 minutes.")]
    [Display(Name = "Temporary IP block duration (minutes)")]
    public int UserTemporaryIpBlockDurationMinutes { get; set; }

    [Range(1, int.MaxValue, ErrorMessage = "User failed attempts before permanent IP block must be at least 1.")]
    [Display(Name = "Failed attempts before permanent IP block")]
    public int UserFailedAttemptsBeforePermanentIpBlock { get; set; }

    [Range(1, int.MaxValue, ErrorMessage = "User failed attempts before temporary account lockout must be at least 1.")]
    [Display(Name = "Failed attempts before temporary account lockout")]
    public int UserFailedAttemptsBeforeTemporaryAccountLockout { get; set; }

    [Range(1, int.MaxValue, ErrorMessage = "User temporary account lockout duration must be greater than 0 minutes.")]
    [Display(Name = "Temporary account lockout duration (minutes)")]
    public int UserTemporaryAccountLockoutDurationMinutes { get; set; }

    public DateTimeOffset UpdatedAtUtc { get; set; }
}

public sealed class ManualIpBlockForm
{
    [Required(ErrorMessage = "Auth type is required.")]
    [Display(Name = "Auth type")]
    public SecurityAuthType? AuthType { get; set; }

    [Required(ErrorMessage = "IP address is required.")]
    [Display(Name = "IP address")]
    public string IpAddress { get; set; } = string.Empty;

    [StringLength(512, ErrorMessage = "Reason must be 512 characters or fewer.")]
    [Display(Name = "Reason")]
    public string? Reason { get; set; }
}

using System.ComponentModel.DataAnnotations;
using PingMonitor.Web.Models;

namespace PingMonitor.Web.ViewModels.Admin;

public sealed class ConfirmUnblockIpViewModel
{
    public bool IncludeSuccessfulUsers { get; set; }
    public bool IncludeSuccessfulAgents { get; set; }
    public string SecurityIpBlockId { get; set; } = string.Empty;
    public SecurityAuthType AuthType { get; set; }
    public string IpAddress { get; set; } = string.Empty;
    public SecurityIpBlockType BlockType { get; set; }
    public DateTimeOffset BlockedAtUtc { get; set; }
    public DateTimeOffset? ExpiresAtUtc { get; set; }

    [Display(Name = "Type confirmation text")]
    public string? ConfirmationText { get; set; }

    public string RequiredConfirmationText { get; set; } = string.Empty;
    public string? ErrorMessage { get; set; }
}

public sealed class ConfirmUnlockUserViewModel
{
    public bool IncludeSuccessfulUsers { get; set; }
    public bool IncludeSuccessfulAgents { get; set; }
    public string UserId { get; set; } = string.Empty;
    public string UserName { get; set; } = string.Empty;
    public string Email { get; set; } = string.Empty;
    public DateTimeOffset LockoutEndUtc { get; set; }

    [Display(Name = "Type confirmation text")]
    public string? ConfirmationText { get; set; }

    public string RequiredConfirmationText { get; set; } = string.Empty;
    public string? ErrorMessage { get; set; }
}

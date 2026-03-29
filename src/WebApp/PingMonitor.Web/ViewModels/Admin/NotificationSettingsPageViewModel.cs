using System.ComponentModel.DataAnnotations;

namespace PingMonitor.Web.ViewModels.Admin;

public sealed class NotificationSettingsPageViewModel
{
    [Display(Name = "Enable browser notifications while this app is open")]
    public bool BrowserNotificationsEnabled { get; set; }

    [Display(Name = "Cached browser permission state")]
    public string BrowserNotificationsPermissionState { get; set; } = "default";

    public bool Saved { get; set; }

    public DateTimeOffset UpdatedAtUtc { get; set; }

    public string? UpdatedByUserId { get; set; }
}

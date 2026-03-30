using System.ComponentModel.DataAnnotations;

namespace PingMonitor.Web.ViewModels.Admin;

public sealed class NotificationSettingsPageViewModel
{
    [Display(Name = "Enable SMTP delivery infrastructure")]
    public bool SmtpNotificationsEnabled { get; set; }

    [Display(Name = "SMTP host")]
    public string? SmtpHost { get; set; }

    [Display(Name = "SMTP port")]
    public int SmtpPort { get; set; } = 25;

    [Display(Name = "Use TLS")]
    public bool SmtpUseTls { get; set; } = true;

    [Display(Name = "SMTP username")]
    public string? SmtpUsername { get; set; }

    [Display(Name = "SMTP password")]
    public string? SmtpPassword { get; set; }

    [Display(Name = "Clear stored SMTP password")]
    public bool SmtpClearPassword { get; set; }

    [Display(Name = "SMTP password configured")]
    public bool SmtpPasswordConfigured { get; set; }

    [Display(Name = "From address")]
    public string? SmtpFromAddress { get; set; }

    [Display(Name = "From display name")]
    public string? SmtpFromDisplayName { get; set; }

    public bool SmtpTestSent { get; set; }
    public string? SmtpTestMessage { get; set; }
    public bool Saved { get; set; }
    public DateTimeOffset UpdatedAtUtc { get; set; }
    public string? UpdatedByUserId { get; set; }
}

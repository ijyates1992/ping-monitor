using System.ComponentModel.DataAnnotations;

namespace PingMonitor.Web.ViewModels.Admin;

public sealed class NotificationSettingsPageViewModel
{
    [Display(Name = "Enable browser notifications while this app is open")]
    public bool BrowserNotificationsEnabled { get; set; }

    [Display(Name = "Endpoint down")]
    public bool BrowserNotifyEndpointDown { get; set; }

    [Display(Name = "Endpoint recovered")]
    public bool BrowserNotifyEndpointRecovered { get; set; }

    [Display(Name = "Agent offline")]
    public bool BrowserNotifyAgentOffline { get; set; }

    [Display(Name = "Agent online")]
    public bool BrowserNotifyAgentOnline { get; set; }

    [Display(Name = "Cached browser permission state")]
    public string BrowserNotificationsPermissionState { get; set; } = "default";

    [Display(Name = "Enable SMTP email notifications")]
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

    [Display(Name = "Recipient addresses (one per line or comma-separated)")]
    public string? SmtpRecipientAddresses { get; set; }

    [Display(Name = "Endpoint down")]
    public bool SmtpNotifyEndpointDown { get; set; }

    [Display(Name = "Endpoint recovered")]
    public bool SmtpNotifyEndpointRecovered { get; set; }

    [Display(Name = "Agent offline")]
    public bool SmtpNotifyAgentOffline { get; set; }

    [Display(Name = "Agent online")]
    public bool SmtpNotifyAgentOnline { get; set; }

    public bool SmtpTestSent { get; set; }
    public string? SmtpTestMessage { get; set; }

    public bool Saved { get; set; }

    public DateTimeOffset UpdatedAtUtc { get; set; }

    public string? UpdatedByUserId { get; set; }
}

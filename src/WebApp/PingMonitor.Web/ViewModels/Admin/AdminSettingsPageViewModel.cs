using System.ComponentModel.DataAnnotations;

namespace PingMonitor.Web.ViewModels.Admin;

public sealed class AdminSettingsPageViewModel
{
    [Required(ErrorMessage = "Site URL is required.")]
    [Url(ErrorMessage = "Site URL must be a valid absolute URL.")]
    [Display(Name = "Site URL")]
    public string SiteUrl { get; set; } = string.Empty;

    [Range(1, int.MaxValue, ErrorMessage = "Default ping interval must be at least 1 second.")]
    [Display(Name = "Default ping interval (seconds)")]
    public int DefaultPingIntervalSeconds { get; set; }

    [Range(1, int.MaxValue, ErrorMessage = "Default retry interval must be at least 1 second.")]
    [Display(Name = "Default retry interval (seconds)")]
    public int DefaultRetryIntervalSeconds { get; set; }

    [Range(1, int.MaxValue, ErrorMessage = "Default timeout must be at least 1 millisecond.")]
    [Display(Name = "Default timeout (ms)")]
    public int DefaultTimeoutMs { get; set; }

    [Range(1, int.MaxValue, ErrorMessage = "Default failure threshold must be at least 1.")]
    [Display(Name = "Default failure threshold")]
    public int DefaultFailureThreshold { get; set; }

    [Range(1, int.MaxValue, ErrorMessage = "Default recovery threshold must be at least 1.")]
    [Display(Name = "Default recovery threshold")]
    public int DefaultRecoveryThreshold { get; set; }

    public bool Saved { get; set; }

    public DateTimeOffset UpdatedAtUtc { get; set; }
}

using System.ComponentModel.DataAnnotations;

namespace PingMonitor.Web.ViewModels.Admin;

public sealed class AdminSettingsPageViewModel
{
    [Required(ErrorMessage = "Site URL is required.")]
    [Url(ErrorMessage = "Site URL must be a valid absolute URL.")]
    [Display(Name = "Site URL")]
    public string SiteUrl { get; set; } = string.Empty;

    public bool Saved { get; set; }

    public DateTimeOffset UpdatedAtUtc { get; set; }
}

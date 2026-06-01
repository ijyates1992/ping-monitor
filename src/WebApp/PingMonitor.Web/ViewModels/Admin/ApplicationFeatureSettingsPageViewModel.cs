using System.ComponentModel.DataAnnotations;

namespace PingMonitor.Web.ViewModels.Admin;

public sealed class ApplicationFeatureSettingsPageViewModel
{
    [Display(Name = "Enable network diagrams")]
    public bool NetworkDiagramsEnabled { get; set; }

    public bool Saved { get; set; }

    public DateTimeOffset UpdatedAtUtc { get; set; }
}

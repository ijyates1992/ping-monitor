using System.ComponentModel.DataAnnotations;

namespace PingMonitor.Web.ViewModels.Admin;

public sealed class DefaultEndpointValuesPageViewModel : IValidatableObject
{
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

    [Display(Name = "Enable degraded evaluation")]
    public bool DegradedEvaluationEnabled { get; set; } = true;

    [Range(2, int.MaxValue, ErrorMessage = "Baseline lookback must be at least 2 minutes.")]
    [Display(Name = "Degraded baseline lookback (minutes)")]
    public int DegradedBaselineLookbackMinutes { get; set; }

    [Range(1, int.MaxValue, ErrorMessage = "Current evaluation window must be at least 1 minute.")]
    [Display(Name = "Degraded current window (minutes)")]
    public int DegradedCurrentWindowMinutes { get; set; }

    [Range(0, 100, ErrorMessage = "Packet loss increase threshold must be between 0 and 100 percentage points.")]
    [Display(Name = "Packet loss increase threshold (percentage points)")]
    public double DegradedPacketLossIncreasePercentagePoints { get; set; }

    [Range(0, 10000, ErrorMessage = "RTT increase threshold must be zero or greater.")]
    [Display(Name = "RTT increase threshold (percent)")]
    public double DegradedRttIncreasePercent { get; set; }

    [Range(1, int.MaxValue, ErrorMessage = "Minimum samples must be at least 1.")]
    [Display(Name = "Minimum samples per window")]
    public int DegradedMinimumSamples { get; set; }

    public bool Saved { get; set; }

    public DateTimeOffset UpdatedAtUtc { get; set; }

    public IEnumerable<ValidationResult> Validate(ValidationContext validationContext)
    {
        if (DegradedBaselineLookbackMinutes <= DegradedCurrentWindowMinutes)
        {
            yield return new ValidationResult(
                "Baseline lookback must be greater than the current evaluation window so the windows do not overlap.",
                [nameof(DegradedBaselineLookbackMinutes), nameof(DegradedCurrentWindowMinutes)]);
        }
    }
}

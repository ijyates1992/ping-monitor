namespace PingMonitor.Web.ViewModels.Endpoints;

public sealed class EndpointPerformancePageViewModel
{
    public string AssignmentId { get; init; } = string.Empty;
    public string EndpointName { get; init; } = string.Empty;
    public string IconKey { get; init; } = "generic";
    public string Target { get; init; } = string.Empty;
    public string AgentDisplay { get; init; } = string.Empty;
    public string SelectedRange { get; init; } = EndpointPerformanceRange.Default;
    public DateTimeOffset WindowStartUtc { get; init; }
    public DateTimeOffset WindowEndUtc { get; init; }
    public IReadOnlyList<EndpointPerformanceRangeOptionViewModel> AvailableRanges { get; init; } = [];
    public IReadOnlyList<TimeSeriesPointViewModel> RttSeries { get; init; } = [];
    public IReadOnlyList<JitterPointViewModel> JitterSeries { get; init; } = [];
    public IReadOnlyList<FailureBucketViewModel> FailureSeries { get; init; } = [];
}

public sealed class EndpointPerformanceRangeOptionViewModel
{
    public string Value { get; init; } = string.Empty;
    public string Label { get; init; } = string.Empty;
}

public sealed class TimeSeriesPointViewModel
{
    public DateTimeOffset TimestampUtc { get; init; }
    public double Value { get; init; }
}

public sealed class JitterPointViewModel
{
    public DateTimeOffset TimestampUtc { get; init; }
    public double JitterMs { get; init; }
}

public sealed class FailureBucketViewModel
{
    public DateTimeOffset BucketStartUtc { get; init; }
    public DateTimeOffset BucketEndUtc { get; init; }
    public int SuccessfulCount { get; init; }
    public int FailedCount { get; init; }
}

public static class EndpointPerformanceRange
{
    public const string OneHour = "1h";
    public const string TwentyFourHours = "24h";
    public const string SevenDays = "7d";
    public const string Default = TwentyFourHours;

    public static IReadOnlyList<EndpointPerformanceRangeOptionViewModel> Options { get; } =
    [
        new EndpointPerformanceRangeOptionViewModel { Value = OneHour, Label = "Last 1 hour" },
        new EndpointPerformanceRangeOptionViewModel { Value = TwentyFourHours, Label = "Last 24 hours" },
        new EndpointPerformanceRangeOptionViewModel { Value = SevenDays, Label = "Last 7 days" }
    ];

    public static (string Range, TimeSpan Duration, TimeSpan BucketSize) Parse(string? requestedRange)
    {
        var normalized = requestedRange?.Trim().ToLowerInvariant();
        return normalized switch
        {
            OneHour => (OneHour, TimeSpan.FromHours(1), TimeSpan.FromMinutes(5)),
            SevenDays => (SevenDays, TimeSpan.FromDays(7), TimeSpan.FromHours(6)),
            _ => (TwentyFourHours, TimeSpan.FromHours(24), TimeSpan.FromHours(1))
        };
    }
}

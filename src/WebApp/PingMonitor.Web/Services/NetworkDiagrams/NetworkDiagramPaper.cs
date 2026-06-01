namespace PingMonitor.Web.Services.NetworkDiagrams;

public static class NetworkDiagramPaper
{
    public const double ASeriesLandscapeRatio = 1.41421356237;
    public const double SmallCanvasWidth = 4000;
    public const double SmallCanvasHeight = 2828;

    public static readonly IReadOnlyList<NetworkDiagramCanvasPreset> CanvasPresets =
    [
        new("small", "Small", SmallCanvasWidth, SmallCanvasHeight),
        new("medium", "Medium", 5656, 4000),
        new("large", "Large", 8000, 5657),
        new("extra-large", "Extra large", 11314, 8000)
    ];

    public static bool IsApproximatelyASeriesLandscape(double width, double height)
    {
        if (width <= 0 || height <= 0)
        {
            return false;
        }

        return Math.Abs((width / height) - ASeriesLandscapeRatio) < 0.01;
    }
}

public sealed record NetworkDiagramCanvasPreset(string Value, string Label, double Width, double Height);

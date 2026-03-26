namespace PingMonitor.Web.Services.Endpoints;

public sealed record EndpointIconOption(string Key, string DisplayName, string Symbol);

public static class EndpointIconCatalog
{
    public const string Generic = "generic";

    public static IReadOnlyList<EndpointIconOption> Options { get; } =
    [
        new EndpointIconOption("generic", "Generic", "◻"),
        new EndpointIconOption("switch", "Switch", "⇄"),
        new EndpointIconOption("firewall", "Firewall", "🛡"),
        new EndpointIconOption("server", "Server", "🖧"),
        new EndpointIconOption("router", "Router", "⤴"),
        new EndpointIconOption("printer", "Printer", "🖨"),
        new EndpointIconOption("pc", "PC", "🖥"),
        new EndpointIconOption("laptop", "Laptop", "💻"),
        new EndpointIconOption("nas", "NAS", "💾"),
        new EndpointIconOption("access-point", "Access point", "📶"),
        new EndpointIconOption("camera", "Camera", "📷"),
        new EndpointIconOption("phone", "Phone", "📱")
    ];

    private static readonly HashSet<string> ValidKeys = Options
        .Select(x => x.Key)
        .ToHashSet(StringComparer.OrdinalIgnoreCase);

    private static readonly Dictionary<string, EndpointIconOption> OptionsByKey = Options
        .ToDictionary(x => x.Key, x => x, StringComparer.OrdinalIgnoreCase);

    public static string Normalize(string? iconKey)
    {
        if (string.IsNullOrWhiteSpace(iconKey))
        {
            return Generic;
        }

        var normalized = iconKey.Trim();
        return ValidKeys.Contains(normalized) ? normalized.ToLowerInvariant() : Generic;
    }

    public static string GetSymbol(string? iconKey) => GetOption(iconKey).Symbol;

    public static string GetDisplayName(string? iconKey) => GetOption(iconKey).DisplayName;

    private static EndpointIconOption GetOption(string? iconKey)
    {
        var normalized = Normalize(iconKey);
        return OptionsByKey.GetValueOrDefault(normalized) ?? OptionsByKey[Generic];
    }
}

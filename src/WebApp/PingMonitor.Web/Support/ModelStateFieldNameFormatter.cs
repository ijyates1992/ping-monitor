namespace PingMonitor.Web.Support;

public static class ModelStateFieldNameFormatter
{
    public static string ToCamelCase(string key)
    {
        if (string.IsNullOrWhiteSpace(key))
        {
            return key;
        }

        var bracketIndex = key.IndexOf('[');
        var dotIndex = key.IndexOf('.');
        var stopIndex = new[] { bracketIndex, dotIndex }
            .Where(index => index >= 0)
            .DefaultIfEmpty(key.Length)
            .Min();

        var firstSegment = key[..stopIndex];
        if (string.IsNullOrEmpty(firstSegment))
        {
            return key;
        }

        var camelFirstSegment = char.ToLowerInvariant(firstSegment[0]) + firstSegment[1..];
        return camelFirstSegment + key[stopIndex..];
    }
}

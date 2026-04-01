namespace PingMonitor.Web.Support;

public static class DisplayFormatting
{
    public static string FormatUtcDateTime(DateTimeOffset? value)
    {
        return value.HasValue
            ? value.Value.UtcDateTime.ToString("yyyy-MM-dd HH:mm:ss 'UTC'")
            : "n/a";
    }

    public static string FormatBytes(long bytes)
    {
        if (bytes < 1024)
        {
            return $"{bytes} B";
        }

        var units = new[] { "KB", "MB", "GB", "TB" };
        double size = bytes;
        var unitIndex = -1;

        while (size >= 1024 && unitIndex < units.Length - 1)
        {
            size /= 1024;
            unitIndex++;
        }

        return $"{size:0.##} {units[unitIndex]}";
    }

    public static string FormatPercent(double value)
    {
        return $"{value:0.##}%";
    }
}

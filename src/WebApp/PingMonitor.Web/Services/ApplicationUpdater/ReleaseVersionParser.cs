using System.Text.RegularExpressions;

namespace PingMonitor.Web.Services.ApplicationUpdater;

internal static partial class ReleaseVersionParser
{
    private static readonly Regex ReleaseVersionRegex = ReleaseRegex();
    private static readonly Regex DevBuildVersionRegex = DevRegex();

    public static VersionParseResult ParseCurrent(string? version)
    {
        var normalized = version?.Trim();
        if (string.IsNullOrWhiteSpace(normalized))
        {
            return VersionParseResult.Unknown(normalized);
        }

        if (DevBuildVersionRegex.IsMatch(normalized))
        {
            return VersionParseResult.DevBuild(normalized);
        }

        if (TryParseReleaseVersion(normalized, out var releaseVersion))
        {
            return VersionParseResult.Release(normalized, releaseVersion);
        }

        return VersionParseResult.Unknown(normalized);
    }

    public static bool TryParseReleaseVersion(string? value, out ReleaseVersion version)
    {
        version = default;

        var normalized = value?.Trim();
        if (string.IsNullOrWhiteSpace(normalized))
        {
            return false;
        }

        var match = ReleaseVersionRegex.Match(normalized);
        if (!match.Success)
        {
            return false;
        }

        if (!int.TryParse(match.Groups[1].Value, out var major)
            || !int.TryParse(match.Groups[2].Value, out var minor)
            || !int.TryParse(match.Groups[3].Value, out var patch))
        {
            return false;
        }

        version = new ReleaseVersion(major, minor, patch);
        return true;
    }

    [GeneratedRegex("^V(\\d+)\\.(\\d+)\\.(\\d+)$", RegexOptions.CultureInvariant)]
    private static partial Regex ReleaseRegex();

    [GeneratedRegex("^DEV-\\d{2}\\.\\d{2}\\.\\d{2}-\\d{2}:\\d{2}$", RegexOptions.CultureInvariant)]
    private static partial Regex DevRegex();
}

internal readonly record struct ReleaseVersion(int Major, int Minor, int Patch)
    : IComparable<ReleaseVersion>
{
    public int CompareTo(ReleaseVersion other)
    {
        var majorCompare = Major.CompareTo(other.Major);
        if (majorCompare != 0)
        {
            return majorCompare;
        }

        var minorCompare = Minor.CompareTo(other.Minor);
        if (minorCompare != 0)
        {
            return minorCompare;
        }

        return Patch.CompareTo(other.Patch);
    }

    public override string ToString()
    {
        return $"V{Major}.{Minor}.{Patch}";
    }
}

internal sealed record VersionParseResult(string? RawVersion, bool IsReleaseVersion, bool IsDevBuild, ReleaseVersion? ReleaseVersion)
{
    public static VersionParseResult Release(string rawVersion, ReleaseVersion releaseVersion)
    {
        return new VersionParseResult(rawVersion, IsReleaseVersion: true, IsDevBuild: false, ReleaseVersion: releaseVersion);
    }

    public static VersionParseResult DevBuild(string rawVersion)
    {
        return new VersionParseResult(rawVersion, IsReleaseVersion: false, IsDevBuild: true, ReleaseVersion: null);
    }

    public static VersionParseResult Unknown(string? rawVersion)
    {
        return new VersionParseResult(rawVersion, IsReleaseVersion: false, IsDevBuild: false, ReleaseVersion: null);
    }
}

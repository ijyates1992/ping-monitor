using System.Globalization;
using System.Text.RegularExpressions;

namespace PingMonitor.Web.Services.ApplicationUpdate;

public readonly record struct ApplicationUpdateVersion(int Major, int Minor, int Patch) : IComparable<ApplicationUpdateVersion>
{
    private static readonly Regex VersionRegex = new("^[Vv](\\d+)\\.(\\d+)\\.(\\d+)$", RegexOptions.Compiled | RegexOptions.CultureInvariant);

    public static bool TryParse(string? value, out ApplicationUpdateVersion version)
    {
        version = default;
        if (string.IsNullOrWhiteSpace(value))
        {
            return false;
        }

        var match = VersionRegex.Match(value.Trim());
        if (!match.Success)
        {
            return false;
        }

        if (!int.TryParse(match.Groups[1].Value, NumberStyles.None, CultureInfo.InvariantCulture, out var major)
            || !int.TryParse(match.Groups[2].Value, NumberStyles.None, CultureInfo.InvariantCulture, out var minor)
            || !int.TryParse(match.Groups[3].Value, NumberStyles.None, CultureInfo.InvariantCulture, out var patch))
        {
            return false;
        }

        version = new ApplicationUpdateVersion(major, minor, patch);
        return true;
    }

    public int CompareTo(ApplicationUpdateVersion other)
    {
        var majorComparison = Major.CompareTo(other.Major);
        if (majorComparison != 0)
        {
            return majorComparison;
        }

        var minorComparison = Minor.CompareTo(other.Minor);
        if (minorComparison != 0)
        {
            return minorComparison;
        }

        return Patch.CompareTo(other.Patch);
    }

    public override string ToString() => $"V{Major}.{Minor}.{Patch}";
}

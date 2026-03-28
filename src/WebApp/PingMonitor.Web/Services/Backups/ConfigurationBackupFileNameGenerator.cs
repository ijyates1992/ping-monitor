using System.Text;
using System.Text.RegularExpressions;

namespace PingMonitor.Web.Services.Backups;

public interface IConfigurationBackupFileNameGenerator
{
    string CreateFileName(string backupName, string appVersion, DateTimeOffset exportedAtUtc);
}

public sealed partial class ConfigurationBackupFileNameGenerator : IConfigurationBackupFileNameGenerator
{
    private const int MaxNameLength = 64;

    public string CreateFileName(string backupName, string appVersion, DateTimeOffset exportedAtUtc)
    {
        if (string.IsNullOrWhiteSpace(backupName))
        {
            throw new ArgumentException("Backup name is required.", nameof(backupName));
        }

        var safeBackupName = SanitizeBackupName(backupName);
        var safeVersion = SanitizeBackupName(appVersion);
        var timestamp = exportedAtUtc.UtcDateTime.ToString("yyyy-MM-dd-HH-mm-ss");
        return $"config-backup-{safeBackupName}-{safeVersion}-{timestamp}.json";
    }

    private static string SanitizeBackupName(string input)
    {
        var lowered = input.Trim().ToLowerInvariant();
        var spaced = WhitespaceRegex().Replace(lowered, "-");

        var builder = new StringBuilder(spaced.Length);
        foreach (var c in spaced)
        {
            if (Path.GetInvalidFileNameChars().Contains(c))
            {
                continue;
            }

            if (char.IsLetterOrDigit(c) || c is '-' or '_')
            {
                builder.Append(c);
            }
        }

        var sanitized = builder.ToString().Trim('-');
        if (sanitized.Length > MaxNameLength)
        {
            sanitized = sanitized[..MaxNameLength].Trim('-');
        }

        if (string.IsNullOrWhiteSpace(sanitized))
        {
            return "backup";
        }

        return sanitized;
    }

    [GeneratedRegex("\\s+")]
    private static partial Regex WhitespaceRegex();
}

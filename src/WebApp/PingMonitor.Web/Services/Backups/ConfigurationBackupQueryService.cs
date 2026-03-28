using System.Text.Json;
using Microsoft.AspNetCore.Hosting;
using Microsoft.Extensions.Options;
using PingMonitor.Web.Options;

namespace PingMonitor.Web.Services.Backups;

public interface IConfigurationBackupQueryService
{
    Task<IReadOnlyList<BackupFileListItem>> ListBackupsAsync(CancellationToken cancellationToken);
    string ResolveDownloadPath(string fileId);
}

public sealed class ConfigurationBackupQueryService : IConfigurationBackupQueryService
{
    private readonly IWebHostEnvironment _environment;
    private readonly BackupOptions _options;

    public ConfigurationBackupQueryService(IWebHostEnvironment environment, IOptions<BackupOptions> options)
    {
        _environment = environment;
        _options = options.Value;
    }

    public async Task<IReadOnlyList<BackupFileListItem>> ListBackupsAsync(CancellationToken cancellationToken)
    {
        var storagePath = ResolveStoragePath();
        if (!Directory.Exists(storagePath))
        {
            return [];
        }

        var files = Directory.EnumerateFiles(storagePath, "config-backup-*.json", SearchOption.TopDirectoryOnly)
            .OrderByDescending(path => path, StringComparer.Ordinal)
            .ToArray();

        var rows = new List<BackupFileListItem>(files.Length);
        foreach (var fullPath in files)
        {
            cancellationToken.ThrowIfCancellationRequested();

            var fileInfo = new FileInfo(fullPath);
            var metadata = await TryReadMetadataAsync(fullPath, cancellationToken);

            rows.Add(new BackupFileListItem
            {
                FileName = fileInfo.Name,
                FileId = fileInfo.Name,
                FileCreatedAtUtc = fileInfo.CreationTimeUtc,
                ExportedAtUtc = metadata.ExportedAtUtc,
                BackupName = metadata.BackupName,
                AppVersion = metadata.AppVersion,
                IncludedSections = metadata.IncludedSections,
                NotesSummary = metadata.NotesSummary
            });
        }

        return rows;
    }

    public string ResolveDownloadPath(string fileId)
    {
        if (string.IsNullOrWhiteSpace(fileId))
        {
            throw new FileNotFoundException("Backup file was not found.");
        }

        var fileName = Path.GetFileName(fileId);
        if (!string.Equals(fileName, fileId, StringComparison.Ordinal))
        {
            throw new FileNotFoundException("Backup file was not found.");
        }

        var rootPath = Path.GetFullPath(ResolveStoragePath());
        var fullPath = Path.GetFullPath(Path.Combine(rootPath, fileName));

        if (!fullPath.StartsWith(rootPath, StringComparison.Ordinal))
        {
            throw new FileNotFoundException("Backup file was not found.");
        }

        if (!File.Exists(fullPath))
        {
            throw new FileNotFoundException("Backup file was not found.");
        }

        return fullPath;
    }

    private string ResolveStoragePath()
    {
        return Path.IsPathRooted(_options.StoragePath)
            ? _options.StoragePath
            : Path.Combine(_environment.ContentRootPath, _options.StoragePath);
    }

    private static async Task<BackupListMetadata> TryReadMetadataAsync(string fullPath, CancellationToken cancellationToken)
    {
        try
        {
            await using var stream = File.OpenRead(fullPath);
            using var document = await JsonDocument.ParseAsync(stream, cancellationToken: cancellationToken);
            var root = document.RootElement;

            DateTimeOffset? exportedAtUtc = null;
            if (root.TryGetProperty("exportedAtUtc", out var exportedAtProperty)
                && exportedAtProperty.ValueKind == JsonValueKind.String
                && DateTimeOffset.TryParse(exportedAtProperty.GetString(), out var parsedExportedAt))
            {
                exportedAtUtc = parsedExportedAt;
            }

            var includedSections = new List<string>();
            if (root.TryGetProperty("sections", out var sectionsElement) && sectionsElement.ValueKind == JsonValueKind.Object)
            {
                foreach (var section in ConfigurationBackupSections.All)
                {
                    if (sectionsElement.TryGetProperty(section, out var sectionNode) && sectionNode.ValueKind != JsonValueKind.Null)
                    {
                        includedSections.Add(section);
                    }
                }
            }

            string? notes = null;
            if (root.TryGetProperty("notes", out var notesProperty) && notesProperty.ValueKind == JsonValueKind.String)
            {
                notes = notesProperty.GetString();
            }

            return new BackupListMetadata
            {
                ExportedAtUtc = exportedAtUtc,
                BackupName = TryReadString(root, "backupName"),
                AppVersion = TryReadString(root, "appVersion"),
                IncludedSections = includedSections,
                NotesSummary = ToNotesSummary(notes)
            };
        }
        catch (JsonException)
        {
            return new BackupListMetadata();
        }
        catch (IOException)
        {
            return new BackupListMetadata();
        }
    }

    private static string? TryReadString(JsonElement root, string propertyName)
    {
        if (root.TryGetProperty(propertyName, out var property) && property.ValueKind == JsonValueKind.String)
        {
            return property.GetString();
        }

        return null;
    }

    private static string? ToNotesSummary(string? notes)
    {
        if (string.IsNullOrWhiteSpace(notes))
        {
            return null;
        }

        var trimmed = notes.Trim();
        if (trimmed.Length <= 100)
        {
            return trimmed;
        }

        return $"{trimmed[..100]}...";
    }

    private sealed class BackupListMetadata
    {
        public DateTimeOffset? ExportedAtUtc { get; init; }
        public string? BackupName { get; init; }
        public string? AppVersion { get; init; }
        public IReadOnlyList<string> IncludedSections { get; init; } = [];
        public string? NotesSummary { get; init; }
    }
}

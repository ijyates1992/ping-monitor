using System.Text.Json;
using Microsoft.AspNetCore.Hosting;
using Microsoft.Extensions.Options;
using PingMonitor.Web.Options;

namespace PingMonitor.Web.Services.Backups;

public interface IConfigurationBackupDocumentLoader
{
    Task<IReadOnlyList<BackupFileListItem>> ListBackupsAsync(CancellationToken cancellationToken);
    string ResolveBackupPath(string fileId);
    Task<ConfigurationBackupDocument> LoadValidatedDocumentAsync(string fileId, CancellationToken cancellationToken);
}

public sealed class ConfigurationBackupDocumentLoader : IConfigurationBackupDocumentLoader
{
    private readonly IWebHostEnvironment _environment;
    private readonly BackupOptions _options;
    private readonly ILogger<ConfigurationBackupDocumentLoader> _logger;

    public ConfigurationBackupDocumentLoader(
        IWebHostEnvironment environment,
        IOptions<BackupOptions> options,
        ILogger<ConfigurationBackupDocumentLoader> logger)
    {
        _environment = environment;
        _options = options.Value;
        _logger = logger;
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

    public string ResolveBackupPath(string fileId)
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

    public async Task<ConfigurationBackupDocument> LoadValidatedDocumentAsync(string fileId, CancellationToken cancellationToken)
    {
        var fullPath = ResolveBackupPath(fileId);

        await using var stream = File.OpenRead(fullPath);
        var document = await JsonSerializer.DeserializeAsync<ConfigurationBackupDocument>(stream, cancellationToken: cancellationToken);
        if (document is null)
        {
            throw new InvalidOperationException("Backup file could not be parsed.");
        }

        ValidateDocument(document, fileId);
        return document;
    }

    private string ResolveStoragePath()
    {
        return Path.IsPathRooted(_options.StoragePath)
            ? _options.StoragePath
            : Path.Combine(_environment.ContentRootPath, _options.StoragePath);
    }

    private void ValidateDocument(ConfigurationBackupDocument document, string fileId)
    {
        if (document.FormatVersion != ConfigurationBackupMetadata.CurrentFormatVersion)
        {
            throw BuildValidationException(fileId, $"Backup formatVersion {document.FormatVersion} is not supported.");
        }

        if (string.IsNullOrWhiteSpace(document.BackupName)
            || string.IsNullOrWhiteSpace(document.AppVersion)
            || document.ExportedAtUtc == default
            || string.IsNullOrWhiteSpace(document.MachineName))
        {
            throw BuildValidationException(fileId, "Backup metadata is incomplete.");
        }

        if (document.Sections is null)
        {
            throw BuildValidationException(fileId, "Backup sections are missing.");
        }

        if (document.Sections.Agents is not null)
        {
            foreach (var agent in document.Sections.Agents)
            {
                if (string.IsNullOrWhiteSpace(agent.InstanceId))
                {
                    throw BuildValidationException(fileId, "Agent section contains an invalid record (instanceId missing).");
                }
            }
        }

        if (document.Sections.Endpoints is not null)
        {
            foreach (var endpoint in document.Sections.Endpoints)
            {
                if (string.IsNullOrWhiteSpace(endpoint.Name) || string.IsNullOrWhiteSpace(endpoint.Target))
                {
                    throw BuildValidationException(fileId, "Endpoint section contains an invalid record (name/target missing).");
                }
            }
        }

        if (document.Sections.Assignments is not null)
        {
            foreach (var assignment in document.Sections.Assignments)
            {
                if (string.IsNullOrWhiteSpace(assignment.AgentId)
                    || string.IsNullOrWhiteSpace(assignment.EndpointId)
                    || string.IsNullOrWhiteSpace(assignment.CheckType))
                {
                    throw BuildValidationException(fileId, "Assignment section contains an invalid record.");
                }
            }
        }

        if (document.Sections.Identity is not null)
        {
            foreach (var user in document.Sections.Identity.Users)
            {
                if (string.IsNullOrWhiteSpace(user.NormalizedUserName) && string.IsNullOrWhiteSpace(user.NormalizedEmail))
                {
                    throw BuildValidationException(fileId, "Identity section contains a user without normalized username/email.");
                }
            }
        }

        _logger.LogInformation("Validated configuration backup {FileId} for restore workflow.", fileId);
    }

    private InvalidOperationException BuildValidationException(string fileId, string message)
    {
        _logger.LogWarning("Backup validation failed for {FileId}: {ValidationMessage}", fileId, message);
        return new InvalidOperationException(message);
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

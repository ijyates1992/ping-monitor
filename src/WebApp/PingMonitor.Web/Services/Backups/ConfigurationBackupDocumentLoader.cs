using System.Text.Json;
using Microsoft.AspNetCore.Hosting;
using Microsoft.Extensions.Options;
using PingMonitor.Web.Options;
using PingMonitor.Web.Support;

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
    private readonly IConfigurationBackupDocumentValidator _documentValidator;
    private readonly IConfigurationBackupCatalogService _catalogService;
    private readonly ILogger<ConfigurationBackupDocumentLoader> _logger;

    public ConfigurationBackupDocumentLoader(
        IWebHostEnvironment environment,
        IOptions<BackupOptions> options,
        IConfigurationBackupDocumentValidator documentValidator,
        IConfigurationBackupCatalogService catalogService,
        ILogger<ConfigurationBackupDocumentLoader> logger)
    {
        _environment = environment;
        _options = options.Value;
        _documentValidator = documentValidator;
        _catalogService = catalogService;
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
        var sourceCatalog = await _catalogService.GetSourcesAsync(cancellationToken);
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
                BackupSource = ResolveSource(fileInfo.Name, metadata.BackupSource, sourceCatalog),
                IncludedSections = metadata.IncludedSections,
                NotesSummary = metadata.NotesSummary,
                FileSizeBytes = fileInfo.Length
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

        try
        {
            _documentValidator.Validate(document, fileId);
        }
        catch (InvalidOperationException ex)
        {
            throw BuildValidationException(fileId, ex.Message);
        }

        _logger.LogInformation("Validated configuration backup {FileId} for restore workflow.", LogValueSanitizer.ForLog(fileId));
        return document;
    }

    private string ResolveStoragePath()
    {
        return Path.IsPathRooted(_options.StoragePath)
            ? _options.StoragePath
            : Path.Combine(_environment.ContentRootPath, _options.StoragePath);
    }

    private InvalidOperationException BuildValidationException(string fileId, string message)
    {
        _logger.LogWarning("Backup validation failed for {FileId}: {ValidationMessage}", LogValueSanitizer.ForLog(fileId), LogValueSanitizer.ForLog(message));
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

                if (!includedSections.Contains(ConfigurationBackupSections.Dependencies, StringComparer.Ordinal)
                    && sectionsElement.TryGetProperty("endpoints", out var endpointsElement)
                    && endpointsElement.ValueKind == JsonValueKind.Array)
                {
                    foreach (var endpointElement in endpointsElement.EnumerateArray())
                    {
                        if (endpointElement.ValueKind != JsonValueKind.Object)
                        {
                            continue;
                        }

                        if (endpointElement.TryGetProperty("dependsOnEndpointIds", out var dependsOnElement)
                            && dependsOnElement.ValueKind == JsonValueKind.Array
                            && dependsOnElement.GetArrayLength() > 0)
                        {
                            includedSections.Add(ConfigurationBackupSections.Dependencies);
                            break;
                        }
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
                BackupSource = TryReadString(root, "backupSource"),
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
        public string? BackupSource { get; init; }
        public IReadOnlyList<string> IncludedSections { get; init; } = [];
        public string? NotesSummary { get; init; }
    }

    private static string ResolveSource(string fileId, string? documentSource, IReadOnlyDictionary<string, string> catalog)
    {
        if (catalog.TryGetValue(fileId, out var catalogSource)
            && ConfigurationBackupSources.All.Contains(catalogSource, StringComparer.Ordinal))
        {
            return catalogSource;
        }

        if (!string.IsNullOrWhiteSpace(documentSource)
            && ConfigurationBackupSources.All.Contains(documentSource, StringComparer.Ordinal))
        {
            return documentSource;
        }

        return ConfigurationBackupSources.Manual;
    }
}

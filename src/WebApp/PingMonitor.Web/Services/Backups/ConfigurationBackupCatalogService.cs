using System.Text.Json;
using Microsoft.AspNetCore.Hosting;
using Microsoft.Extensions.Options;
using PingMonitor.Web.Options;

namespace PingMonitor.Web.Services.Backups;

public interface IConfigurationBackupCatalogService
{
    Task<IReadOnlyDictionary<string, string>> GetSourcesAsync(CancellationToken cancellationToken);
    Task UpsertSourceAsync(string fileId, string source, CancellationToken cancellationToken);
    Task RemoveAsync(string fileId, CancellationToken cancellationToken);
}

public sealed class ConfigurationBackupCatalogService : IConfigurationBackupCatalogService
{
    private static readonly JsonSerializerOptions SerializerOptions = new() { WriteIndented = true };
    private static readonly SemaphoreSlim FileLock = new(1, 1);

    private readonly IWebHostEnvironment _environment;
    private readonly BackupOptions _options;

    public ConfigurationBackupCatalogService(IWebHostEnvironment environment, IOptions<BackupOptions> options)
    {
        _environment = environment;
        _options = options.Value;
    }

    public async Task<IReadOnlyDictionary<string, string>> GetSourcesAsync(CancellationToken cancellationToken)
    {
        var catalog = await LoadCatalogAsync(cancellationToken);
        return catalog.Entries.ToDictionary(x => x.FileId, x => x.Source, StringComparer.Ordinal);
    }

    public async Task UpsertSourceAsync(string fileId, string source, CancellationToken cancellationToken)
    {
        if (string.IsNullOrWhiteSpace(fileId))
        {
            return;
        }

        var normalizedSource = NormalizeSource(source);

        await FileLock.WaitAsync(cancellationToken);
        try
        {
            var catalog = await LoadCatalogCoreAsync(cancellationToken);
            var existing = catalog.Entries.SingleOrDefault(x => string.Equals(x.FileId, fileId, StringComparison.Ordinal));
            if (existing is null)
            {
                catalog.Entries.Add(new BackupCatalogEntry
                {
                    FileId = fileId,
                    Source = normalizedSource,
                    UpdatedAtUtc = DateTimeOffset.UtcNow
                });
            }
            else
            {
                existing.Source = normalizedSource;
                existing.UpdatedAtUtc = DateTimeOffset.UtcNow;
            }

            await SaveCatalogCoreAsync(catalog, cancellationToken);
        }
        finally
        {
            FileLock.Release();
        }
    }

    public async Task RemoveAsync(string fileId, CancellationToken cancellationToken)
    {
        if (string.IsNullOrWhiteSpace(fileId))
        {
            return;
        }

        await FileLock.WaitAsync(cancellationToken);
        try
        {
            var catalog = await LoadCatalogCoreAsync(cancellationToken);
            catalog.Entries.RemoveAll(x => string.Equals(x.FileId, fileId, StringComparison.Ordinal));
            await SaveCatalogCoreAsync(catalog, cancellationToken);
        }
        finally
        {
            FileLock.Release();
        }
    }

    private async Task<BackupCatalogDocument> LoadCatalogAsync(CancellationToken cancellationToken)
    {
        await FileLock.WaitAsync(cancellationToken);
        try
        {
            return await LoadCatalogCoreAsync(cancellationToken);
        }
        finally
        {
            FileLock.Release();
        }
    }

    private async Task<BackupCatalogDocument> LoadCatalogCoreAsync(CancellationToken cancellationToken)
    {
        var fullPath = ResolveCatalogPath();
        if (!File.Exists(fullPath))
        {
            return new BackupCatalogDocument();
        }

        await using var stream = File.OpenRead(fullPath);
        var document = await JsonSerializer.DeserializeAsync<BackupCatalogDocument>(stream, cancellationToken: cancellationToken);
        return document ?? new BackupCatalogDocument();
    }

    private async Task SaveCatalogCoreAsync(BackupCatalogDocument document, CancellationToken cancellationToken)
    {
        var fullPath = ResolveCatalogPath();
        Directory.CreateDirectory(Path.GetDirectoryName(fullPath)!);

        var json = JsonSerializer.Serialize(document, SerializerOptions);
        await File.WriteAllTextAsync(fullPath, json, cancellationToken);
    }

    private string ResolveCatalogPath()
    {
        var storagePath = Path.IsPathRooted(_options.StoragePath)
            ? _options.StoragePath
            : Path.Combine(_environment.ContentRootPath, _options.StoragePath);

        return Path.Combine(storagePath, "backup-catalog.json");
    }

    private static string NormalizeSource(string? source)
    {
        if (string.IsNullOrWhiteSpace(source))
        {
            return ConfigurationBackupSources.Manual;
        }

        var normalized = source.Trim().ToLowerInvariant();
        return ConfigurationBackupSources.All.Contains(normalized, StringComparer.Ordinal)
            ? normalized
            : ConfigurationBackupSources.Manual;
    }

    private sealed class BackupCatalogDocument
    {
        public List<BackupCatalogEntry> Entries { get; init; } = [];
    }

    private sealed class BackupCatalogEntry
    {
        public string FileId { get; init; } = string.Empty;
        public string Source { get; set; } = ConfigurationBackupSources.Manual;
        public DateTimeOffset UpdatedAtUtc { get; set; }
    }
}

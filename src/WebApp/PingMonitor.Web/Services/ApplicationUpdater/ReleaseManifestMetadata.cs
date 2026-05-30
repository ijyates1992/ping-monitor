using System.IO.Compression;
using System.Text.Json;

namespace PingMonitor.Web.Services.ApplicationUpdater;

public enum ReleaseSchemaMetadataSource
{
    MissingOrUnknown = 0,
    StandaloneManifestAsset = 1,
    StagedPackageManifest = 2
}

public sealed record ReleaseManifestMetadata(
    string? AppName,
    string? Version,
    string? BuildTimestampUtc,
    string? PackageFileName,
    string? Runtime,
    string? CommitHash,
    int RequiredSchemaVersion,
    ReleaseSchemaMetadataSource Source,
    string? SourceName);

public static class ReleaseManifestMetadataReader
{
    public static async Task<ReleaseManifestMetadata> ReadStandaloneManifestAsync(
        Stream manifestStream,
        string sourceName,
        CancellationToken cancellationToken)
    {
        return await ReadManifestAsync(manifestStream, ReleaseSchemaMetadataSource.StandaloneManifestAsset, sourceName, cancellationToken);
    }

    public static async Task<ReleaseManifestMetadata> ReadPackageManifestFromZipAsync(
        string zipPath,
        string expectedPackageFileName,
        CancellationToken cancellationToken)
    {
        await using var stream = File.OpenRead(zipPath);
        using var archive = new ZipArchive(stream, ZipArchiveMode.Read, leaveOpen: false);
        var entry = ResolvePackageManifestEntry(archive, expectedPackageFileName);
        await using var manifestStream = entry.Open();
        return await ReadManifestAsync(manifestStream, ReleaseSchemaMetadataSource.StagedPackageManifest, entry.FullName, cancellationToken);
    }

    public static void ValidateForRelease(
        ReleaseManifestMetadata metadata,
        string expectedReleaseTag,
        string expectedPackageFileName,
        string expectedRuntime)
    {
        if (!string.Equals(metadata.Version, expectedReleaseTag, StringComparison.OrdinalIgnoreCase))
        {
            throw new InvalidOperationException($"Manifest version '{metadata.Version ?? "(missing)"}' does not match release tag '{expectedReleaseTag}'.");
        }

        if (!string.Equals(metadata.PackageFileName, expectedPackageFileName, StringComparison.OrdinalIgnoreCase))
        {
            throw new InvalidOperationException($"Manifest packageFileName '{metadata.PackageFileName ?? "(missing)"}' does not match selected package '{expectedPackageFileName}'.");
        }

        if (!string.IsNullOrWhiteSpace(metadata.Runtime)
            && !string.Equals(metadata.Runtime, expectedRuntime, StringComparison.OrdinalIgnoreCase))
        {
            throw new InvalidOperationException($"Manifest runtime '{metadata.Runtime}' does not match configured runtime '{expectedRuntime}'.");
        }
    }

    public static void ValidateNoConflict(ReleaseManifestMetadata standaloneMetadata, ReleaseManifestMetadata packageMetadata)
    {
        if (!string.Equals(standaloneMetadata.Version, packageMetadata.Version, StringComparison.OrdinalIgnoreCase)
            || !string.Equals(standaloneMetadata.PackageFileName, packageMetadata.PackageFileName, StringComparison.OrdinalIgnoreCase)
            || !string.Equals(standaloneMetadata.Runtime, packageMetadata.Runtime, StringComparison.OrdinalIgnoreCase)
            || !string.Equals(standaloneMetadata.CommitHash, packageMetadata.CommitHash, StringComparison.OrdinalIgnoreCase)
            || standaloneMetadata.RequiredSchemaVersion != packageMetadata.RequiredSchemaVersion)
        {
            throw new InvalidOperationException("Standalone release manifest metadata conflicts with the manifest embedded in the staged package.");
        }
    }

    private static ZipArchiveEntry ResolvePackageManifestEntry(ZipArchive archive, string expectedPackageFileName)
    {
        var expectedRoot = Path.GetFileNameWithoutExtension(expectedPackageFileName);
        var expectedNestedPath = string.IsNullOrWhiteSpace(expectedRoot)
            ? null
            : $"{expectedRoot}/manifest.json";

        var manifestEntries = archive.Entries
            .Where(entry => !string.IsNullOrWhiteSpace(entry.Name)
                            && string.Equals(entry.Name, "manifest.json", StringComparison.OrdinalIgnoreCase))
            .ToArray();

        if (manifestEntries.Length == 0)
        {
            throw new InvalidOperationException("Staged release package did not include manifest.json.");
        }

        var rootEntry = manifestEntries.FirstOrDefault(entry =>
            string.Equals(NormalizeZipPath(entry.FullName), "manifest.json", StringComparison.OrdinalIgnoreCase));
        if (rootEntry is not null)
        {
            return rootEntry;
        }

        if (expectedNestedPath is not null)
        {
            var expectedEntry = manifestEntries.FirstOrDefault(entry =>
                string.Equals(NormalizeZipPath(entry.FullName), expectedNestedPath, StringComparison.OrdinalIgnoreCase));
            if (expectedEntry is not null)
            {
                return expectedEntry;
            }
        }

        if (manifestEntries.Length == 1)
        {
            var onlyEntryPath = NormalizeZipPath(manifestEntries[0].FullName);
            if (onlyEntryPath.Count(character => character == '/') == 1)
            {
                return manifestEntries[0];
            }
        }

        throw new InvalidOperationException("Staged release package contains multiple or unexpected manifest.json files; schema metadata source is ambiguous.");
    }

    private static async Task<ReleaseManifestMetadata> ReadManifestAsync(
        Stream manifestStream,
        ReleaseSchemaMetadataSource source,
        string sourceName,
        CancellationToken cancellationToken)
    {
        using var document = await JsonDocument.ParseAsync(manifestStream, cancellationToken: cancellationToken);
        var root = document.RootElement;
        if (root.ValueKind != JsonValueKind.Object)
        {
            throw new InvalidOperationException("Release manifest JSON root must be an object.");
        }

        if (!root.TryGetProperty("requiredSchemaVersion", out var schemaVersionElement)
            || schemaVersionElement.ValueKind != JsonValueKind.Number
            || !schemaVersionElement.TryGetInt32(out var requiredSchemaVersion)
            || requiredSchemaVersion < 1)
        {
            throw new InvalidOperationException("Release manifest requiredSchemaVersion must be an integer >= 1.");
        }

        return new ReleaseManifestMetadata(
            GetString(root, "appName"),
            GetString(root, "version"),
            GetString(root, "buildTimestampUtc"),
            GetString(root, "packageFileName"),
            GetString(root, "runtime"),
            GetString(root, "commitHash"),
            requiredSchemaVersion,
            source,
            sourceName);
    }

    private static string? GetString(JsonElement root, string propertyName)
    {
        return root.TryGetProperty(propertyName, out var value) && value.ValueKind == JsonValueKind.String
            ? value.GetString()
            : null;
    }

    private static string NormalizeZipPath(string value)
    {
        return value.Replace('\\', '/').TrimStart('/');
    }
}

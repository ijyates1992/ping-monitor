using System.Text.Json;
using Microsoft.AspNetCore.Hosting;
using Microsoft.Extensions.Options;
using PingMonitor.Web.Options;

namespace PingMonitor.Web.Services.Backups;

public interface IConfigurationBackupUploadService
{
    Task<UploadConfigurationBackupResponse> UploadAsync(UploadConfigurationBackupRequest request, CancellationToken cancellationToken);
}

public sealed class ConfigurationBackupUploadService : IConfigurationBackupUploadService
{
    private readonly IWebHostEnvironment _environment;
    private readonly BackupOptions _options;
    private readonly IConfigurationBackupDocumentValidator _documentValidator;
    private readonly IConfigurationBackupFileNameGenerator _fileNameGenerator;
    private readonly ILogger<ConfigurationBackupUploadService> _logger;

    public ConfigurationBackupUploadService(
        IWebHostEnvironment environment,
        IOptions<BackupOptions> options,
        IConfigurationBackupDocumentValidator documentValidator,
        IConfigurationBackupFileNameGenerator fileNameGenerator,
        ILogger<ConfigurationBackupUploadService> logger)
    {
        _environment = environment;
        _options = options.Value;
        _documentValidator = documentValidator;
        _fileNameGenerator = fileNameGenerator;
        _logger = logger;
    }

    public async Task<UploadConfigurationBackupResponse> UploadAsync(UploadConfigurationBackupRequest request, CancellationToken cancellationToken)
    {
        if (request.File is null)
        {
            throw new InvalidOperationException("Backup upload file is required.");
        }

        _logger.LogInformation(
            "Configuration backup upload attempt started. UploadedBy: {UploadedBy}, ContentLength: {ContentLength}.",
            request.UploadedBy ?? "(unknown)",
            request.File.Length);

        ValidateFileEnvelope(request.File);

        byte[] fileContent;
        await using (var inputStream = request.File.OpenReadStream())
        using (var memoryStream = new MemoryStream())
        {
            await inputStream.CopyToAsync(memoryStream, cancellationToken);
            fileContent = memoryStream.ToArray();
        }

        var parsedDocument = ParseDocument(fileContent);
        ValidateDocument(parsedDocument);

        var uploadedAtUtc = DateTimeOffset.UtcNow;
        var storagePath = Path.GetFullPath(ResolveStoragePath());
        Directory.CreateDirectory(storagePath);

        var generatedFileName = _fileNameGenerator.CreateFileName(parsedDocument.BackupName, parsedDocument.AppVersion, parsedDocument.ExportedAtUtc);
        var storedFileName = ResolveUniqueFileName(storagePath, generatedFileName);
        var fullPath = Path.GetFullPath(Path.Combine(storagePath, storedFileName));
        if (!fullPath.StartsWith(storagePath, StringComparison.Ordinal))
        {
            throw new InvalidOperationException("Backup file path resolution failed.");
        }

        await using (var outputStream = new FileStream(fullPath, FileMode.CreateNew, FileAccess.Write, FileShare.None))
        {
            await outputStream.WriteAsync(fileContent, cancellationToken);
        }

        _logger.LogInformation(
            "Configuration backup upload accepted. StoredFile: {StoredFile}, BackupName: {BackupName}, ExportedAtUtc: {ExportedAtUtc:O}.",
            storedFileName,
            parsedDocument.BackupName,
            parsedDocument.ExportedAtUtc);

        return new UploadConfigurationBackupResponse
        {
            FileName = storedFileName,
            FileId = storedFileName,
            BackupName = parsedDocument.BackupName,
            AppVersion = parsedDocument.AppVersion,
            ExportedAtUtc = parsedDocument.ExportedAtUtc,
            UploadedAtUtc = uploadedAtUtc,
            UploadedBy = request.UploadedBy
        };
    }

    private void ValidateFileEnvelope(Microsoft.AspNetCore.Http.IFormFile file)
    {
        if (file.Length <= 0)
        {
            throw BuildValidationException("Uploaded file is empty.");
        }

        if (_options.MaxUploadSizeBytes <= 0)
        {
            throw BuildValidationException("Backup upload max size is not configured correctly.");
        }

        if (file.Length > _options.MaxUploadSizeBytes)
        {
            throw BuildValidationException($"Uploaded file exceeds maximum allowed size ({_options.MaxUploadSizeBytes} bytes).");
        }

        var extension = Path.GetExtension(file.FileName);
        if (!string.Equals(extension, ".json", StringComparison.OrdinalIgnoreCase))
        {
            throw BuildValidationException("Only .json backup files are supported.");
        }
    }

    private ConfigurationBackupDocument ParseDocument(byte[] fileContent)
    {
        try
        {
            var document = JsonSerializer.Deserialize<ConfigurationBackupDocument>(fileContent);
            if (document is null)
            {
                throw BuildValidationException("Uploaded backup file could not be parsed.");
            }

            return document;
        }
        catch (JsonException)
        {
            throw BuildValidationException("Uploaded file is not valid JSON.");
        }
    }

    private void ValidateDocument(ConfigurationBackupDocument document)
    {
        try
        {
            _documentValidator.Validate(document, "uploaded-file");
        }
        catch (InvalidOperationException ex)
        {
            throw BuildValidationException(ex.Message);
        }
    }

    private InvalidOperationException BuildValidationException(string message)
    {
        _logger.LogWarning("Configuration backup upload validation failed: {ValidationMessage}", message);
        return new InvalidOperationException(message);
    }

    private string ResolveStoragePath()
    {
        return Path.IsPathRooted(_options.StoragePath)
            ? _options.StoragePath
            : Path.Combine(_environment.ContentRootPath, _options.StoragePath);
    }

    private static string ResolveUniqueFileName(string storagePath, string fileName)
    {
        var extension = Path.GetExtension(fileName);
        var baseName = Path.GetFileNameWithoutExtension(fileName);
        var candidate = fileName;
        var suffix = 1;

        while (File.Exists(Path.Combine(storagePath, candidate)))
        {
            candidate = $"{baseName}-{suffix}{extension}";
            suffix++;
        }

        return candidate;
    }
}

namespace PingMonitor.Web.Services.Backups;

public interface IConfigurationRestorePreviewService
{
    Task<ConfigurationBackupPreview> GetPreviewAsync(string fileId, CancellationToken cancellationToken);
}

public sealed class ConfigurationRestorePreviewService : IConfigurationRestorePreviewService
{
    private readonly IConfigurationBackupDocumentLoader _documentLoader;
    private readonly ILogger<ConfigurationRestorePreviewService> _logger;

    public ConfigurationRestorePreviewService(
        IConfigurationBackupDocumentLoader documentLoader,
        ILogger<ConfigurationRestorePreviewService> logger)
    {
        _documentLoader = documentLoader;
        _logger = logger;
    }

    public async Task<ConfigurationBackupPreview> GetPreviewAsync(string fileId, CancellationToken cancellationToken)
    {
        var document = await _documentLoader.LoadValidatedDocumentAsync(fileId, cancellationToken);

        var includedSections = GetIncludedSections(document).ToArray();
        var preview = new ConfigurationBackupPreview
        {
            FileId = fileId,
            FileName = fileId,
            Metadata = new RestorePreviewMetadata
            {
                BackupName = document.BackupName,
                ExportedAtUtc = document.ExportedAtUtc,
                AppVersion = document.AppVersion,
                FormatVersion = document.FormatVersion,
                Notes = document.Notes
            },
            IncludedSections = includedSections,
            Counts = new ConfigurationBackupSectionCounts
            {
                Agents = document.Sections.Agents?.Count ?? 0,
                Endpoints = document.Sections.Endpoints?.Count ?? 0,
                Assignments = document.Sections.Assignments?.Count ?? 0,
                IdentityUsers = document.Sections.Identity?.Users.Count ?? 0,
                IdentityRoles = document.Sections.Identity?.Roles.Count ?? 0,
                IdentityUserRoles = document.Sections.Identity?.UserRoles.Count ?? 0
            }
        };

        _logger.LogInformation(
            "Loaded restore preview for {FileId}. Included sections: {Sections}.",
            fileId,
            string.Join(",", includedSections));

        return preview;
    }

    private static IEnumerable<string> GetIncludedSections(ConfigurationBackupDocument document)
    {
        if (document.Sections.Agents is not null)
        {
            yield return ConfigurationBackupSections.Agents;
        }

        if (document.Sections.Endpoints is not null)
        {
            yield return ConfigurationBackupSections.Endpoints;
        }

        if (document.Sections.Assignments is not null)
        {
            yield return ConfigurationBackupSections.Assignments;
        }

        if (document.Sections.Identity is not null)
        {
            yield return ConfigurationBackupSections.Identity;
        }
    }
}

namespace PingMonitor.Web.Services.Backups;
using PingMonitor.Web.Support;

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
                Groups = document.Sections.Groups?.Groups.Count ?? 0,
                GroupEndpointMemberships = document.Sections.Groups?.EndpointMemberships.Count ?? 0,
                Dependencies = document.Sections.Dependencies?.Count ?? document.Sections.Endpoints?.Sum(x => x.DependsOnEndpointIds?.Count ?? 0) ?? 0,
                Assignments = document.Sections.Assignments?.Count ?? 0,
                SecuritySettings = document.Sections.SecuritySettings is null ? 0 : 1,
                NotificationSettings = document.Sections.NotificationSettings is null ? 0 : 1,
                UserNotificationSettings = document.Sections.UserNotificationSettings?.Count ?? 0,
                IdentityUsers = document.Sections.Identity?.Users.Count ?? 0,
                IdentityRoles = document.Sections.Identity?.Roles.Count ?? 0,
                IdentityUserRoles = document.Sections.Identity?.UserRoles.Count ?? 0
            }
        };

        _logger.LogInformation(
            "Loaded restore preview for {FileId}. Included sections: {Sections}.",
            LogValueSanitizer.ForLog(fileId),
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

        if (document.Sections.Groups is not null)
        {
            yield return ConfigurationBackupSections.Groups;
        }

        if (document.Sections.Dependencies is not null
            || document.Sections.Endpoints?.Any(x => x.DependsOnEndpointIds is { Count: > 0 }) == true)
        {
            yield return ConfigurationBackupSections.Dependencies;
        }

        if (document.Sections.Assignments is not null)
        {
            yield return ConfigurationBackupSections.Assignments;
        }

        if (document.Sections.SecuritySettings is not null)
        {
            yield return ConfigurationBackupSections.SecuritySettings;
        }

        if (document.Sections.NotificationSettings is not null)
        {
            yield return ConfigurationBackupSections.NotificationSettings;
        }

        if (document.Sections.UserNotificationSettings is not null)
        {
            yield return ConfigurationBackupSections.UserNotificationSettings;
        }

        if (document.Sections.Identity is not null)
        {
            yield return ConfigurationBackupSections.Identity;
        }
    }
}

namespace PingMonitor.Web.Services.Backups;

public interface IConfigurationBackupDocumentValidator
{
    void Validate(ConfigurationBackupDocument document, string fileIdForErrors);
}

public sealed class ConfigurationBackupDocumentValidator : IConfigurationBackupDocumentValidator
{
    public void Validate(ConfigurationBackupDocument document, string fileIdForErrors)
    {
        _ = fileIdForErrors;

        if (document.FormatVersion <= 0 || document.FormatVersion > ConfigurationBackupMetadata.CurrentFormatVersion)
        {
            throw new InvalidOperationException($"Backup formatVersion {document.FormatVersion} is not supported.");
        }

        if (string.IsNullOrWhiteSpace(document.BackupName)
            || string.IsNullOrWhiteSpace(document.AppVersion)
            || document.ExportedAtUtc == default)
        {
            throw new InvalidOperationException("Backup metadata is incomplete.");
        }

        if (!string.IsNullOrWhiteSpace(document.BackupSource)
            && !ConfigurationBackupSources.All.Contains(document.BackupSource, StringComparer.Ordinal))
        {
            throw new InvalidOperationException("Backup source metadata is not supported.");
        }

        if (document.Sections is null)
        {
            throw new InvalidOperationException("Backup sections are missing.");
        }

        var hasAtLeastOneSection =
            document.Sections.Agents is not null
            || document.Sections.Endpoints is not null
            || document.Sections.Groups is not null
            || document.Sections.Dependencies is not null
            || document.Sections.Assignments is not null
            || document.Sections.SecuritySettings is not null
            || document.Sections.NotificationSettings is not null
            || document.Sections.UserNotificationSettings is not null
            || document.Sections.Identity is not null;

        if (!hasAtLeastOneSection)
        {
            throw new InvalidOperationException("Backup does not contain any recognized configuration sections.");
        }

        if (document.Sections.Agents is not null)
        {
            foreach (var agent in document.Sections.Agents)
            {
                if (string.IsNullOrWhiteSpace(agent.InstanceId))
                {
                    throw new InvalidOperationException("Agent section contains an invalid record (instanceId missing).");
                }
            }
        }

        if (document.Sections.Endpoints is not null)
        {
            foreach (var endpoint in document.Sections.Endpoints)
            {
                if (string.IsNullOrWhiteSpace(endpoint.Name) || string.IsNullOrWhiteSpace(endpoint.Target))
                {
                    throw new InvalidOperationException("Endpoint section contains an invalid record (name/target missing).");
                }
            }
        }

        if (document.Sections.Groups is not null)
        {
            foreach (var group in document.Sections.Groups.Groups)
            {
                if (string.IsNullOrWhiteSpace(group.Name))
                {
                    throw new InvalidOperationException("Groups section contains an invalid record (name missing).");
                }
            }
        }

        if (document.Sections.Dependencies is not null)
        {
            foreach (var dependency in document.Sections.Dependencies)
            {
                if (string.IsNullOrWhiteSpace(dependency.EndpointId)
                    || string.IsNullOrWhiteSpace(dependency.DependsOnEndpointId))
                {
                    throw new InvalidOperationException("Dependencies section contains an invalid record.");
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
                    throw new InvalidOperationException("Assignment section contains an invalid record.");
                }
            }
        }

        if (document.Sections.Identity is not null)
        {
            foreach (var user in document.Sections.Identity.Users)
            {
                if (string.IsNullOrWhiteSpace(user.NormalizedUserName) && string.IsNullOrWhiteSpace(user.NormalizedEmail))
                {
                    throw new InvalidOperationException("Identity section contains a user without normalized username/email.");
                }
            }
        }

        if (document.Sections.UserNotificationSettings is not null)
        {
            foreach (var userSettings in document.Sections.UserNotificationSettings)
            {
                if (string.IsNullOrWhiteSpace(userSettings.UserId))
                {
                    throw new InvalidOperationException("User notification settings section contains an invalid record (userId missing).");
                }
            }
        }
    }
}

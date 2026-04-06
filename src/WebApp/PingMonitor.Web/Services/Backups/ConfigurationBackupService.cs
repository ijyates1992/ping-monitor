using System.Reflection;
using System.Text.Json;
using Microsoft.AspNetCore.Hosting;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Options;
using PingMonitor.Web.Data;
using PingMonitor.Web.Models;
using PingMonitor.Web.Options;

namespace PingMonitor.Web.Services.Backups;

public interface IConfigurationBackupService
{
    Task<CreateConfigurationBackupResponse> CreateBackupAsync(CreateConfigurationBackupRequest request, CancellationToken cancellationToken);
}

public sealed class ConfigurationBackupService : IConfigurationBackupService
{
    private static readonly JsonSerializerOptions SerializerOptions = new()
    {
        WriteIndented = true
    };

    private readonly PingMonitorDbContext _dbContext;
    private readonly IWebHostEnvironment _environment;
    private readonly BackupOptions _options;
    private readonly IConfigurationBackupFileNameGenerator _fileNameGenerator;
    private readonly IConfigurationBackupCatalogService _catalogService;

    public ConfigurationBackupService(
        PingMonitorDbContext dbContext,
        IWebHostEnvironment environment,
        IOptions<BackupOptions> options,
        IConfigurationBackupFileNameGenerator fileNameGenerator,
        IConfigurationBackupCatalogService catalogService)
    {
        _dbContext = dbContext;
        _environment = environment;
        _options = options.Value;
        _fileNameGenerator = fileNameGenerator;
        _catalogService = catalogService;
    }

    public async Task<CreateConfigurationBackupResponse> CreateBackupAsync(CreateConfigurationBackupRequest request, CancellationToken cancellationToken)
    {
        if (string.IsNullOrWhiteSpace(request.BackupName))
        {
            throw new InvalidOperationException("Backup name is required.");
        }

        if (request.SelectedSections.Count == 0)
        {
            throw new InvalidOperationException("At least one section must be selected.");
        }

        var selectedSections = request.SelectedSections
            .Select(section => section.Trim().ToLowerInvariant())
            .Where(section => ConfigurationBackupSections.All.Contains(section, StringComparer.Ordinal))
            .Distinct(StringComparer.Ordinal)
            .ToArray();

        if (selectedSections.Length == 0)
        {
            throw new InvalidOperationException("At least one valid section must be selected.");
        }

        var exportedAtUtc = DateTimeOffset.UtcNow;
        var appVersion = Assembly.GetExecutingAssembly().GetName().Version?.ToString() ?? "unknown";
        var metadata = new ConfigurationBackupMetadata
        {
            AppVersion = appVersion,
            BackupName = request.BackupName.Trim(),
            Notes = string.IsNullOrWhiteSpace(request.Notes) ? null : request.Notes.Trim(),
            ExportedAtUtc = exportedAtUtc,
            ExportedBy = request.ExportedBy,
            MachineName = Environment.MachineName,
            BackupSource = NormalizeSource(request.BackupSource)
        };

        var sections = new ConfigurationBackupSectionData
        {
            Agents = selectedSections.Contains(ConfigurationBackupSections.Agents, StringComparer.Ordinal)
                ? await LoadAgentsAsync(cancellationToken)
                : null,
            Endpoints = selectedSections.Contains(ConfigurationBackupSections.Endpoints, StringComparer.Ordinal)
                ? await LoadEndpointsAsync(cancellationToken)
                : null,
            Groups = selectedSections.Contains(ConfigurationBackupSections.Groups, StringComparer.Ordinal)
                ? await LoadGroupsAsync(cancellationToken)
                : null,
            Dependencies = selectedSections.Contains(ConfigurationBackupSections.Dependencies, StringComparer.Ordinal)
                ? await LoadDependenciesAsync(cancellationToken)
                : null,
            Assignments = selectedSections.Contains(ConfigurationBackupSections.Assignments, StringComparer.Ordinal)
                ? await LoadAssignmentsAsync(cancellationToken)
                : null,
            SecuritySettings = selectedSections.Contains(ConfigurationBackupSections.SecuritySettings, StringComparer.Ordinal)
                ? await LoadSecuritySettingsAsync(cancellationToken)
                : null,
            NotificationSettings = selectedSections.Contains(ConfigurationBackupSections.NotificationSettings, StringComparer.Ordinal)
                ? await LoadNotificationSettingsAsync(cancellationToken)
                : null,
            UserNotificationSettings = selectedSections.Contains(ConfigurationBackupSections.UserNotificationSettings, StringComparer.Ordinal)
                ? await LoadUserNotificationSettingsAsync(cancellationToken)
                : null,
            Identity = selectedSections.Contains(ConfigurationBackupSections.Identity, StringComparer.Ordinal)
                ? await LoadIdentityAsync(cancellationToken)
                : null
        };

        var document = ConfigurationBackupDocument.Create(metadata, sections);

        var storagePath = ResolveStoragePath();
        Directory.CreateDirectory(storagePath);

        var fileName = _fileNameGenerator.CreateFileName(metadata.BackupName, metadata.AppVersion, metadata.ExportedAtUtc);
        var fullPath = Path.Combine(storagePath, fileName);

        var json = JsonSerializer.Serialize(document, SerializerOptions);
        await File.WriteAllTextAsync(fullPath, json, cancellationToken);
        await _catalogService.UpsertSourceAsync(fileName, metadata.BackupSource, cancellationToken);

        return new CreateConfigurationBackupResponse
        {
            FileName = fileName,
            FileId = fileName,
            BackupName = metadata.BackupName,
            ExportedAtUtc = metadata.ExportedAtUtc,
            IncludedSections = selectedSections
        };
    }

    private static string NormalizeSource(string? source)
    {
        if (string.IsNullOrWhiteSpace(source))
        {
            return ConfigurationBackupSources.Manual;
        }

        var trimmed = source.Trim().ToLowerInvariant();
        return ConfigurationBackupSources.All.Contains(trimmed, StringComparer.Ordinal)
            ? trimmed
            : ConfigurationBackupSources.Manual;
    }

    private string ResolveStoragePath()
    {
        return Path.IsPathRooted(_options.StoragePath)
            ? _options.StoragePath
            : Path.Combine(_environment.ContentRootPath, _options.StoragePath);
    }

    private async Task<IReadOnlyList<BackupAgentRecord>> LoadAgentsAsync(CancellationToken cancellationToken)
    {
        return await _dbContext.Agents
            .AsNoTracking()
            .OrderBy(x => x.InstanceId)
            .Select(x => new BackupAgentRecord
            {
                AgentId = x.AgentId,
                InstanceId = x.InstanceId,
                Name = x.Name,
                Site = x.Site,
                Enabled = x.Enabled,
                AgentVersion = x.AgentVersion,
                Platform = x.Platform,
                MachineName = x.MachineName,
                CreatedAtUtc = x.CreatedAtUtc
            })
            .ToListAsync(cancellationToken);
    }

    private async Task<IReadOnlyList<BackupEndpointRecord>> LoadEndpointsAsync(CancellationToken cancellationToken)
    {
        var endpoints = await _dbContext.Endpoints
            .AsNoTracking()
            .OrderBy(x => x.Name)
            .ToListAsync(cancellationToken);

        return endpoints
            .Select(x => new BackupEndpointRecord
            {
                EndpointId = x.EndpointId,
                Name = x.Name,
                Target = x.Target,
                IconKey = x.IconKey,
                Enabled = x.Enabled,
                Tags = x.Tags,
                Notes = x.Notes,
                CreatedAtUtc = x.CreatedAtUtc
            })
            .ToArray();
    }

    private async Task<BackupGroupSection> LoadGroupsAsync(CancellationToken cancellationToken)
    {
        var groups = await _dbContext.Groups
            .AsNoTracking()
            .OrderBy(x => x.Name)
            .Select(x => new BackupGroupRecord
            {
                GroupId = x.GroupId,
                Name = x.Name,
                Description = x.Description,
                CreatedAtUtc = x.CreatedAtUtc
            })
            .ToListAsync(cancellationToken);

        var memberships = await _dbContext.EndpointGroupMemberships
            .AsNoTracking()
            .OrderBy(x => x.GroupId)
            .ThenBy(x => x.EndpointId)
            .Select(x => new BackupEndpointGroupMembershipRecord
            {
                EndpointId = x.EndpointId,
                GroupId = x.GroupId,
                CreatedAtUtc = x.CreatedAtUtc
            })
            .ToListAsync(cancellationToken);

        return new BackupGroupSection
        {
            Groups = groups,
            EndpointMemberships = memberships
        };
    }

    private async Task<IReadOnlyList<BackupEndpointDependencyRecord>> LoadDependenciesAsync(CancellationToken cancellationToken)
    {
        return await _dbContext.EndpointDependencies
            .AsNoTracking()
            .OrderBy(x => x.EndpointId)
            .ThenBy(x => x.DependsOnEndpointId)
            .Select(x => new BackupEndpointDependencyRecord
            {
                EndpointId = x.EndpointId,
                DependsOnEndpointId = x.DependsOnEndpointId,
                CreatedAtUtc = x.CreatedAtUtc
            })
            .ToListAsync(cancellationToken);
    }

    private async Task<IReadOnlyList<BackupAssignmentRecord>> LoadAssignmentsAsync(CancellationToken cancellationToken)
    {
        return await _dbContext.MonitorAssignments
            .AsNoTracking()
            .OrderBy(x => x.AgentId)
            .ThenBy(x => x.EndpointId)
            .Select(x => new BackupAssignmentRecord
            {
                AssignmentId = x.AssignmentId,
                AgentId = x.AgentId,
                EndpointId = x.EndpointId,
                CheckType = x.CheckType.ToString().ToLowerInvariant(),
                Enabled = x.Enabled,
                PingIntervalSeconds = x.PingIntervalSeconds,
                RetryIntervalSeconds = x.RetryIntervalSeconds,
                TimeoutMs = x.TimeoutMs,
                FailureThreshold = x.FailureThreshold,
                RecoveryThreshold = x.RecoveryThreshold,
                CreatedAtUtc = x.CreatedAtUtc,
                UpdatedAtUtc = x.UpdatedAtUtc
            })
            .ToListAsync(cancellationToken);
    }

    private async Task<BackupIdentitySection> LoadIdentityAsync(CancellationToken cancellationToken)
    {
        var users = await _dbContext.Users
            .AsNoTracking()
            .OrderBy(x => x.UserName)
            .Select(x => new BackupIdentityUserRecord
            {
                Id = x.Id,
                UserName = x.UserName,
                NormalizedUserName = x.NormalizedUserName,
                Email = x.Email,
                NormalizedEmail = x.NormalizedEmail,
                EmailConfirmed = x.EmailConfirmed,
                LockoutEnabled = x.LockoutEnabled,
                LockoutEnd = x.LockoutEnd,
                AccessFailedCount = x.AccessFailedCount
            })
            .ToListAsync(cancellationToken);

        var roles = await _dbContext.Roles
            .AsNoTracking()
            .OrderBy(x => x.Name)
            .Select(x => new BackupIdentityRoleRecord
            {
                Id = x.Id,
                Name = x.Name,
                NormalizedName = x.NormalizedName
            })
            .ToListAsync(cancellationToken);

        var userRoles = await _dbContext.UserRoles
            .AsNoTracking()
            .OrderBy(x => x.UserId)
            .ThenBy(x => x.RoleId)
            .Select(x => new BackupIdentityUserRoleRecord
            {
                UserId = x.UserId,
                RoleId = x.RoleId
            })
            .ToListAsync(cancellationToken);

        return new BackupIdentitySection
        {
            Users = users,
            Roles = roles,
            UserRoles = userRoles
        };
    }

    private async Task<BackupSecuritySettingsRecord?> LoadSecuritySettingsAsync(CancellationToken cancellationToken)
    {
        var settings = await _dbContext.SecuritySettings
            .AsNoTracking()
            .SingleOrDefaultAsync(x => x.SecuritySettingsId == SecuritySettings.SingletonId, cancellationToken);
        if (settings is null)
        {
            return null;
        }

        return new BackupSecuritySettingsRecord
        {
            AgentFailedAttemptsBeforeTemporaryIpBlock = settings.AgentFailedAttemptsBeforeTemporaryIpBlock,
            AgentTemporaryIpBlockDurationMinutes = settings.AgentTemporaryIpBlockDurationMinutes,
            AgentFailedAttemptsBeforePermanentIpBlock = settings.AgentFailedAttemptsBeforePermanentIpBlock,
            UserFailedAttemptsBeforeTemporaryIpBlock = settings.UserFailedAttemptsBeforeTemporaryIpBlock,
            UserTemporaryIpBlockDurationMinutes = settings.UserTemporaryIpBlockDurationMinutes,
            UserFailedAttemptsBeforePermanentIpBlock = settings.UserFailedAttemptsBeforePermanentIpBlock,
            UserFailedAttemptsBeforeTemporaryAccountLockout = settings.UserFailedAttemptsBeforeTemporaryAccountLockout,
            UserTemporaryAccountLockoutDurationMinutes = settings.UserTemporaryAccountLockoutDurationMinutes,
            SecurityLogRetentionEnabled = settings.SecurityLogRetentionEnabled,
            SecurityLogRetentionDays = settings.SecurityLogRetentionDays,
            SecurityLogAutoPruneEnabled = settings.SecurityLogAutoPruneEnabled,
            UpdatedAtUtc = settings.UpdatedAtUtc
        };
    }

    private async Task<BackupNotificationSettingsRecord?> LoadNotificationSettingsAsync(CancellationToken cancellationToken)
    {
        var settings = await _dbContext.NotificationSettings
            .AsNoTracking()
            .SingleOrDefaultAsync(x => x.NotificationSettingsId == NotificationSettings.SingletonId, cancellationToken);
        if (settings is null)
        {
            return null;
        }

        return new BackupNotificationSettingsRecord
        {
            BrowserNotificationsEnabled = settings.BrowserNotificationsEnabled,
            BrowserNotifyEndpointDown = settings.BrowserNotifyEndpointDown,
            BrowserNotifyEndpointRecovered = settings.BrowserNotifyEndpointRecovered,
            BrowserNotifyAgentOffline = settings.BrowserNotifyAgentOffline,
            BrowserNotifyAgentOnline = settings.BrowserNotifyAgentOnline,
            BrowserNotificationsPermissionState = settings.BrowserNotificationsPermissionState,
            TelegramEnabled = settings.TelegramEnabled,
            TelegramBotTokenProtected = settings.TelegramBotTokenProtected,
            TelegramInboundMode = settings.TelegramInboundMode.ToString().ToLowerInvariant(),
            TelegramPollIntervalSeconds = settings.TelegramPollIntervalSeconds,
            TelegramLastProcessedUpdateId = settings.TelegramLastProcessedUpdateId,
            TelegramWebhookUrl = settings.TelegramWebhookUrl,
            TelegramWebhookSecretToken = settings.TelegramWebhookSecretToken,
            QuietHoursEnabled = settings.QuietHoursEnabled,
            QuietHoursStartLocalTime = settings.QuietHoursStartLocalTime,
            QuietHoursEndLocalTime = settings.QuietHoursEndLocalTime,
            QuietHoursTimeZoneId = settings.QuietHoursTimeZoneId,
            QuietHoursSuppressBrowserNotifications = settings.QuietHoursSuppressBrowserNotifications,
            QuietHoursSuppressSmtpNotifications = settings.QuietHoursSuppressSmtpNotifications,
            SmtpNotificationsEnabled = settings.SmtpNotificationsEnabled,
            SmtpHost = settings.SmtpHost,
            SmtpPort = settings.SmtpPort,
            SmtpUseTls = settings.SmtpUseTls,
            SmtpUsername = settings.SmtpUsername,
            SmtpPasswordProtected = settings.SmtpPasswordProtected,
            SmtpFromAddress = settings.SmtpFromAddress,
            SmtpFromDisplayName = settings.SmtpFromDisplayName,
            SmtpRecipientAddresses = settings.SmtpRecipientAddresses,
            SmtpNotifyEndpointDown = settings.SmtpNotifyEndpointDown,
            SmtpNotifyEndpointRecovered = settings.SmtpNotifyEndpointRecovered,
            SmtpNotifyAgentOffline = settings.SmtpNotifyAgentOffline,
            SmtpNotifyAgentOnline = settings.SmtpNotifyAgentOnline,
            UpdatedAtUtc = settings.UpdatedAtUtc,
            UpdatedByUserId = settings.UpdatedByUserId
        };
    }

    private async Task<IReadOnlyList<BackupUserNotificationSettingsRecord>> LoadUserNotificationSettingsAsync(CancellationToken cancellationToken)
    {
        return await _dbContext.UserNotificationSettings
            .AsNoTracking()
            .OrderBy(x => x.UserId)
            .Select(x => new BackupUserNotificationSettingsRecord
            {
                UserId = x.UserId,
                BrowserNotificationsEnabled = x.BrowserNotificationsEnabled,
                BrowserNotifyEndpointDown = x.BrowserNotifyEndpointDown,
                BrowserNotifyEndpointRecovered = x.BrowserNotifyEndpointRecovered,
                BrowserNotifyAgentOffline = x.BrowserNotifyAgentOffline,
                BrowserNotifyAgentOnline = x.BrowserNotifyAgentOnline,
                BrowserNotificationsPermissionState = x.BrowserNotificationsPermissionState,
                SmtpNotificationsEnabled = x.SmtpNotificationsEnabled,
                SmtpNotifyEndpointDown = x.SmtpNotifyEndpointDown,
                SmtpNotifyEndpointRecovered = x.SmtpNotifyEndpointRecovered,
                SmtpNotifyAgentOffline = x.SmtpNotifyAgentOffline,
                SmtpNotifyAgentOnline = x.SmtpNotifyAgentOnline,
                TelegramNotificationsEnabled = x.TelegramNotificationsEnabled,
                TelegramNotifyEndpointDown = x.TelegramNotifyEndpointDown,
                TelegramNotifyEndpointRecovered = x.TelegramNotifyEndpointRecovered,
                TelegramNotifyAgentOffline = x.TelegramNotifyAgentOffline,
                TelegramNotifyAgentOnline = x.TelegramNotifyAgentOnline,
                QuietHoursSuppressTelegramNotifications = x.QuietHoursSuppressTelegramNotifications,
                QuietHoursEnabled = x.QuietHoursEnabled,
                QuietHoursStartLocalTime = x.QuietHoursStartLocalTime,
                QuietHoursEndLocalTime = x.QuietHoursEndLocalTime,
                QuietHoursTimeZoneId = x.QuietHoursTimeZoneId,
                QuietHoursSuppressBrowserNotifications = x.QuietHoursSuppressBrowserNotifications,
                QuietHoursSuppressSmtpNotifications = x.QuietHoursSuppressSmtpNotifications,
                UpdatedAtUtc = x.UpdatedAtUtc
            })
            .ToListAsync(cancellationToken);
    }
}

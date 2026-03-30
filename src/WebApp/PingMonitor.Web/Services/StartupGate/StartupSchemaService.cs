using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Options;
using MySqlConnector;
using PingMonitor.Web.Data;
using PingMonitor.Web.Models;
using PingMonitor.Web.Options;

namespace PingMonitor.Web.Services.StartupGate;

internal sealed class StartupSchemaService : IStartupSchemaService
{
    private static readonly string[] RequiredTables =
    [
        "AppSchemaInfo",
        "AspNetRoles",
        "AspNetUserRoles",
        "AspNetUsers",
        "Agents",
        "AgentHeartbeatHistory",
        "Endpoints",
        "EndpointDependencies",
        "Groups",
        "EndpointGroupMemberships",
        "UserGroupAccesses",
        "UserEndpointAccesses",
        "MonitorAssignments",
        "CheckResults",
        "ResultBatches",
        "EndpointStates",
        "StateTransitions",
        "EventLogs",
        "SecurityAuthLogs",
        "ApplicationSettings",
        "SecuritySettings",
        "SecurityIpBlocks",
        "NotificationSettings",
        "UserNotificationSettings",
        "PendingTelegramLinks",
        "TelegramAccounts"
    ];
    private static readonly string[] RequiredEndpointDependencyColumns =
    [
        "EndpointDependencyId",
        "EndpointId",
        "DependsOnEndpointId",
        "CreatedAtUtc"
    ];
    private static readonly string[] RequiredEndpointColumns =
    [
        "EndpointId",
        "Name",
        "Target",
        "Enabled",
        "Tags",
        "IconKey"
    ];
    private static readonly string[] RequiredSecuritySettingsColumns =
    [
        "SecuritySettingsId",
        "AgentFailedAttemptsBeforeTemporaryIpBlock",
        "AgentTemporaryIpBlockDurationMinutes",
        "AgentFailedAttemptsBeforePermanentIpBlock",
        "UserFailedAttemptsBeforeTemporaryIpBlock",
        "UserTemporaryIpBlockDurationMinutes",
        "UserFailedAttemptsBeforePermanentIpBlock",
        "UserFailedAttemptsBeforeTemporaryAccountLockout",
        "UserTemporaryAccountLockoutDurationMinutes",
        "SecurityLogRetentionEnabled",
        "SecurityLogRetentionDays",
        "SecurityLogAutoPruneEnabled",
        "UpdatedAtUtc"
    ];
    private static readonly string[] RequiredNotificationSettingsColumns =
    [
        "NotificationSettingsId",
        "BrowserNotificationsEnabled",
        "BrowserNotifyEndpointDown",
        "BrowserNotifyEndpointRecovered",
        "BrowserNotifyAgentOffline",
        "BrowserNotifyAgentOnline",
        "BrowserNotificationsPermissionState",
        "TelegramEnabled",
        "TelegramBotTokenProtected",
        "TelegramInboundMode",
        "TelegramPollIntervalSeconds",
        "TelegramLastProcessedUpdateId",
        "TelegramWebhookUrl",
        "TelegramWebhookSecretToken",
        "QuietHoursEnabled",
        "QuietHoursStartLocalTime",
        "QuietHoursEndLocalTime",
        "QuietHoursTimeZoneId",
        "QuietHoursSuppressBrowserNotifications",
        "QuietHoursSuppressSmtpNotifications",
        "QuietHoursSuppressTelegramNotifications",
        "SmtpNotificationsEnabled",
        "SmtpHost",
        "SmtpPort",
        "SmtpUseTls",
        "SmtpUsername",
        "SmtpPasswordProtected",
        "SmtpFromAddress",
        "SmtpFromDisplayName",
        "SmtpRecipientAddresses",
        "SmtpNotifyEndpointDown",
        "SmtpNotifyEndpointRecovered",
        "SmtpNotifyAgentOffline",
        "SmtpNotifyAgentOnline",
        "UpdatedAtUtc",
        "UpdatedByUserId"
    ];
    private static readonly string[] RequiredUserNotificationSettingsColumns =
    [
        "UserId",
        "BrowserNotificationsEnabled",
        "BrowserNotifyEndpointDown",
        "BrowserNotifyEndpointRecovered",
        "BrowserNotifyAgentOffline",
        "BrowserNotifyAgentOnline",
        "BrowserNotificationsPermissionState",
        "SmtpNotificationsEnabled",
        "SmtpNotifyEndpointDown",
        "SmtpNotifyEndpointRecovered",
        "SmtpNotifyAgentOffline",
        "SmtpNotifyAgentOnline",
        "TelegramNotificationsEnabled",
        "TelegramNotifyEndpointDown",
        "TelegramNotifyEndpointRecovered",
        "TelegramNotifyAgentOffline",
        "TelegramNotifyAgentOnline",
        "QuietHoursEnabled",
        "QuietHoursStartLocalTime",
        "QuietHoursEndLocalTime",
        "QuietHoursTimeZoneId",
        "QuietHoursSuppressBrowserNotifications",
        "QuietHoursSuppressSmtpNotifications",
        "QuietHoursSuppressTelegramNotifications",
        "UpdatedAtUtc"
    ];

    private readonly IDbContextFactory<PingMonitorDbContext> _dbContextFactory;
    private readonly IStartupDatabaseConfigurationStore _configurationStore;
    private readonly StartupGateOptions _options;
    private readonly ILogger<StartupSchemaService> _logger;

    public StartupSchemaService(
        IDbContextFactory<PingMonitorDbContext> dbContextFactory,
        IStartupDatabaseConfigurationStore configurationStore,
        IOptions<StartupGateOptions> options,
        ILogger<StartupSchemaService> logger)
    {
        _dbContextFactory = dbContextFactory;
        _configurationStore = configurationStore;
        _options = options.Value;
        _logger = logger;
    }

    public async Task<StartupSchemaStatus> GetStatusAsync(CancellationToken cancellationToken)
    {
        var configuration = await _configurationStore.LoadAsync(cancellationToken);
        var password = await _configurationStore.LoadPasswordAsync(cancellationToken);
        if (configuration is null || !configuration.IsComplete || string.IsNullOrWhiteSpace(password))
        {
            return new StartupSchemaStatus
            {
                State = StartupGateSchemaState.Unknown,
                Diagnostics = { "Database configuration is incomplete." }
            };
        }

        var connectionString = _configurationStore.BuildConnectionString(configuration, password);
        await using var connection = new MySqlConnection(connectionString);
        await connection.OpenAsync(cancellationToken);

        var existingTables = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        await using (var command = connection.CreateCommand())
        {
            command.CommandText = "SELECT TABLE_NAME FROM information_schema.tables WHERE table_schema = @schema;";
            command.Parameters.AddWithValue("@schema", configuration.DatabaseName);

            await using var reader = await command.ExecuteReaderAsync(cancellationToken);
            while (await reader.ReadAsync(cancellationToken))
            {
                existingTables.Add(reader.GetString(0));
            }
        }

        var missingTables = RequiredTables.Where(table => !existingTables.Contains(table)).ToArray();
        if (missingTables.Length > 0)
        {
            var status = new StartupSchemaStatus { State = StartupGateSchemaState.Missing };
            status.Diagnostics.Add($"Missing required tables: {string.Join(", ", missingTables)}.");
            return status;
        }

        var missingEndpointDependencyColumns = await GetMissingEndpointDependencyColumnsAsync(connection, cancellationToken);
        if (missingEndpointDependencyColumns.Length > 0)
        {
            var status = new StartupSchemaStatus { State = StartupGateSchemaState.Incompatible };
            status.Diagnostics.Add($"EndpointDependencies table is missing required columns: {string.Join(", ", missingEndpointDependencyColumns)}.");
            return status;
        }
        var missingEndpointColumns = await GetMissingEndpointColumnsAsync(connection, cancellationToken);
        if (missingEndpointColumns.Length > 0)
        {
            var status = new StartupSchemaStatus { State = StartupGateSchemaState.Incompatible };
            status.Diagnostics.Add($"Endpoints table is missing required columns: {string.Join(", ", missingEndpointColumns)}.");
            return status;
        }
        var missingSecuritySettingsColumns = await GetMissingSecuritySettingsColumnsAsync(connection, cancellationToken);
        if (missingSecuritySettingsColumns.Length > 0)
        {
            var status = new StartupSchemaStatus { State = StartupGateSchemaState.Incompatible };
            status.Diagnostics.Add($"SecuritySettings table is missing required columns: {string.Join(", ", missingSecuritySettingsColumns)}.");
            return status;
        }
        var missingNotificationSettingsColumns = await GetMissingNotificationSettingsColumnsAsync(connection, cancellationToken);
        if (missingNotificationSettingsColumns.Length > 0)
        {
            var status = new StartupSchemaStatus { State = StartupGateSchemaState.Incompatible };
            status.Diagnostics.Add($"NotificationSettings table is missing required columns: {string.Join(", ", missingNotificationSettingsColumns)}.");
            return status;
        }
        var missingUserNotificationSettingsColumns = await GetMissingUserNotificationSettingsColumnsAsync(connection, cancellationToken);
        if (missingUserNotificationSettingsColumns.Length > 0)
        {
            var status = new StartupSchemaStatus { State = StartupGateSchemaState.Incompatible };
            status.Diagnostics.Add($"UserNotificationSettings table is missing required columns: {string.Join(", ", missingUserNotificationSettingsColumns)}.");
            return status;
        }

        await using var dbContext = await _dbContextFactory.CreateDbContextAsync(cancellationToken);
        var schemaInfo = await dbContext.AppSchemaInfos.OrderByDescending(x => x.AppSchemaInfoId).FirstOrDefaultAsync(cancellationToken);
        if (schemaInfo is null)
        {
            return new StartupSchemaStatus
            {
                State = StartupGateSchemaState.Missing,
                Diagnostics = { "AppSchemaInfo row is missing." }
            };
        }

        if (schemaInfo.CurrentSchemaVersion != _options.RequiredSchemaVersion)
        {
            return new StartupSchemaStatus
            {
                State = StartupGateSchemaState.Incompatible,
                CurrentSchemaVersion = schemaInfo.CurrentSchemaVersion,
                Diagnostics = { $"Schema version {schemaInfo.CurrentSchemaVersion} does not match required version {_options.RequiredSchemaVersion}." }
            };
        }

        return new StartupSchemaStatus
        {
            State = StartupGateSchemaState.Compatible,
            CurrentSchemaVersion = schemaInfo.CurrentSchemaVersion,
            Diagnostics = { "Schema is present and compatible." }
        };
    }

    public async Task<StartupSchemaStatus> ApplySchemaAsync(CancellationToken cancellationToken)
    {
        _logger.LogInformation("Startup gate schema apply requested.");

        await using var dbContext = await _dbContextFactory.CreateDbContextAsync(cancellationToken);
        await dbContext.Database.EnsureCreatedAsync(cancellationToken);
        await EnsureAdditionalTablesAsync(dbContext, cancellationToken);

        var schemaInfo = await dbContext.AppSchemaInfos.OrderByDescending(x => x.AppSchemaInfoId).FirstOrDefaultAsync(cancellationToken);
        if (schemaInfo is null)
        {
            schemaInfo = new AppSchemaInfo
            {
                CurrentSchemaVersion = _options.RequiredSchemaVersion,
                UpdatedAtUtc = DateTimeOffset.UtcNow
            };
            dbContext.AppSchemaInfos.Add(schemaInfo);
        }
        else
        {
            schemaInfo.CurrentSchemaVersion = _options.RequiredSchemaVersion;
            schemaInfo.UpdatedAtUtc = DateTimeOffset.UtcNow;
        }

        await dbContext.SaveChangesAsync(cancellationToken);
        _logger.LogInformation("Startup gate schema apply completed successfully.");

        return await GetStatusAsync(cancellationToken);
    }

    private static async Task EnsureAdditionalTablesAsync(PingMonitorDbContext dbContext, CancellationToken cancellationToken)
    {
        const string createEndpointStatesSql = """
            CREATE TABLE IF NOT EXISTS `EndpointStates` (
                `AssignmentId` varchar(64) NOT NULL,
                `CurrentState` varchar(16) NOT NULL,
                `ConsecutiveFailureCount` int NOT NULL,
                `ConsecutiveSuccessCount` int NOT NULL,
                `LastCheckUtc` datetime(6) NULL,
                `LastStateChangeUtc` datetime(6) NULL,
                `SuppressedByEndpointId` varchar(64) NULL,
                `AgentId` varchar(64) NOT NULL,
                `EndpointId` varchar(64) NOT NULL,
                PRIMARY KEY (`AssignmentId`)
            );
            """;

        const string createStateTransitionsSql = """
            CREATE TABLE IF NOT EXISTS `StateTransitions` (
                `TransitionId` varchar(64) NOT NULL,
                `AssignmentId` varchar(64) NOT NULL,
                `AgentId` varchar(64) NOT NULL,
                `EndpointId` varchar(64) NOT NULL,
                `PreviousState` varchar(16) NOT NULL,
                `NewState` varchar(16) NOT NULL,
                `TransitionAtUtc` datetime(6) NOT NULL,
                `ReasonCode` varchar(64) NULL,
                `DependencyEndpointId` varchar(64) NULL,
                PRIMARY KEY (`TransitionId`)
            );
            """;

        const string createApplicationSettingsSql = """
            CREATE TABLE IF NOT EXISTS `ApplicationSettings` (
                `ApplicationSettingsId` int NOT NULL,
                `SiteUrl` varchar(2048) NOT NULL,
                `DefaultPingIntervalSeconds` int NOT NULL,
                `DefaultRetryIntervalSeconds` int NOT NULL,
                `DefaultTimeoutMs` int NOT NULL,
                `DefaultFailureThreshold` int NOT NULL,
                `DefaultRecoveryThreshold` int NOT NULL,
                `UpdatedAtUtc` datetime(6) NOT NULL,
                PRIMARY KEY (`ApplicationSettingsId`)
            );
            """;

        const string createSecuritySettingsSql = """
            CREATE TABLE IF NOT EXISTS `SecuritySettings` (
                `SecuritySettingsId` int NOT NULL,
                `AgentFailedAttemptsBeforeTemporaryIpBlock` int NOT NULL,
                `AgentTemporaryIpBlockDurationMinutes` int NOT NULL,
                `AgentFailedAttemptsBeforePermanentIpBlock` int NOT NULL,
                `UserFailedAttemptsBeforeTemporaryIpBlock` int NOT NULL,
                `UserTemporaryIpBlockDurationMinutes` int NOT NULL,
                `UserFailedAttemptsBeforePermanentIpBlock` int NOT NULL,
                `UserFailedAttemptsBeforeTemporaryAccountLockout` int NOT NULL,
                `UserTemporaryAccountLockoutDurationMinutes` int NOT NULL,
                `SecurityLogRetentionEnabled` tinyint(1) NOT NULL DEFAULT 0,
                `SecurityLogRetentionDays` int NOT NULL DEFAULT 90,
                `SecurityLogAutoPruneEnabled` tinyint(1) NOT NULL DEFAULT 0,
                `UpdatedAtUtc` datetime(6) NOT NULL,
                PRIMARY KEY (`SecuritySettingsId`)
            );
            """;

        const string createSecurityIpBlocksSql = """
            CREATE TABLE IF NOT EXISTS `SecurityIpBlocks` (
                `SecurityIpBlockId` varchar(64) NOT NULL,
                `AuthType` varchar(16) NOT NULL,
                `IpAddress` varchar(64) NOT NULL,
                `BlockType` varchar(16) NOT NULL,
                `BlockedAtUtc` datetime(6) NOT NULL,
                `ExpiresAtUtc` datetime(6) NULL,
                `Reason` varchar(512) NULL,
                `CreatedByUserId` varchar(255) NULL,
                `RemovedAtUtc` datetime(6) NULL,
                `RemovedByUserId` varchar(255) NULL,
                PRIMARY KEY (`SecurityIpBlockId`),
                KEY `IX_SecurityIpBlocks_AuthType_IpAddress_RemovedAtUtc` (`AuthType`, `IpAddress`, `RemovedAtUtc`),
                KEY `IX_SecurityIpBlocks_BlockedAtUtc` (`BlockedAtUtc`)
            );
            """;

        const string createNotificationSettingsSql = """
            CREATE TABLE IF NOT EXISTS `NotificationSettings` (
                `NotificationSettingsId` int NOT NULL,
                `BrowserNotificationsEnabled` tinyint(1) NOT NULL,
                `BrowserNotifyEndpointDown` tinyint(1) NOT NULL DEFAULT 1,
                `BrowserNotifyEndpointRecovered` tinyint(1) NOT NULL DEFAULT 1,
                `BrowserNotifyAgentOffline` tinyint(1) NOT NULL DEFAULT 1,
                `BrowserNotifyAgentOnline` tinyint(1) NOT NULL DEFAULT 1,
                `BrowserNotificationsPermissionState` varchar(16) NULL,
                `TelegramEnabled` tinyint(1) NOT NULL DEFAULT 0,
                `TelegramBotTokenProtected` varchar(4096) NULL,
                `TelegramInboundMode` varchar(16) NOT NULL DEFAULT 'Polling',
                `TelegramPollIntervalSeconds` int NOT NULL DEFAULT 10,
                `TelegramLastProcessedUpdateId` bigint NOT NULL DEFAULT 0,
                `TelegramWebhookUrl` varchar(2048) NULL,
                `TelegramWebhookSecretToken` varchar(512) NULL,
                `QuietHoursEnabled` tinyint(1) NOT NULL DEFAULT 0,
                `QuietHoursStartLocalTime` varchar(5) NOT NULL DEFAULT '22:00',
                `QuietHoursEndLocalTime` varchar(5) NOT NULL DEFAULT '07:00',
                `QuietHoursTimeZoneId` varchar(128) NOT NULL DEFAULT 'UTC',
                `QuietHoursSuppressBrowserNotifications` tinyint(1) NOT NULL DEFAULT 1,
                `QuietHoursSuppressSmtpNotifications` tinyint(1) NOT NULL DEFAULT 1,
                `QuietHoursSuppressTelegramNotifications` tinyint(1) NOT NULL DEFAULT 1,
                `SmtpNotificationsEnabled` tinyint(1) NOT NULL,
                `SmtpHost` varchar(255) NULL,
                `SmtpPort` int NOT NULL DEFAULT 25,
                `SmtpUseTls` tinyint(1) NOT NULL DEFAULT 1,
                `SmtpUsername` varchar(255) NULL,
                `SmtpPasswordProtected` varchar(4096) NULL,
                `SmtpFromAddress` varchar(255) NULL,
                `SmtpFromDisplayName` varchar(255) NULL,
                `SmtpRecipientAddresses` varchar(4096) NULL,
                `SmtpNotifyEndpointDown` tinyint(1) NOT NULL DEFAULT 1,
                `SmtpNotifyEndpointRecovered` tinyint(1) NOT NULL DEFAULT 1,
                `SmtpNotifyAgentOffline` tinyint(1) NOT NULL DEFAULT 1,
                `SmtpNotifyAgentOnline` tinyint(1) NOT NULL DEFAULT 1,
                `UpdatedAtUtc` datetime(6) NOT NULL,
                `UpdatedByUserId` varchar(255) NULL,
                PRIMARY KEY (`NotificationSettingsId`)
            );
            """;
        const string createUserNotificationSettingsSql = """
            CREATE TABLE IF NOT EXISTS `UserNotificationSettings` (
                `UserId` varchar(255) NOT NULL,
                `BrowserNotificationsEnabled` tinyint(1) NOT NULL DEFAULT 0,
                `BrowserNotifyEndpointDown` tinyint(1) NOT NULL DEFAULT 1,
                `BrowserNotifyEndpointRecovered` tinyint(1) NOT NULL DEFAULT 1,
                `BrowserNotifyAgentOffline` tinyint(1) NOT NULL DEFAULT 1,
                `BrowserNotifyAgentOnline` tinyint(1) NOT NULL DEFAULT 1,
                `BrowserNotificationsPermissionState` varchar(16) NULL,
                `SmtpNotificationsEnabled` tinyint(1) NOT NULL DEFAULT 0,
                `SmtpNotifyEndpointDown` tinyint(1) NOT NULL DEFAULT 1,
                `SmtpNotifyEndpointRecovered` tinyint(1) NOT NULL DEFAULT 1,
                `SmtpNotifyAgentOffline` tinyint(1) NOT NULL DEFAULT 1,
                `SmtpNotifyAgentOnline` tinyint(1) NOT NULL DEFAULT 1,
                `TelegramNotificationsEnabled` tinyint(1) NOT NULL DEFAULT 0,
                `TelegramNotifyEndpointDown` tinyint(1) NOT NULL DEFAULT 1,
                `TelegramNotifyEndpointRecovered` tinyint(1) NOT NULL DEFAULT 1,
                `TelegramNotifyAgentOffline` tinyint(1) NOT NULL DEFAULT 1,
                `TelegramNotifyAgentOnline` tinyint(1) NOT NULL DEFAULT 1,
                `QuietHoursEnabled` tinyint(1) NOT NULL DEFAULT 0,
                `QuietHoursStartLocalTime` varchar(5) NOT NULL DEFAULT '22:00',
                `QuietHoursEndLocalTime` varchar(5) NOT NULL DEFAULT '07:00',
                `QuietHoursTimeZoneId` varchar(128) NOT NULL DEFAULT 'UTC',
                `QuietHoursSuppressBrowserNotifications` tinyint(1) NOT NULL DEFAULT 1,
                `QuietHoursSuppressSmtpNotifications` tinyint(1) NOT NULL DEFAULT 1,
                `QuietHoursSuppressTelegramNotifications` tinyint(1) NOT NULL DEFAULT 1,
                `UpdatedAtUtc` datetime(6) NOT NULL,
                PRIMARY KEY (`UserId`)
            );
            """;


        const string createPendingTelegramLinksSql = """
            CREATE TABLE IF NOT EXISTS `PendingTelegramLinks` (
                `PendingTelegramLinkId` varchar(64) NOT NULL,
                `UserId` varchar(255) NOT NULL,
                `Code` varchar(16) NOT NULL,
                `CreatedAtUtc` datetime(6) NOT NULL,
                `ExpiresAtUtc` datetime(6) NOT NULL,
                `UsedAtUtc` datetime(6) NULL,
                `ConsumedByChatId` varchar(64) NULL,
                `Status` varchar(16) NOT NULL,
                PRIMARY KEY (`PendingTelegramLinkId`),
                KEY `IX_PendingTelegramLinks_Code_Status` (`Code`, `Status`)
            );
            """;

        const string createTelegramAccountsSql = """
            CREATE TABLE IF NOT EXISTS `TelegramAccounts` (
                `TelegramAccountId` varchar(64) NOT NULL,
                `UserId` varchar(255) NOT NULL,
                `ChatId` varchar(64) NOT NULL,
                `Verified` tinyint(1) NOT NULL,
                `LinkedAtUtc` datetime(6) NOT NULL,
                `Username` varchar(255) NULL,
                `DisplayName` varchar(255) NULL,
                `IsActive` tinyint(1) NOT NULL DEFAULT 1,
                PRIMARY KEY (`TelegramAccountId`),
                UNIQUE KEY `UX_TelegramAccounts_UserId` (`UserId`),
                UNIQUE KEY `UX_TelegramAccounts_ChatId` (`ChatId`)
            );
            """;

        const string createEventLogsSql = """
            CREATE TABLE IF NOT EXISTS `EventLogs` (
                `EventLogId` varchar(64) NOT NULL,
                `OccurredAtUtc` datetime(6) NOT NULL,
                `EventCategory` varchar(32) NOT NULL,
                `EventType` varchar(128) NOT NULL,
                `Severity` varchar(16) NOT NULL,
                `AgentId` varchar(64) NULL,
                `EndpointId` varchar(64) NULL,
                `AssignmentId` varchar(64) NULL,
                `Message` varchar(2048) NOT NULL,
                `DetailsJson` varchar(8192) NULL,
                PRIMARY KEY (`EventLogId`),
                KEY `IX_EventLogs_OccurredAtUtc` (`OccurredAtUtc`),
                KEY `IX_EventLogs_EndpointId_OccurredAtUtc` (`EndpointId`, `OccurredAtUtc`),
                KEY `IX_EventLogs_AgentId_OccurredAtUtc` (`AgentId`, `OccurredAtUtc`),
                KEY `IX_EventLogs_AssignmentId_OccurredAtUtc` (`AssignmentId`, `OccurredAtUtc`)
            );
            """;
        const string createSecurityAuthLogsSql = """
            CREATE TABLE IF NOT EXISTS `SecurityAuthLogs` (
                `SecurityAuthLogId` varchar(64) NOT NULL,
                `OccurredAtUtc` datetime(6) NOT NULL,
                `AuthType` varchar(16) NOT NULL,
                `SubjectIdentifier` varchar(255) NOT NULL,
                `SourceIpAddress` varchar(64) NULL,
                `Success` tinyint(1) NOT NULL,
                `FailureReason` varchar(128) NULL,
                `UserId` varchar(255) NULL,
                `AgentId` varchar(64) NULL,
                `DetailsJson` varchar(4096) NULL,
                PRIMARY KEY (`SecurityAuthLogId`),
                KEY `IX_SecurityAuthLogs_OccurredAtUtc` (`OccurredAtUtc`),
                KEY `IX_SecurityAuthLogs_AuthType_OccurredAtUtc` (`AuthType`, `OccurredAtUtc`),
                KEY `IX_SecurityAuthLogs_AuthType_Success_OccurredAtUtc` (`AuthType`, `Success`, `OccurredAtUtc`),
                KEY `IX_SecurityAuthLogs_AuthType_SourceIpAddress_OccurredAtUtc` (`AuthType`, `SourceIpAddress`, `OccurredAtUtc`)
            );
            """;

        const string createEndpointDependenciesSql = """
            CREATE TABLE IF NOT EXISTS `EndpointDependencies` (
                `EndpointDependencyId` varchar(64) NOT NULL,
                `EndpointId` varchar(64) NOT NULL,
                `DependsOnEndpointId` varchar(64) NOT NULL,
                `CreatedAtUtc` datetime(6) NOT NULL,
                PRIMARY KEY (`EndpointDependencyId`),
                UNIQUE KEY `UX_EndpointDependencies_EndpointId_DependsOnEndpointId` (`EndpointId`, `DependsOnEndpointId`)
            );
            """;

        const string createAgentHeartbeatHistorySql = """
            CREATE TABLE IF NOT EXISTS `AgentHeartbeatHistory` (
                `AgentHeartbeatHistoryId` varchar(64) NOT NULL,
                `AgentId` varchar(64) NOT NULL,
                `HeartbeatAtUtc` datetime(6) NOT NULL,
                `RecordedAtUtc` datetime(6) NOT NULL,
                PRIMARY KEY (`AgentHeartbeatHistoryId`),
                KEY `IX_AgentHeartbeatHistory_AgentId_HeartbeatAtUtc` (`AgentId`, `HeartbeatAtUtc`)
            );
            """;

        const string createGroupsSql = """
            CREATE TABLE IF NOT EXISTS `Groups` (
                `GroupId` varchar(64) NOT NULL,
                `Name` varchar(255) NOT NULL,
                `Description` varchar(2048) NULL,
                `CreatedAtUtc` datetime(6) NOT NULL,
                PRIMARY KEY (`GroupId`),
                UNIQUE KEY `UX_Groups_Name` (`Name`)
            );
            """;

        const string createEndpointGroupMembershipsSql = """
            CREATE TABLE IF NOT EXISTS `EndpointGroupMemberships` (
                `EndpointGroupMembershipId` varchar(64) NOT NULL,
                `EndpointId` varchar(64) NOT NULL,
                `GroupId` varchar(64) NOT NULL,
                `CreatedAtUtc` datetime(6) NOT NULL,
                PRIMARY KEY (`EndpointGroupMembershipId`),
                UNIQUE KEY `UX_EndpointGroupMemberships_EndpointId_GroupId` (`EndpointId`, `GroupId`)
            );
            """;

        const string createUserGroupAccessesSql = """
            CREATE TABLE IF NOT EXISTS `UserGroupAccesses` (
                `UserGroupAccessId` varchar(64) NOT NULL,
                `UserId` varchar(255) NOT NULL,
                `GroupId` varchar(64) NOT NULL,
                `CreatedAtUtc` datetime(6) NOT NULL,
                PRIMARY KEY (`UserGroupAccessId`),
                UNIQUE KEY `UX_UserGroupAccesses_UserId_GroupId` (`UserId`, `GroupId`)
            );
            """;

        const string createUserEndpointAccessesSql = """
            CREATE TABLE IF NOT EXISTS `UserEndpointAccesses` (
                `UserEndpointAccessId` varchar(64) NOT NULL,
                `UserId` varchar(255) NOT NULL,
                `EndpointId` varchar(64) NOT NULL,
                `CreatedAtUtc` datetime(6) NOT NULL,
                PRIMARY KEY (`UserEndpointAccessId`),
                UNIQUE KEY `UX_UserEndpointAccesses_UserId_EndpointId` (`UserId`, `EndpointId`)
            );
            """;

        await dbContext.Database.ExecuteSqlRawAsync(createEndpointStatesSql, cancellationToken);
        await dbContext.Database.ExecuteSqlRawAsync(createStateTransitionsSql, cancellationToken);
        await dbContext.Database.ExecuteSqlRawAsync(createEventLogsSql, cancellationToken);
        await dbContext.Database.ExecuteSqlRawAsync(createSecurityAuthLogsSql, cancellationToken);
        await dbContext.Database.ExecuteSqlRawAsync(createApplicationSettingsSql, cancellationToken);
        await dbContext.Database.ExecuteSqlRawAsync(createSecuritySettingsSql, cancellationToken);
        await dbContext.Database.ExecuteSqlRawAsync(createSecurityIpBlocksSql, cancellationToken);
        await dbContext.Database.ExecuteSqlRawAsync(createNotificationSettingsSql, cancellationToken);
        await dbContext.Database.ExecuteSqlRawAsync(createUserNotificationSettingsSql, cancellationToken);
        await dbContext.Database.ExecuteSqlRawAsync(createPendingTelegramLinksSql, cancellationToken);
        await dbContext.Database.ExecuteSqlRawAsync(createTelegramAccountsSql, cancellationToken);
        await dbContext.Database.ExecuteSqlRawAsync(createEndpointDependenciesSql, cancellationToken);
        await dbContext.Database.ExecuteSqlRawAsync(createAgentHeartbeatHistorySql, cancellationToken);
        await dbContext.Database.ExecuteSqlRawAsync(createGroupsSql, cancellationToken);
        await dbContext.Database.ExecuteSqlRawAsync(createEndpointGroupMembershipsSql, cancellationToken);
        await dbContext.Database.ExecuteSqlRawAsync(createUserGroupAccessesSql, cancellationToken);
        await dbContext.Database.ExecuteSqlRawAsync(createUserEndpointAccessesSql, cancellationToken);
        await EnsureEndpointDependenciesColumnsAsync(dbContext, cancellationToken);
        await EnsureEndpointColumnsAsync(dbContext, cancellationToken);
        await EnsureSecuritySettingsColumnsAsync(dbContext, cancellationToken);
        await EnsureNotificationSettingsColumnsAsync(dbContext, cancellationToken);
        await EnsureUserNotificationSettingsColumnsAsync(dbContext, cancellationToken);
        await EnsureAgentColumnsAsync(dbContext, cancellationToken);
        await MigrateLegacyEndpointDependenciesAsync(dbContext, cancellationToken);
    }

    private static async Task<string[]> GetMissingEndpointDependencyColumnsAsync(MySqlConnection connection, CancellationToken cancellationToken)
    {
        var existingColumns = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        await using var command = connection.CreateCommand();
        command.CommandText = """
            SELECT COLUMN_NAME
            FROM information_schema.columns
            WHERE table_schema = DATABASE()
              AND table_name = 'EndpointDependencies';
            """;

        await using var reader = await command.ExecuteReaderAsync(cancellationToken);
        while (await reader.ReadAsync(cancellationToken))
        {
            existingColumns.Add(reader.GetString(0));
        }

        return RequiredEndpointDependencyColumns
            .Where(column => !existingColumns.Contains(column))
            .ToArray();
    }

    private static async Task<string[]> GetMissingEndpointColumnsAsync(MySqlConnection connection, CancellationToken cancellationToken)
    {
        var existingColumns = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        await using var command = connection.CreateCommand();
        command.CommandText = """
            SELECT COLUMN_NAME
            FROM information_schema.columns
            WHERE table_schema = DATABASE()
              AND table_name = 'Endpoints';
            """;

        await using var reader = await command.ExecuteReaderAsync(cancellationToken);
        while (await reader.ReadAsync(cancellationToken))
        {
            existingColumns.Add(reader.GetString(0));
        }

        return RequiredEndpointColumns
            .Where(column => !existingColumns.Contains(column))
            .ToArray();
    }

    private static async Task<string[]> GetMissingSecuritySettingsColumnsAsync(MySqlConnection connection, CancellationToken cancellationToken)
    {
        var existingColumns = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        await using var command = connection.CreateCommand();
        command.CommandText = """
            SELECT COLUMN_NAME
            FROM information_schema.columns
            WHERE table_schema = DATABASE()
              AND table_name = 'SecuritySettings';
            """;

        await using var reader = await command.ExecuteReaderAsync(cancellationToken);
        while (await reader.ReadAsync(cancellationToken))
        {
            existingColumns.Add(reader.GetString(0));
        }

        return RequiredSecuritySettingsColumns
            .Where(column => !existingColumns.Contains(column))
            .ToArray();
    }

    private static async Task<string[]> GetMissingNotificationSettingsColumnsAsync(MySqlConnection connection, CancellationToken cancellationToken)
    {
        var existingColumns = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        await using var command = connection.CreateCommand();
        command.CommandText = """
            SELECT COLUMN_NAME
            FROM information_schema.columns
            WHERE table_schema = DATABASE()
              AND table_name = 'NotificationSettings';
            """;

        await using var reader = await command.ExecuteReaderAsync(cancellationToken);
        while (await reader.ReadAsync(cancellationToken))
        {
            existingColumns.Add(reader.GetString(0));
        }

        return RequiredNotificationSettingsColumns
            .Where(column => !existingColumns.Contains(column))
            .ToArray();
    }

    private static async Task<string[]> GetMissingUserNotificationSettingsColumnsAsync(MySqlConnection connection, CancellationToken cancellationToken)
    {
        var existingColumns = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        await using var command = connection.CreateCommand();
        command.CommandText = """
            SELECT COLUMN_NAME
            FROM information_schema.columns
            WHERE table_schema = DATABASE()
              AND table_name = 'UserNotificationSettings';
            """;

        await using var reader = await command.ExecuteReaderAsync(cancellationToken);
        while (await reader.ReadAsync(cancellationToken))
        {
            existingColumns.Add(reader.GetString(0));
        }

        return RequiredUserNotificationSettingsColumns
            .Where(column => !existingColumns.Contains(column))
            .ToArray();
    }

    private static async Task EnsureEndpointDependenciesColumnsAsync(PingMonitorDbContext dbContext, CancellationToken cancellationToken)
    {
        await using var connection = dbContext.Database.GetDbConnection();
        if (connection.State != System.Data.ConnectionState.Open)
        {
            await connection.OpenAsync(cancellationToken);
        }

        var hasDependsOnEndpointId = await HasEndpointDependencyColumnAsync(connection, "DependsOnEndpointId", cancellationToken);
        var hasParentEndpointId = await HasEndpointDependencyColumnAsync(connection, "ParentEndpointId", cancellationToken);

        if (!hasDependsOnEndpointId && hasParentEndpointId)
        {
            await dbContext.Database.ExecuteSqlRawAsync(
                """
                ALTER TABLE `EndpointDependencies`
                ADD COLUMN `DependsOnEndpointId` varchar(64) NULL;
                """,
                cancellationToken);

            await dbContext.Database.ExecuteSqlRawAsync(
                """
                UPDATE `EndpointDependencies`
                SET `DependsOnEndpointId` = `ParentEndpointId`
                WHERE (`DependsOnEndpointId` IS NULL OR `DependsOnEndpointId` = '')
                  AND `ParentEndpointId` IS NOT NULL
                  AND `ParentEndpointId` <> '';
                """,
                cancellationToken);

            await dbContext.Database.ExecuteSqlRawAsync(
                """
                ALTER TABLE `EndpointDependencies`
                MODIFY COLUMN `DependsOnEndpointId` varchar(64) NOT NULL;
                """,
                cancellationToken);
        }

        var hasEndpointDependencyId = await HasEndpointDependencyColumnAsync(connection, "EndpointDependencyId", cancellationToken);
        if (!hasEndpointDependencyId)
        {
            await dbContext.Database.ExecuteSqlRawAsync(
                """
                ALTER TABLE `EndpointDependencies`
                ADD COLUMN `EndpointDependencyId` varchar(64) NULL;
                """,
                cancellationToken);

            await dbContext.Database.ExecuteSqlRawAsync(
                """
                UPDATE `EndpointDependencies`
                SET `EndpointDependencyId` = UUID()
                WHERE `EndpointDependencyId` IS NULL OR `EndpointDependencyId` = '';
                """,
                cancellationToken);

            await dbContext.Database.ExecuteSqlRawAsync(
                """
                ALTER TABLE `EndpointDependencies`
                MODIFY COLUMN `EndpointDependencyId` varchar(64) NOT NULL;
                """,
                cancellationToken);

            if (await HasPrimaryKeyAsync(connection, "EndpointDependencies", cancellationToken))
            {
                await dbContext.Database.ExecuteSqlRawAsync(
                    """
                    ALTER TABLE `EndpointDependencies`
                    DROP PRIMARY KEY;
                    """,
                    cancellationToken);
            }

            await dbContext.Database.ExecuteSqlRawAsync(
                """
                ALTER TABLE `EndpointDependencies`
                ADD PRIMARY KEY (`EndpointDependencyId`);
                """,
                cancellationToken);
        }

        var hasCreatedAtUtc = await HasEndpointDependencyColumnAsync(connection, "CreatedAtUtc", cancellationToken);
        if (!hasCreatedAtUtc)
        {
            await dbContext.Database.ExecuteSqlRawAsync(
                """
                ALTER TABLE `EndpointDependencies`
                ADD COLUMN `CreatedAtUtc` datetime(6) NULL;
                """,
                cancellationToken);

            await dbContext.Database.ExecuteSqlRawAsync(
                """
                UPDATE `EndpointDependencies`
                SET `CreatedAtUtc` = UTC_TIMESTAMP(6)
                WHERE `CreatedAtUtc` IS NULL;
                """,
                cancellationToken);

            await dbContext.Database.ExecuteSqlRawAsync(
                """
                ALTER TABLE `EndpointDependencies`
                MODIFY COLUMN `CreatedAtUtc` datetime(6) NOT NULL;
                """,
                cancellationToken);
        }

        if (!await HasEndpointDependencyIndexAsync(connection, "UX_EndpointDependencies_EndpointId_DependsOnEndpointId", cancellationToken))
        {
            await dbContext.Database.ExecuteSqlRawAsync(
                """
                CREATE UNIQUE INDEX `UX_EndpointDependencies_EndpointId_DependsOnEndpointId`
                ON `EndpointDependencies` (`EndpointId`, `DependsOnEndpointId`);
                """,
                cancellationToken);
        }
    }

    private static async Task EnsureAgentColumnsAsync(PingMonitorDbContext dbContext, CancellationToken cancellationToken)
    {
        await using var connection = dbContext.Database.GetDbConnection();
        if (connection.State != System.Data.ConnectionState.Open)
        {
            await connection.OpenAsync(cancellationToken);
        }

        if (!await HasAgentColumnAsync(connection, "LastHeartbeatEventLoggedAtUtc", cancellationToken))
        {
            await dbContext.Database.ExecuteSqlRawAsync(
                """
                ALTER TABLE `Agents`
                ADD COLUMN `LastHeartbeatEventLoggedAtUtc` datetime(6) NULL;
                """,
                cancellationToken);
        }
    }

    private static async Task EnsureSecuritySettingsColumnsAsync(PingMonitorDbContext dbContext, CancellationToken cancellationToken)
    {
        await using var connection = dbContext.Database.GetDbConnection();
        if (connection.State != System.Data.ConnectionState.Open)
        {
            await connection.OpenAsync(cancellationToken);
        }

        if (!await HasSecuritySettingsColumnAsync(connection, "SecurityLogRetentionEnabled", cancellationToken))
        {
            await dbContext.Database.ExecuteSqlRawAsync(
                """
                ALTER TABLE `SecuritySettings`
                ADD COLUMN `SecurityLogRetentionEnabled` tinyint(1) NOT NULL DEFAULT 0;
                """,
                cancellationToken);
        }

        if (!await HasSecuritySettingsColumnAsync(connection, "SecurityLogRetentionDays", cancellationToken))
        {
            await dbContext.Database.ExecuteSqlRawAsync(
                """
                ALTER TABLE `SecuritySettings`
                ADD COLUMN `SecurityLogRetentionDays` int NOT NULL DEFAULT 90;
                """,
                cancellationToken);
        }

        if (!await HasSecuritySettingsColumnAsync(connection, "SecurityLogAutoPruneEnabled", cancellationToken))
        {
            await dbContext.Database.ExecuteSqlRawAsync(
                """
                ALTER TABLE `SecuritySettings`
                ADD COLUMN `SecurityLogAutoPruneEnabled` tinyint(1) NOT NULL DEFAULT 0;
                """,
                cancellationToken);
        }
    }

    private static async Task EnsureNotificationSettingsColumnsAsync(PingMonitorDbContext dbContext, CancellationToken cancellationToken)
    {
        await using var connection = dbContext.Database.GetDbConnection();
        if (connection.State != System.Data.ConnectionState.Open)
        {
            await connection.OpenAsync(cancellationToken);
        }

        if (!await HasNotificationSettingsColumnAsync(connection, "BrowserNotifyEndpointDown", cancellationToken))
        {
            await dbContext.Database.ExecuteSqlRawAsync(
                """
                ALTER TABLE `NotificationSettings`
                ADD COLUMN `BrowserNotifyEndpointDown` tinyint(1) NOT NULL DEFAULT 1;
                """,
                cancellationToken);
        }

        if (!await HasNotificationSettingsColumnAsync(connection, "BrowserNotifyEndpointRecovered", cancellationToken))
        {
            await dbContext.Database.ExecuteSqlRawAsync(
                """
                ALTER TABLE `NotificationSettings`
                ADD COLUMN `BrowserNotifyEndpointRecovered` tinyint(1) NOT NULL DEFAULT 1;
                """,
                cancellationToken);
        }

        if (!await HasNotificationSettingsColumnAsync(connection, "BrowserNotifyAgentOffline", cancellationToken))
        {
            await dbContext.Database.ExecuteSqlRawAsync(
                """
                ALTER TABLE `NotificationSettings`
                ADD COLUMN `BrowserNotifyAgentOffline` tinyint(1) NOT NULL DEFAULT 1;
                """,
                cancellationToken);
        }

        if (!await HasNotificationSettingsColumnAsync(connection, "BrowserNotifyAgentOnline", cancellationToken))
        {
            await dbContext.Database.ExecuteSqlRawAsync(
                """
                ALTER TABLE `NotificationSettings`
                ADD COLUMN `BrowserNotifyAgentOnline` tinyint(1) NOT NULL DEFAULT 1;
                """,
                cancellationToken);
        }


        if (!await HasNotificationSettingsColumnAsync(connection, "TelegramEnabled", cancellationToken))
        {
            await dbContext.Database.ExecuteSqlRawAsync(
                """
                ALTER TABLE `NotificationSettings`
                ADD COLUMN `TelegramEnabled` tinyint(1) NOT NULL DEFAULT 0;
                """,
                cancellationToken);
        }

        if (!await HasNotificationSettingsColumnAsync(connection, "TelegramBotTokenProtected", cancellationToken))
        {
            await dbContext.Database.ExecuteSqlRawAsync(
                """
                ALTER TABLE `NotificationSettings`
                ADD COLUMN `TelegramBotTokenProtected` varchar(4096) NULL;
                """,
                cancellationToken);
        }

        if (!await HasNotificationSettingsColumnAsync(connection, "TelegramInboundMode", cancellationToken))
        {
            await dbContext.Database.ExecuteSqlRawAsync(
                """
                ALTER TABLE `NotificationSettings`
                ADD COLUMN `TelegramInboundMode` varchar(16) NOT NULL DEFAULT 'Polling';
                """,
                cancellationToken);
        }

        if (!await HasNotificationSettingsColumnAsync(connection, "TelegramPollIntervalSeconds", cancellationToken))
        {
            await dbContext.Database.ExecuteSqlRawAsync(
                """
                ALTER TABLE `NotificationSettings`
                ADD COLUMN `TelegramPollIntervalSeconds` int NOT NULL DEFAULT 10;
                """,
                cancellationToken);
        }

        if (!await HasNotificationSettingsColumnAsync(connection, "TelegramLastProcessedUpdateId", cancellationToken))
        {
            await dbContext.Database.ExecuteSqlRawAsync(
                """
                ALTER TABLE `NotificationSettings`
                ADD COLUMN `TelegramLastProcessedUpdateId` bigint NOT NULL DEFAULT 0;
                """,
                cancellationToken);
        }

        if (!await HasNotificationSettingsColumnAsync(connection, "TelegramWebhookUrl", cancellationToken))
        {
            await dbContext.Database.ExecuteSqlRawAsync(
                """
                ALTER TABLE `NotificationSettings`
                ADD COLUMN `TelegramWebhookUrl` varchar(2048) NULL;
                """,
                cancellationToken);
        }

        if (!await HasNotificationSettingsColumnAsync(connection, "TelegramWebhookSecretToken", cancellationToken))
        {
            await dbContext.Database.ExecuteSqlRawAsync(
                """
                ALTER TABLE `NotificationSettings`
                ADD COLUMN `TelegramWebhookSecretToken` varchar(512) NULL;
                """,
                cancellationToken);
        }

        if (!await HasNotificationSettingsColumnAsync(connection, "SmtpHost", cancellationToken))
        {
            await dbContext.Database.ExecuteSqlRawAsync(
                """
                ALTER TABLE `NotificationSettings`
                ADD COLUMN `SmtpHost` varchar(255) NULL;
                """,
                cancellationToken);
        }

        if (!await HasNotificationSettingsColumnAsync(connection, "QuietHoursEnabled", cancellationToken))
        {
            await dbContext.Database.ExecuteSqlRawAsync(
                """
                ALTER TABLE `NotificationSettings`
                ADD COLUMN `QuietHoursEnabled` tinyint(1) NOT NULL DEFAULT 0;
                """,
                cancellationToken);
        }

        if (!await HasNotificationSettingsColumnAsync(connection, "QuietHoursStartLocalTime", cancellationToken))
        {
            await dbContext.Database.ExecuteSqlRawAsync(
                """
                ALTER TABLE `NotificationSettings`
                ADD COLUMN `QuietHoursStartLocalTime` varchar(5) NOT NULL DEFAULT '22:00';
                """,
                cancellationToken);
        }

        if (!await HasNotificationSettingsColumnAsync(connection, "QuietHoursEndLocalTime", cancellationToken))
        {
            await dbContext.Database.ExecuteSqlRawAsync(
                """
                ALTER TABLE `NotificationSettings`
                ADD COLUMN `QuietHoursEndLocalTime` varchar(5) NOT NULL DEFAULT '07:00';
                """,
                cancellationToken);
        }

        if (!await HasNotificationSettingsColumnAsync(connection, "QuietHoursTimeZoneId", cancellationToken))
        {
            await dbContext.Database.ExecuteSqlRawAsync(
                """
                ALTER TABLE `NotificationSettings`
                ADD COLUMN `QuietHoursTimeZoneId` varchar(128) NOT NULL DEFAULT 'UTC';
                """,
                cancellationToken);
        }

        if (!await HasNotificationSettingsColumnAsync(connection, "QuietHoursSuppressBrowserNotifications", cancellationToken))
        {
            await dbContext.Database.ExecuteSqlRawAsync(
                """
                ALTER TABLE `NotificationSettings`
                ADD COLUMN `QuietHoursSuppressBrowserNotifications` tinyint(1) NOT NULL DEFAULT 1;
                """,
                cancellationToken);
        }

        if (!await HasNotificationSettingsColumnAsync(connection, "QuietHoursSuppressSmtpNotifications", cancellationToken))
        {
            await dbContext.Database.ExecuteSqlRawAsync(
                """
                ALTER TABLE `NotificationSettings`
                ADD COLUMN `QuietHoursSuppressSmtpNotifications` tinyint(1) NOT NULL DEFAULT 1;
                """,
                cancellationToken);
        }

        if (!await HasNotificationSettingsColumnAsync(connection, "QuietHoursSuppressTelegramNotifications", cancellationToken))
        {
            await dbContext.Database.ExecuteSqlRawAsync(
                """
                ALTER TABLE `NotificationSettings`
                ADD COLUMN `QuietHoursSuppressTelegramNotifications` tinyint(1) NOT NULL DEFAULT 1;
                """,
                cancellationToken);
        }

        if (!await HasNotificationSettingsColumnAsync(connection, "SmtpPort", cancellationToken))
        {
            await dbContext.Database.ExecuteSqlRawAsync(
                """
                ALTER TABLE `NotificationSettings`
                ADD COLUMN `SmtpPort` int NOT NULL DEFAULT 25;
                """,
                cancellationToken);
        }

        if (!await HasNotificationSettingsColumnAsync(connection, "SmtpUseTls", cancellationToken))
        {
            await dbContext.Database.ExecuteSqlRawAsync(
                """
                ALTER TABLE `NotificationSettings`
                ADD COLUMN `SmtpUseTls` tinyint(1) NOT NULL DEFAULT 1;
                """,
                cancellationToken);
        }

        if (!await HasNotificationSettingsColumnAsync(connection, "SmtpUsername", cancellationToken))
        {
            await dbContext.Database.ExecuteSqlRawAsync(
                """
                ALTER TABLE `NotificationSettings`
                ADD COLUMN `SmtpUsername` varchar(255) NULL;
                """,
                cancellationToken);
        }

        if (!await HasNotificationSettingsColumnAsync(connection, "SmtpPasswordProtected", cancellationToken))
        {
            await dbContext.Database.ExecuteSqlRawAsync(
                """
                ALTER TABLE `NotificationSettings`
                ADD COLUMN `SmtpPasswordProtected` varchar(4096) NULL;
                """,
                cancellationToken);
        }

        if (!await HasNotificationSettingsColumnAsync(connection, "SmtpFromAddress", cancellationToken))
        {
            await dbContext.Database.ExecuteSqlRawAsync(
                """
                ALTER TABLE `NotificationSettings`
                ADD COLUMN `SmtpFromAddress` varchar(255) NULL;
                """,
                cancellationToken);
        }

        if (!await HasNotificationSettingsColumnAsync(connection, "SmtpFromDisplayName", cancellationToken))
        {
            await dbContext.Database.ExecuteSqlRawAsync(
                """
                ALTER TABLE `NotificationSettings`
                ADD COLUMN `SmtpFromDisplayName` varchar(255) NULL;
                """,
                cancellationToken);
        }

        if (!await HasNotificationSettingsColumnAsync(connection, "SmtpRecipientAddresses", cancellationToken))
        {
            await dbContext.Database.ExecuteSqlRawAsync(
                """
                ALTER TABLE `NotificationSettings`
                ADD COLUMN `SmtpRecipientAddresses` varchar(4096) NULL;
                """,
                cancellationToken);
        }

        if (!await HasNotificationSettingsColumnAsync(connection, "SmtpNotifyEndpointDown", cancellationToken))
        {
            await dbContext.Database.ExecuteSqlRawAsync(
                """
                ALTER TABLE `NotificationSettings`
                ADD COLUMN `SmtpNotifyEndpointDown` tinyint(1) NOT NULL DEFAULT 1;
                """,
                cancellationToken);
        }

        if (!await HasNotificationSettingsColumnAsync(connection, "SmtpNotifyEndpointRecovered", cancellationToken))
        {
            await dbContext.Database.ExecuteSqlRawAsync(
                """
                ALTER TABLE `NotificationSettings`
                ADD COLUMN `SmtpNotifyEndpointRecovered` tinyint(1) NOT NULL DEFAULT 1;
                """,
                cancellationToken);
        }

        if (!await HasNotificationSettingsColumnAsync(connection, "SmtpNotifyAgentOffline", cancellationToken))
        {
            await dbContext.Database.ExecuteSqlRawAsync(
                """
                ALTER TABLE `NotificationSettings`
                ADD COLUMN `SmtpNotifyAgentOffline` tinyint(1) NOT NULL DEFAULT 1;
                """,
                cancellationToken);
        }

        if (!await HasNotificationSettingsColumnAsync(connection, "SmtpNotifyAgentOnline", cancellationToken))
        {
            await dbContext.Database.ExecuteSqlRawAsync(
                """
                ALTER TABLE `NotificationSettings`
                ADD COLUMN `SmtpNotifyAgentOnline` tinyint(1) NOT NULL DEFAULT 1;
                """,
                cancellationToken);
        }
    }

    private static async Task EnsureUserNotificationSettingsColumnsAsync(PingMonitorDbContext dbContext, CancellationToken cancellationToken)
    {
        await using var connection = dbContext.Database.GetDbConnection();
        if (connection.State != System.Data.ConnectionState.Open)
        {
            await connection.OpenAsync(cancellationToken);
        }

        if (!await HasUserNotificationSettingsColumnAsync(connection, "TelegramNotificationsEnabled", cancellationToken))
        {
            await dbContext.Database.ExecuteSqlRawAsync(
                """
                ALTER TABLE `UserNotificationSettings`
                ADD COLUMN `TelegramNotificationsEnabled` tinyint(1) NOT NULL DEFAULT 0;
                """,
                cancellationToken);
        }

        if (!await HasUserNotificationSettingsColumnAsync(connection, "TelegramNotifyEndpointDown", cancellationToken))
        {
            await dbContext.Database.ExecuteSqlRawAsync(
                """
                ALTER TABLE `UserNotificationSettings`
                ADD COLUMN `TelegramNotifyEndpointDown` tinyint(1) NOT NULL DEFAULT 1;
                """,
                cancellationToken);
        }

        if (!await HasUserNotificationSettingsColumnAsync(connection, "TelegramNotifyEndpointRecovered", cancellationToken))
        {
            await dbContext.Database.ExecuteSqlRawAsync(
                """
                ALTER TABLE `UserNotificationSettings`
                ADD COLUMN `TelegramNotifyEndpointRecovered` tinyint(1) NOT NULL DEFAULT 1;
                """,
                cancellationToken);
        }

        if (!await HasUserNotificationSettingsColumnAsync(connection, "TelegramNotifyAgentOffline", cancellationToken))
        {
            await dbContext.Database.ExecuteSqlRawAsync(
                """
                ALTER TABLE `UserNotificationSettings`
                ADD COLUMN `TelegramNotifyAgentOffline` tinyint(1) NOT NULL DEFAULT 1;
                """,
                cancellationToken);
        }

        if (!await HasUserNotificationSettingsColumnAsync(connection, "TelegramNotifyAgentOnline", cancellationToken))
        {
            await dbContext.Database.ExecuteSqlRawAsync(
                """
                ALTER TABLE `UserNotificationSettings`
                ADD COLUMN `TelegramNotifyAgentOnline` tinyint(1) NOT NULL DEFAULT 1;
                """,
                cancellationToken);
        }

        if (!await HasUserNotificationSettingsColumnAsync(connection, "QuietHoursSuppressTelegramNotifications", cancellationToken))
        {
            await dbContext.Database.ExecuteSqlRawAsync(
                """
                ALTER TABLE `UserNotificationSettings`
                ADD COLUMN `QuietHoursSuppressTelegramNotifications` tinyint(1) NOT NULL DEFAULT 1;
                """,
                cancellationToken);
        }
    }

    private static async Task EnsureEndpointColumnsAsync(PingMonitorDbContext dbContext, CancellationToken cancellationToken)
    {
        await using var connection = dbContext.Database.GetDbConnection();
        if (connection.State != System.Data.ConnectionState.Open)
        {
            await connection.OpenAsync(cancellationToken);
        }

        if (!await HasEndpointColumnAsync(connection, "IconKey", cancellationToken))
        {
            await dbContext.Database.ExecuteSqlRawAsync(
                """
                ALTER TABLE `Endpoints`
                ADD COLUMN `IconKey` varchar(64) NULL;
                """,
                cancellationToken);

            await dbContext.Database.ExecuteSqlRawAsync(
                """
                UPDATE `Endpoints`
                SET `IconKey` = 'generic'
                WHERE `IconKey` IS NULL OR `IconKey` = '';
                """,
                cancellationToken);

            await dbContext.Database.ExecuteSqlRawAsync(
                """
                ALTER TABLE `Endpoints`
                MODIFY COLUMN `IconKey` varchar(64) NOT NULL DEFAULT 'generic';
                """,
                cancellationToken);
        }
    }

    private static async Task<bool> HasEndpointColumnAsync(System.Data.Common.DbConnection connection, string columnName, CancellationToken cancellationToken)
    {
        await using var command = connection.CreateCommand();
        command.CommandText = """
            SELECT COUNT(*)
            FROM information_schema.columns
            WHERE table_schema = DATABASE()
              AND table_name = 'Endpoints'
              AND column_name = @columnName;
            """;
        var parameter = command.CreateParameter();
        parameter.ParameterName = "@columnName";
        parameter.Value = columnName;
        command.Parameters.Add(parameter);

        return Convert.ToInt32(await command.ExecuteScalarAsync(cancellationToken)) > 0;
    }

    private static async Task<bool> HasEndpointDependencyColumnAsync(System.Data.Common.DbConnection connection, string columnName, CancellationToken cancellationToken)
    {
        await using var command = connection.CreateCommand();
        command.CommandText = """
            SELECT COUNT(*)
            FROM information_schema.columns
            WHERE table_schema = DATABASE()
              AND table_name = 'EndpointDependencies'
              AND column_name = @columnName;
            """;
        var parameter = command.CreateParameter();
        parameter.ParameterName = "@columnName";
        parameter.Value = columnName;
        command.Parameters.Add(parameter);

        return Convert.ToInt32(await command.ExecuteScalarAsync(cancellationToken)) > 0;
    }

    private static async Task<bool> HasSecuritySettingsColumnAsync(System.Data.Common.DbConnection connection, string columnName, CancellationToken cancellationToken)
    {
        await using var command = connection.CreateCommand();
        command.CommandText = """
            SELECT COUNT(*)
            FROM information_schema.columns
            WHERE table_schema = DATABASE()
              AND table_name = 'SecuritySettings'
              AND column_name = @columnName;
            """;
        var parameter = command.CreateParameter();
        parameter.ParameterName = "@columnName";
        parameter.Value = columnName;
        command.Parameters.Add(parameter);

        return Convert.ToInt32(await command.ExecuteScalarAsync(cancellationToken)) > 0;
    }

    private static async Task<bool> HasNotificationSettingsColumnAsync(System.Data.Common.DbConnection connection, string columnName, CancellationToken cancellationToken)
    {
        await using var command = connection.CreateCommand();
        command.CommandText = """
            SELECT COUNT(*)
            FROM information_schema.columns
            WHERE table_schema = DATABASE()
              AND table_name = 'NotificationSettings'
              AND column_name = @columnName;
            """;
        var parameter = command.CreateParameter();
        parameter.ParameterName = "@columnName";
        parameter.Value = columnName;
        command.Parameters.Add(parameter);

        return Convert.ToInt32(await command.ExecuteScalarAsync(cancellationToken)) > 0;
    }

    private static async Task<bool> HasUserNotificationSettingsColumnAsync(System.Data.Common.DbConnection connection, string columnName, CancellationToken cancellationToken)
    {
        await using var command = connection.CreateCommand();
        command.CommandText = """
            SELECT COUNT(*)
            FROM information_schema.columns
            WHERE table_schema = DATABASE()
              AND table_name = 'UserNotificationSettings'
              AND column_name = @columnName;
            """;
        var parameter = command.CreateParameter();
        parameter.ParameterName = "@columnName";
        parameter.Value = columnName;
        command.Parameters.Add(parameter);

        return Convert.ToInt32(await command.ExecuteScalarAsync(cancellationToken)) > 0;
    }

    private static async Task<bool> HasAgentColumnAsync(System.Data.Common.DbConnection connection, string columnName, CancellationToken cancellationToken)
    {
        await using var command = connection.CreateCommand();
        command.CommandText = """
            SELECT COUNT(*)
            FROM information_schema.columns
            WHERE table_schema = DATABASE()
              AND table_name = 'Agents'
              AND column_name = @columnName;
            """;
        var parameter = command.CreateParameter();
        parameter.ParameterName = "@columnName";
        parameter.Value = columnName;
        command.Parameters.Add(parameter);

        return Convert.ToInt32(await command.ExecuteScalarAsync(cancellationToken)) > 0;
    }

    private static async Task<bool> HasEndpointDependencyIndexAsync(System.Data.Common.DbConnection connection, string indexName, CancellationToken cancellationToken)
    {
        await using var command = connection.CreateCommand();
        command.CommandText = """
            SELECT COUNT(*)
            FROM information_schema.statistics
            WHERE table_schema = DATABASE()
              AND table_name = 'EndpointDependencies'
              AND index_name = @indexName;
            """;
        var parameter = command.CreateParameter();
        parameter.ParameterName = "@indexName";
        parameter.Value = indexName;
        command.Parameters.Add(parameter);

        return Convert.ToInt32(await command.ExecuteScalarAsync(cancellationToken)) > 0;
    }

    private static async Task<bool> HasPrimaryKeyAsync(System.Data.Common.DbConnection connection, string tableName, CancellationToken cancellationToken)
    {
        await using var command = connection.CreateCommand();
        command.CommandText = """
            SELECT COUNT(*)
            FROM information_schema.table_constraints
            WHERE table_schema = DATABASE()
              AND table_name = @tableName
              AND constraint_type = 'PRIMARY KEY';
            """;
        var parameter = command.CreateParameter();
        parameter.ParameterName = "@tableName";
        parameter.Value = tableName;
        command.Parameters.Add(parameter);

        return Convert.ToInt32(await command.ExecuteScalarAsync(cancellationToken)) > 0;
    }

    private static async Task MigrateLegacyEndpointDependenciesAsync(PingMonitorDbContext dbContext, CancellationToken cancellationToken)
    {
        await using var connection = dbContext.Database.GetDbConnection();
        if (connection.State != System.Data.ConnectionState.Open)
        {
            await connection.OpenAsync(cancellationToken);
        }

        await using var hasColumnCommand = connection.CreateCommand();
        hasColumnCommand.CommandText = """
            SELECT COUNT(*)
            FROM information_schema.columns
            WHERE table_schema = DATABASE()
              AND table_name = 'Endpoints'
              AND column_name = 'DependsOnEndpointId';
            """;

        var hasLegacyColumn = Convert.ToInt32(await hasColumnCommand.ExecuteScalarAsync(cancellationToken)) > 0;
        if (!hasLegacyColumn)
        {
            return;
        }

        await dbContext.Database.ExecuteSqlRawAsync(
            """
            INSERT IGNORE INTO `EndpointDependencies` (`EndpointDependencyId`, `EndpointId`, `DependsOnEndpointId`, `CreatedAtUtc`)
            SELECT UUID(), `EndpointId`, `DependsOnEndpointId`, UTC_TIMESTAMP(6)
            FROM `Endpoints`
            WHERE `DependsOnEndpointId` IS NOT NULL AND `DependsOnEndpointId` <> '';
            """,
            cancellationToken);
    }
}

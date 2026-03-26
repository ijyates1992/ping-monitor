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
        "Endpoints",
        "EndpointDependencies",
        "Groups",
        "EndpointGroupMemberships",
        "MonitorAssignments",
        "CheckResults",
        "ResultBatches",
        "EndpointStates",
        "StateTransitions",
        "ApplicationSettings"
    ];
    private static readonly string[] RequiredEndpointDependencyColumns =
    [
        "EndpointDependencyId",
        "EndpointId",
        "DependsOnEndpointId",
        "CreatedAtUtc"
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

        await dbContext.Database.ExecuteSqlRawAsync(createEndpointStatesSql, cancellationToken);
        await dbContext.Database.ExecuteSqlRawAsync(createStateTransitionsSql, cancellationToken);
        await dbContext.Database.ExecuteSqlRawAsync(createApplicationSettingsSql, cancellationToken);
        await dbContext.Database.ExecuteSqlRawAsync(createEndpointDependenciesSql, cancellationToken);
        await dbContext.Database.ExecuteSqlRawAsync(createGroupsSql, cancellationToken);
        await dbContext.Database.ExecuteSqlRawAsync(createEndpointGroupMembershipsSql, cancellationToken);
        await EnsureEndpointDependenciesColumnsAsync(dbContext, cancellationToken);
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

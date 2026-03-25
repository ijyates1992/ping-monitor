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
        "MonitorAssignments",
        "CheckResults",
        "ResultBatches",
        "EndpointStates",
        "StateTransitions",
        "ApplicationSettings"
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

        await dbContext.Database.ExecuteSqlRawAsync(createEndpointStatesSql, cancellationToken);
        await dbContext.Database.ExecuteSqlRawAsync(createStateTransitionsSql, cancellationToken);
        await dbContext.Database.ExecuteSqlRawAsync(createApplicationSettingsSql, cancellationToken);
        await dbContext.Database.ExecuteSqlRawAsync(createEndpointDependenciesSql, cancellationToken);
        await MigrateLegacyEndpointDependenciesAsync(dbContext, cancellationToken);
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

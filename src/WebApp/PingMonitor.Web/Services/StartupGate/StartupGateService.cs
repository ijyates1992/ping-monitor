using System.Reflection;
using MySqlConnector;

namespace PingMonitor.Web.Services.StartupGate;

internal sealed class StartupGateService : IStartupGateService
{
    private readonly IStartupDatabaseConfigurationStore _configurationStore;
    private readonly IStartupSchemaService _schemaService;
    private readonly IStartupAdminBootstrapService _adminBootstrapService;
    private readonly ILocalRequestEvaluator _localRequestEvaluator;
    private readonly IWebHostEnvironment _environment;
    private readonly ILogger<StartupGateService> _logger;
    private readonly int _requiredSchemaVersion;

    public StartupGateService(
        IStartupDatabaseConfigurationStore configurationStore,
        IStartupSchemaService schemaService,
        IStartupAdminBootstrapService adminBootstrapService,
        ILocalRequestEvaluator localRequestEvaluator,
        IWebHostEnvironment environment,
        Microsoft.Extensions.Options.IOptions<PingMonitor.Web.Options.StartupGateOptions> options,
        ILogger<StartupGateService> logger)
    {
        _configurationStore = configurationStore;
        _schemaService = schemaService;
        _adminBootstrapService = adminBootstrapService;
        _localRequestEvaluator = localRequestEvaluator;
        _environment = environment;
        _requiredSchemaVersion = options.Value.RequiredSchemaVersion;
        _logger = logger;
    }

    public async Task<StartupGateStatus> EvaluateAsync(HttpContext httpContext, CancellationToken cancellationToken)
    {
        var isLocalRequest = _localRequestEvaluator.IsLocal(httpContext);
        var applicationVersion = Assembly.GetEntryAssembly()?.GetName().Version?.ToString() ?? "unknown";
        var configuration = await _configurationStore.LoadAsync(cancellationToken);
        var status = new StartupGateStatus
        {
            EnvironmentName = _environment.EnvironmentName,
            ApplicationVersion = applicationVersion,
            DatabaseConfiguration = configuration,
            IsLocalRequest = isLocalRequest,
            CanPerformWriteActions = isLocalRequest,
            RequiredSchemaVersion = _requiredSchemaVersion
        };

        if (configuration is null || !configuration.IsComplete)
        {
            status.Diagnostics.Add("MySQL configuration is missing or incomplete.");
            status.Mode = StartupMode.Gate;
            status.FailingStage = StartupGateStage.DatabaseConfiguration;
            return status;
        }

        status.IsDatabaseConfigurationPresent = true;

        string? password;
        try
        {
            password = await _configurationStore.LoadPasswordAsync(cancellationToken);
        }
        catch (Exception exception)
        {
            _logger.LogWarning(exception, "Startup gate could not load the protected database password.");
            status.Diagnostics.Add("Database password could not be read from local storage.");
            status.Mode = StartupMode.Gate;
            status.FailingStage = StartupGateStage.DatabaseConfiguration;
            return status;
        }

        if (string.IsNullOrWhiteSpace(password))
        {
            status.Diagnostics.Add("Database password is missing from local storage.");
            status.Mode = StartupMode.Gate;
            status.FailingStage = StartupGateStage.DatabaseConfiguration;
            return status;
        }

        try
        {
            var connectionString = _configurationStore.BuildConnectionString(configuration, password);
            await using var connection = new MySqlConnection(connectionString);
            await connection.OpenAsync(cancellationToken);
            status.IsDatabaseConnectionSuccessful = true;
            status.Diagnostics.Add("MySQL connection succeeded.");
        }
        catch (Exception exception)
        {
            _logger.LogWarning(exception, "Startup gate database connection test failed for {Host}:{Port}/{DatabaseName}.", configuration.Host, configuration.Port, configuration.DatabaseName);
            status.Diagnostics.Add($"MySQL connection failed: {exception.Message}");
            status.Mode = StartupMode.Gate;
            status.FailingStage = StartupGateStage.DatabaseConnection;
            return status;
        }

        StartupSchemaStatus schemaStatus;
        try
        {
            schemaStatus = await _schemaService.GetStatusAsync(cancellationToken);
        }
        catch (Exception exception)
        {
            _logger.LogWarning(exception, "Startup gate schema inspection failed.");
            status.Diagnostics.Add($"Schema inspection failed: {exception.Message}");
            status.Mode = StartupMode.Gate;
            status.FailingStage = StartupGateStage.Schema;
            return status;
        }

        status.SchemaState = schemaStatus.State;
        status.CurrentSchemaVersion = schemaStatus.CurrentSchemaVersion;
        status.Diagnostics.AddRange(schemaStatus.Diagnostics);
        if (schemaStatus.State != StartupGateSchemaState.Compatible)
        {
            status.Mode = StartupMode.Gate;
            status.FailingStage = StartupGateStage.Schema;
            return status;
        }

        StartupAdminStatus adminStatus;
        try
        {
            adminStatus = await _adminBootstrapService.GetStatusAsync(cancellationToken);
        }
        catch (Exception exception)
        {
            _logger.LogWarning(exception, "Startup gate admin bootstrap inspection failed.");
            status.Diagnostics.Add($"Admin bootstrap inspection failed: {exception.Message}");
            status.Mode = StartupMode.Gate;
            status.FailingStage = StartupGateStage.AdminBootstrap;
            return status;
        }

        status.AdminUserExists = adminStatus.AdminExists;
        status.Diagnostics.AddRange(adminStatus.Diagnostics);
        if (!adminStatus.AdminExists)
        {
            status.Mode = StartupMode.Gate;
            status.FailingStage = StartupGateStage.AdminBootstrap;
            return status;
        }

        status.Mode = StartupMode.Normal;
        status.FailingStage = StartupGateStage.None;
        return status;
    }
}

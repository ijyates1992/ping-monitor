namespace PingMonitor.Web.Services.StartupGate;

public enum StartupMode
{
    Normal,
    Gate
}

public enum StartupGateStage
{
    None,
    DatabaseConfiguration,
    DatabaseConnection,
    Schema,
    AdminBootstrap
}

public enum StartupGateSchemaState
{
    Unknown,
    Missing,
    Compatible,
    Incompatible
}

public sealed class StartupDatabaseConfiguration
{
    public string Host { get; init; } = string.Empty;
    public int Port { get; init; }
    public string DatabaseName { get; init; } = string.Empty;
    public string Username { get; init; } = string.Empty;
    public bool HasPassword { get; init; }

    public bool IsComplete =>
        !string.IsNullOrWhiteSpace(Host) &&
        Port > 0 &&
        !string.IsNullOrWhiteSpace(DatabaseName) &&
        !string.IsNullOrWhiteSpace(Username) &&
        HasPassword;
}

public sealed class StartupDatabaseConfigurationInput
{
    public string Host { get; set; } = string.Empty;
    public int Port { get; set; }
    public string DatabaseName { get; set; } = string.Empty;
    public string Username { get; set; } = string.Empty;
    public string Password { get; set; } = string.Empty;
}

public sealed class StartupGateStatus
{
    public StartupMode Mode { get; set; }
    public StartupGateStage FailingStage { get; set; }
    public List<string> Diagnostics { get; } = [];
    public bool IsDatabaseConfigurationPresent { get; set; }
    public bool IsDatabaseConnectionSuccessful { get; set; }
    public StartupGateSchemaState SchemaState { get; set; }
    public bool AdminUserExists { get; set; }
    public bool IsLocalRequest { get; set; }
    public bool CanPerformWriteActions { get; set; }
    public string EnvironmentName { get; set; } = string.Empty;
    public string ApplicationVersion { get; set; } = string.Empty;
    public StartupDatabaseConfiguration? DatabaseConfiguration { get; set; }
    public int RequiredSchemaVersion { get; set; }
    public int? CurrentSchemaVersion { get; set; }
}

public sealed class StartupSchemaStatus
{
    public StartupGateSchemaState State { get; init; }
    public int? CurrentSchemaVersion { get; set; }
    public List<string> Diagnostics { get; } = [];
}

public sealed class StartupAdminStatus
{
    public bool AdminExists { get; init; }
    public List<string> Diagnostics { get; } = [];
}

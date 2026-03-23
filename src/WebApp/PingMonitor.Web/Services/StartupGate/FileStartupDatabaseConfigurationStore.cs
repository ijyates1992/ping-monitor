using System.Security.Cryptography;
using System.Text.Json;
using Microsoft.Extensions.Options;
using PingMonitor.Web.Options;

namespace PingMonitor.Web.Services.StartupGate;

internal sealed class FileStartupDatabaseConfigurationStore : IStartupDatabaseConfigurationStore
{
    private const string SettingsFileName = "dbsettings.json";
    private const string PasswordFileName = "dbpassword.bin";

    private readonly ILogger<FileStartupDatabaseConfigurationStore> _logger;
    private readonly StartupGateOptions _options;
    private readonly string _storageDirectory;

    public FileStartupDatabaseConfigurationStore(
        IOptions<StartupGateOptions> options,
        IWebHostEnvironment environment,
        ILogger<FileStartupDatabaseConfigurationStore> logger)
    {
        _logger = logger;
        _options = options.Value;
        _storageDirectory = Path.IsPathRooted(_options.StorageDirectory)
            ? _options.StorageDirectory
            : Path.Combine(environment.ContentRootPath, _options.StorageDirectory);
    }

    public async Task<StartupDatabaseConfiguration?> LoadAsync(CancellationToken cancellationToken)
    {
        var settingsPath = GetSettingsPath();
        if (!File.Exists(settingsPath))
        {
            return null;
        }

        await using var stream = File.OpenRead(settingsPath);
        var document = await JsonSerializer.DeserializeAsync<StoredDatabaseConfiguration>(stream, cancellationToken: cancellationToken);
        if (document is null)
        {
            return null;
        }

        return new StartupDatabaseConfiguration
        {
            Host = document.Host ?? string.Empty,
            Port = document.Port,
            DatabaseName = document.DatabaseName ?? string.Empty,
            Username = document.Username ?? string.Empty,
            HasPassword = File.Exists(GetPasswordPath())
        };
    }

    public async Task<string?> LoadPasswordAsync(CancellationToken cancellationToken)
    {
        var passwordPath = GetPasswordPath();
        if (!File.Exists(passwordPath))
        {
            return null;
        }

        var payload = await File.ReadAllBytesAsync(passwordPath, cancellationToken);
        if (payload.Length == 0)
        {
            return null;
        }

        if (OperatingSystem.IsWindows())
        {
            var unprotectedBytes = ProtectedData.Unprotect(payload, optionalEntropy: null, DataProtectionScope.LocalMachine);
            return System.Text.Encoding.UTF8.GetString(unprotectedBytes);
        }

        _logger.LogWarning("Startup gate database password fallback storage is in use because DPAPI is only available on Windows. This should only be used outside the intended Windows IIS runtime.");
        return System.Text.Encoding.UTF8.GetString(payload);
    }

    public async Task SaveAsync(StartupDatabaseConfigurationInput input, CancellationToken cancellationToken)
    {
        Directory.CreateDirectory(_storageDirectory);

        var settings = new StoredDatabaseConfiguration
        {
            Host = input.Host.Trim(),
            Port = input.Port,
            DatabaseName = input.DatabaseName.Trim(),
            Username = input.Username.Trim()
        };

        var settingsPath = GetSettingsPath();
        await using (var stream = File.Create(settingsPath))
        {
            await JsonSerializer.SerializeAsync(stream, settings, cancellationToken: cancellationToken);
        }

        var passwordBytes = System.Text.Encoding.UTF8.GetBytes(input.Password);
        if (OperatingSystem.IsWindows())
        {
            passwordBytes = ProtectedData.Protect(passwordBytes, optionalEntropy: null, DataProtectionScope.LocalMachine);
        }

        await File.WriteAllBytesAsync(GetPasswordPath(), passwordBytes, cancellationToken);
    }

    public string BuildConnectionString(StartupDatabaseConfiguration configuration, string password)
    {
        return $"Server={configuration.Host};Port={configuration.Port};Database={configuration.DatabaseName};User ID={configuration.Username};Password={password};SslMode=Preferred;AllowPublicKeyRetrieval=True";
    }

    private string GetSettingsPath() => Path.Combine(_storageDirectory, SettingsFileName);

    private string GetPasswordPath() => Path.Combine(_storageDirectory, PasswordFileName);

    private sealed class StoredDatabaseConfiguration
    {
        public string? Host { get; init; }
        public int Port { get; init; }
        public string? DatabaseName { get; init; }
        public string? Username { get; init; }
    }
}

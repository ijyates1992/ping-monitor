using System.Diagnostics;
using System.Reflection;
using System.Runtime.InteropServices;
using Microsoft.Extensions.Options;
using PingMonitor.Web.Options;
using PingMonitor.Web.Services.ApplicationMetadata;
using PingMonitor.Web.Services.DatabaseStatus;
using PingMonitor.Web.Services.StartupGate;

namespace PingMonitor.Web.Services.AiTools;

public interface IAiRuntimeInfoService
{
    Task<AiRuntimeInfoDto> GetRuntimeInfoAsync(AiRuntimeInfoRequest request, CancellationToken cancellationToken);
}

public sealed class AiRuntimeInfoRequest
{
    public bool IsAdmin { get; init; }
    public bool IncludeDatabase { get; init; } = true;
    public bool IncludeEnvironment { get; init; } = true;
    public bool IncludeBuild { get; init; } = true;
}

public sealed class AiRuntimeInfoDto
{
    public DateTimeOffset GeneratedAtUtc { get; init; }
    public string DataSource { get; init; } = "application_runtime_info";
    public bool PermissionFiltered { get; init; }
    public string DetailLevel { get; init; } = "Minimal";
    public AiApplicationRuntimeInfo Application { get; init; } = new();
    public AiRuntimeEnvironmentInfo? Runtime { get; init; }
    public AiStartupGateInfo? StartupGate { get; init; }
    public AiDatabaseRuntimeInfo? Database { get; init; }
    public IReadOnlyList<string> Limitations { get; init; } = Array.Empty<string>();
}

public sealed class AiApplicationRuntimeInfo
{
    public string Name { get; init; } = "Ping Monitor";
    public string? Version { get; init; }
    public string? AssemblyVersion { get; init; }
    public string? InformationalVersion { get; init; }
    public string? FileVersion { get; init; }
    public string? CommitSha { get; init; }
    public string? Branch { get; init; }
    public string? BuildTimestampUtc { get; init; }
}

public sealed class AiRuntimeEnvironmentInfo
{
    public string? Environment { get; init; }
    public string DotNetVersion { get; init; } = string.Empty;
    public string OSDescription { get; init; } = string.Empty;
    public string ProcessArchitecture { get; init; } = string.Empty;
    public DateTimeOffset ProcessStartedAtUtc { get; init; }
    public long UptimeSeconds { get; init; }
    public DateTimeOffset CurrentServerTimeUtc { get; init; }
    public DateTimeOffset CurrentServerLocalTime { get; init; }
    public bool? IsIisInProcess { get; init; }
}

public sealed class AiStartupGateInfo
{
    public string Mode { get; init; } = string.Empty;
    public int RequiredSchemaVersion { get; init; }
    public int? CurrentSchemaVersion { get; init; }
    public bool? SchemaCompatible { get; init; }
    public bool ReadOnlyMode { get; init; }
}

public sealed class AiDatabaseRuntimeInfo
{
    public bool Available { get; init; } = true;
    public string? Reason { get; init; }
    public string? Provider { get; init; }
    public string? DatabaseName { get; init; }
    public long? SizeBytes { get; init; }
    public long? DataSizeBytes { get; init; }
    public long? IndexSizeBytes { get; init; }
    public int? TableCount { get; init; }
    public IReadOnlyList<AiDatabaseTableRuntimeInfo> LargestTables { get; init; } = Array.Empty<AiDatabaseTableRuntimeInfo>();
}

public sealed class AiDatabaseTableRuntimeInfo
{
    public string TableName { get; init; } = string.Empty;
    public long SizeBytes { get; init; }
    public long RowCountEstimate { get; init; }
}

internal sealed class AiRuntimeInfoService : IAiRuntimeInfoService
{
    private readonly IApplicationMetadataProvider _metadataProvider;
    private readonly IWebHostEnvironment _environment;
    private readonly IStartupGateRuntimeState _startupGateRuntimeState;
    private readonly IStartupSchemaService _startupSchemaService;
    private readonly IDatabaseStatusQueryService _databaseStatusQueryService;
    private readonly IOptions<StartupGateOptions> _startupGateOptions;
    private readonly ILogger<AiRuntimeInfoService> _logger;

    public AiRuntimeInfoService(
        IApplicationMetadataProvider metadataProvider,
        IWebHostEnvironment environment,
        IStartupGateRuntimeState startupGateRuntimeState,
        IStartupSchemaService startupSchemaService,
        IDatabaseStatusQueryService databaseStatusQueryService,
        IOptions<StartupGateOptions> startupGateOptions,
        ILogger<AiRuntimeInfoService> logger)
    {
        _metadataProvider = metadataProvider;
        _environment = environment;
        _startupGateRuntimeState = startupGateRuntimeState;
        _startupSchemaService = startupSchemaService;
        _databaseStatusQueryService = databaseStatusQueryService;
        _startupGateOptions = startupGateOptions;
        _logger = logger;
    }

    public async Task<AiRuntimeInfoDto> GetRuntimeInfoAsync(AiRuntimeInfoRequest request, CancellationToken cancellationToken)
    {
        var now = DateTimeOffset.UtcNow;
        var application = BuildApplicationInfo(request.IncludeBuild);
        if (!request.IsAdmin)
        {
            return new AiRuntimeInfoDto
            {
                GeneratedAtUtc = now,
                PermissionFiltered = true,
                DetailLevel = "Minimal",
                Application = new AiApplicationRuntimeInfo { Name = application.Name, Version = application.Version },
                Limitations = new[] { "Detailed runtime, environment, database, startup gate, schema, and build metadata is admin-only.", "Secrets, connection strings, credentials, API keys, protected settings, filesystem paths, and host details are not exposed." }
            };
        }

        var startupGate = await BuildStartupGateInfoAsync(cancellationToken);
        return new AiRuntimeInfoDto
        {
            GeneratedAtUtc = now,
            PermissionFiltered = false,
            DetailLevel = "Admin",
            Application = application,
            Runtime = request.IncludeEnvironment ? BuildRuntimeInfo(now) : null,
            StartupGate = startupGate,
            Database = request.IncludeDatabase ? await BuildDatabaseInfoAsync(cancellationToken) : null,
            Limitations = new[] { "Database sizes and row counts are approximate where reported by the database provider.", "Secrets, connection strings, credentials, API keys, protected settings, filesystem paths, hostnames, ports, usernames, and runtime command access are not exposed." }
        };
    }

    private AiApplicationRuntimeInfo BuildApplicationInfo(bool includeBuild)
    {
        var metadata = _metadataProvider.GetSnapshot();
        var assembly = Assembly.GetEntryAssembly() ?? typeof(AiRuntimeInfoService).Assembly;
        if (!includeBuild) return new AiApplicationRuntimeInfo { Name = Safe(metadata.ApplicationName, "Ping Monitor"), Version = metadata.Version };
        var info = assembly.GetCustomAttribute<AssemblyInformationalVersionAttribute>()?.InformationalVersion;
        return new AiApplicationRuntimeInfo
        {
            Name = Safe(metadata.ApplicationName, "Ping Monitor"),
            Version = metadata.Version,
            AssemblyVersion = assembly.GetName().Version?.ToString(),
            InformationalVersion = info,
            FileVersion = assembly.GetCustomAttribute<AssemblyFileVersionAttribute>()?.Version,
            CommitSha = TryParseSourceRevision(info),
            Branch = null,
            BuildTimestampUtc = null
        };
    }

    private AiRuntimeEnvironmentInfo BuildRuntimeInfo(DateTimeOffset now)
    {
        var process = Process.GetCurrentProcess();
        var started = new DateTimeOffset(process.StartTime.ToUniversalTime(), TimeSpan.Zero);
        return new AiRuntimeEnvironmentInfo
        {
            Environment = _environment.EnvironmentName,
            DotNetVersion = RuntimeInformation.FrameworkDescription,
            OSDescription = RuntimeInformation.OSDescription,
            ProcessArchitecture = RuntimeInformation.ProcessArchitecture.ToString(),
            ProcessStartedAtUtc = started,
            UptimeSeconds = Math.Max(0, (long)(now - started).TotalSeconds),
            CurrentServerTimeUtc = now,
            CurrentServerLocalTime = DateTimeOffset.Now,
            IsIisInProcess = string.Equals(Environment.GetEnvironmentVariable("ASPNETCORE_HOSTINGSTARTUPASSEMBLIES"), "Microsoft.AspNetCore.Server.IIS", StringComparison.OrdinalIgnoreCase) ? true : null
        };
    }

    private async Task<AiStartupGateInfo> BuildStartupGateInfoAsync(CancellationToken cancellationToken)
    {
        int? current = null;
        bool? compatible = null;
        if (_startupGateRuntimeState.IsOperationalMode)
        {
            try
            {
                var schema = await _startupSchemaService.GetStatusAsync(cancellationToken);
                current = schema.CurrentSchemaVersion;
                compatible = schema.State == StartupGateSchemaState.Compatible;
            }
            catch (Exception ex) when (ex is not OperationCanceledException)
            {
                _logger.LogWarning(ex, "AI runtime info could not read startup schema status.");
            }
        }

        return new AiStartupGateInfo
        {
            Mode = _startupGateRuntimeState.CurrentMode.ToString(),
            RequiredSchemaVersion = _startupGateOptions.Value.RequiredSchemaVersion,
            CurrentSchemaVersion = current,
            SchemaCompatible = compatible,
            ReadOnlyMode = !_startupGateRuntimeState.IsOperationalMode
        };
    }

    private async Task<AiDatabaseRuntimeInfo> BuildDatabaseInfoAsync(CancellationToken cancellationToken)
    {
        if (!_startupGateRuntimeState.IsOperationalMode)
        {
            return new AiDatabaseRuntimeInfo { Available = false, Reason = "Database runtime information is not available while Startup Gate is active." };
        }

        try
        {
            var snapshot = await _databaseStatusQueryService.GetSnapshotAsync(cancellationToken);
            var largestTables = snapshot.Tables
                .OrderByDescending(x => x.DataBytes + x.IndexBytes)
                .ThenBy(x => x.TableName, StringComparer.Ordinal)
                .Take(10)
                .Select(x => new AiDatabaseTableRuntimeInfo { TableName = x.TableName, SizeBytes = x.DataBytes + x.IndexBytes, RowCountEstimate = x.ApproximateRowCount })
                .ToArray();

            return new AiDatabaseRuntimeInfo
            {
                Provider = snapshot.ProviderName,
                DatabaseName = snapshot.DatabaseName,
                SizeBytes = snapshot.TotalDataBytes + snapshot.TotalIndexBytes,
                DataSizeBytes = snapshot.TotalDataBytes,
                IndexSizeBytes = snapshot.TotalIndexBytes,
                TableCount = snapshot.TableCount,
                LargestTables = largestTables
            };
        }
        catch (Exception ex) when (ex is not OperationCanceledException)
        {
            _logger.LogWarning(ex, "AI runtime info could not read database size metadata.");
            return new AiDatabaseRuntimeInfo { Available = false, Reason = "Database size information is not available from the current provider." };
        }
    }

    private static string Safe(string? value, string fallback) => string.IsNullOrWhiteSpace(value) ? fallback : value.Trim();
    private static string? TryParseSourceRevision(string? informationalVersion)
    {
        if (string.IsNullOrWhiteSpace(informationalVersion)) return null;
        var plus = informationalVersion.LastIndexOf('+');
        if (plus < 0 || plus == informationalVersion.Length - 1) return null;
        var candidate = informationalVersion[(plus + 1)..].Trim();
        return candidate.Length is >= 7 and <= 64 && candidate.All(Uri.IsHexDigit) ? candidate : null;
    }
}

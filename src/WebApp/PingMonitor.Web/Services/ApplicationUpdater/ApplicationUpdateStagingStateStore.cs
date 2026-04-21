using System.Text;
using System.Text.Json;
using Microsoft.Extensions.Options;
using PingMonitor.Web.Options;
using PingMonitor.Web.Support;

namespace PingMonitor.Web.Services.ApplicationUpdater;

public interface IApplicationUpdateStagingStateStore
{
    Task<ApplicationUpdateStagingState?> ReadAsync(CancellationToken cancellationToken);
    Task WriteAsync(ApplicationUpdateStagingState state, CancellationToken cancellationToken);
    string GetStagingRootPath();
}

internal sealed class ApplicationUpdateStagingStateStore : IApplicationUpdateStagingStateStore
{
    private static readonly JsonSerializerOptions SerializerOptions = new(JsonSerializerDefaults.Web)
    {
        WriteIndented = true,
        Converters = { new UtcDateTimeOffsetJsonConverter() }
    };

    private readonly IWebHostEnvironment _environment;
    private readonly ApplicationUpdaterOptions _options;

    public ApplicationUpdateStagingStateStore(
        IWebHostEnvironment environment,
        IOptions<ApplicationUpdaterOptions> options)
    {
        _environment = environment;
        _options = options.Value;
    }

    public async Task<ApplicationUpdateStagingState?> ReadAsync(CancellationToken cancellationToken)
    {
        var statePath = GetStatePath();
        if (!File.Exists(statePath))
        {
            return null;
        }

        await using var stream = File.OpenRead(statePath);
        return await JsonSerializer.DeserializeAsync<ApplicationUpdateStagingState>(stream, SerializerOptions, cancellationToken);
    }

    public async Task WriteAsync(ApplicationUpdateStagingState state, CancellationToken cancellationToken)
    {
        var statePath = GetStatePath();
        Directory.CreateDirectory(Path.GetDirectoryName(statePath)!);

        var tempPath = statePath + ".tmp";
        await using (var stream = new FileStream(tempPath, FileMode.Create, FileAccess.Write, FileShare.None))
        {
            await JsonSerializer.SerializeAsync(stream, state, SerializerOptions, cancellationToken);
        }

        if (File.Exists(statePath))
        {
            File.Delete(statePath);
        }

        File.Move(tempPath, statePath);
    }

    public string GetStagingRootPath()
    {
        var configuredPath = _options.StagingStoragePath.Trim();
        return Path.IsPathRooted(configuredPath)
            ? configuredPath
            : Path.GetFullPath(configuredPath, _environment.ContentRootPath);
    }

    private string GetStatePath()
    {
        return Path.Combine(GetStagingRootPath(), "state", "staged-update.json");
    }
}

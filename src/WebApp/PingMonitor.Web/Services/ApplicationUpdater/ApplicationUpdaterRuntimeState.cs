using PingMonitor.Web.Support;
using System.Text.Json;
using Microsoft.Extensions.Options;
using PingMonitor.Web.Options;

namespace PingMonitor.Web.Services.ApplicationUpdater;

public sealed class ApplicationUpdaterRuntimeState
{
    public DateTimeOffset? LastAutomaticCheckAtUtc { get; init; }
    public DateTimeOffset? LastAutomaticCheckSucceededAtUtc { get; init; }
    public DateTimeOffset? LastAutomaticCheckFailedAtUtc { get; init; }
    public string? LastAutomaticCheckFailureMessage { get; init; }
    public string? LastDetectedApplicableReleaseTag { get; init; }
    public DateTimeOffset? LastDetectedApplicableReleaseAtUtc { get; init; }
    public string? LastAutoStageAttemptedReleaseTag { get; init; }
    public DateTimeOffset? LastAutoStageAttemptedAtUtc { get; init; }
    public string? LastAutoStageFailureMessage { get; init; }
    public string? LastAutoStagedReleaseTag { get; init; }
    public DateTimeOffset? LastAutoStagedAtUtc { get; init; }
    public string? LastDevComparisonSkippedReleaseTag { get; init; }
    public DateTimeOffset? LastDevComparisonSkippedAtUtc { get; init; }
    public string? LastDevAutoStageSuppressedReleaseTag { get; init; }
    public DateTimeOffset? LastDevAutoStageSuppressedAtUtc { get; init; }
    public string? LastDevAutoStageOverrideAllowedReleaseTag { get; init; }
    public DateTimeOffset? LastDevAutoStageOverrideAllowedAtUtc { get; init; }
    public DateTimeOffset LastUpdatedAtUtc { get; init; }
}

public interface IApplicationUpdaterRuntimeStateStore
{
    Task<ApplicationUpdaterRuntimeState?> ReadAsync(CancellationToken cancellationToken);
    Task WriteAsync(ApplicationUpdaterRuntimeState state, CancellationToken cancellationToken);
}

internal sealed class ApplicationUpdaterRuntimeStateStore : IApplicationUpdaterRuntimeStateStore
{
    private static readonly JsonSerializerOptions SerializerOptions = new(JsonSerializerDefaults.Web)
    {
        WriteIndented = true,
        Converters = { new UtcDateTimeOffsetJsonConverter() }
    };

    private readonly IApplicationUpdateStagingStateStore _stagingStateStore;

    public ApplicationUpdaterRuntimeStateStore(IApplicationUpdateStagingStateStore stagingStateStore)
    {
        _stagingStateStore = stagingStateStore;
    }

    public async Task<ApplicationUpdaterRuntimeState?> ReadAsync(CancellationToken cancellationToken)
    {
        var statePath = GetStatePath();
        if (!File.Exists(statePath))
        {
            return null;
        }

        await using var stream = File.OpenRead(statePath);
        return await JsonSerializer.DeserializeAsync<ApplicationUpdaterRuntimeState>(stream, SerializerOptions, cancellationToken);
    }

    public async Task WriteAsync(ApplicationUpdaterRuntimeState state, CancellationToken cancellationToken)
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

    private string GetStatePath()
    {
        return Path.Combine(_stagingStateStore.GetStagingRootPath(), "state", "automatic-update-state.json");
    }
}

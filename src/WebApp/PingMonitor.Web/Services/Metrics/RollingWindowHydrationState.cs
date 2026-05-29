namespace PingMonitor.Web.Services.Metrics;

public enum RollingWindowHydrationStatus
{
    NotStarted,
    Running,
    Complete,
    Failed
}

public sealed record RollingWindowHydrationSnapshot(
    RollingWindowHydrationStatus Status,
    DateTimeOffset? StartedAtUtc,
    DateTimeOffset? CompletedAtUtc,
    string? FailureMessage);

public interface IRollingWindowHydrationState
{
    RollingWindowHydrationSnapshot GetSnapshot();
    void MarkRunning(DateTimeOffset startedAtUtc);
    void MarkComplete(DateTimeOffset completedAtUtc);
    void MarkFailed(DateTimeOffset failedAtUtc, string failureMessage);
}

internal sealed class RollingWindowHydrationState : IRollingWindowHydrationState
{
    private readonly object _sync = new();
    private RollingWindowHydrationStatus _status = RollingWindowHydrationStatus.NotStarted;
    private DateTimeOffset? _startedAtUtc;
    private DateTimeOffset? _completedAtUtc;
    private string? _failureMessage;

    public RollingWindowHydrationSnapshot GetSnapshot()
    {
        lock (_sync)
        {
            return new RollingWindowHydrationSnapshot(_status, _startedAtUtc, _completedAtUtc, _failureMessage);
        }
    }

    public void MarkRunning(DateTimeOffset startedAtUtc)
    {
        lock (_sync)
        {
            _status = RollingWindowHydrationStatus.Running;
            _startedAtUtc = startedAtUtc;
            _completedAtUtc = null;
            _failureMessage = null;
        }
    }

    public void MarkComplete(DateTimeOffset completedAtUtc)
    {
        lock (_sync)
        {
            _status = RollingWindowHydrationStatus.Complete;
            _completedAtUtc = completedAtUtc;
            _failureMessage = null;
        }
    }

    public void MarkFailed(DateTimeOffset failedAtUtc, string failureMessage)
    {
        lock (_sync)
        {
            _status = RollingWindowHydrationStatus.Failed;
            _completedAtUtc = failedAtUtc;
            _failureMessage = string.IsNullOrWhiteSpace(failureMessage) ? "Rolling window hydration failed." : failureMessage;
        }
    }
}

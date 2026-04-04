namespace PingMonitor.Web.Services.Diagnostics;

public interface IDbActivityTracker
{
    void Record(DbActivityRecord record);
    IReadOnlyList<DbSubsystemActivitySnapshot> GetSnapshot(DateTimeOffset nowUtc);
}

public sealed class DbActivityRecord
{
    public string Subsystem { get; init; } = "Unknown";
    public DbActivityKind ActivityKind { get; init; }
    public TimeSpan Duration { get; init; }
    public bool Succeeded { get; init; }
    public DateTimeOffset OccurredAtUtc { get; init; }
    public int? Rows { get; init; }
    public string? CommandType { get; init; }
}

public enum DbActivityKind
{
    Read = 1,
    Write = 2,
    Other = 3
}

public sealed class DbSubsystemActivitySnapshot
{
    public string Subsystem { get; init; } = string.Empty;
    public DbActivityAggregateSnapshot Lifetime { get; init; } = new();
    public DbActivityAggregateSnapshot Recent { get; init; } = new();
    public DateTimeOffset? LastActivityAtUtc { get; init; }
    public DateTimeOffset? LastErrorAtUtc { get; init; }
    public string? LastCommandType { get; init; }
}

public sealed class DbActivityAggregateSnapshot
{
    public long ReadCount { get; init; }
    public long WriteCount { get; init; }
    public long ReadErrorCount { get; init; }
    public long WriteErrorCount { get; init; }
    public long ReadDurationMs { get; init; }
    public long WriteDurationMs { get; init; }
    public long WriteRows { get; init; }
    public double AverageReadDurationMs => ReadCount <= 0 ? 0 : ReadDurationMs / (double)ReadCount;
    public double AverageWriteDurationMs => WriteCount <= 0 ? 0 : WriteDurationMs / (double)WriteCount;
    public long TotalDurationMs => ReadDurationMs + WriteDurationMs;
}

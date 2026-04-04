using System.Collections.Concurrent;

namespace PingMonitor.Web.Services.Diagnostics;

internal sealed class DbActivityTracker : IDbActivityTracker
{
    private static readonly TimeSpan RecentWindow = TimeSpan.FromMinutes(5);
    private readonly ConcurrentDictionary<string, SubsystemCounter> _counters = new(StringComparer.Ordinal);

    public void Record(DbActivityRecord record)
    {
        var subsystem = string.IsNullOrWhiteSpace(record.Subsystem)
            ? "Unknown"
            : record.Subsystem.Trim();

        var counter = _counters.GetOrAdd(subsystem, _ => new SubsystemCounter());
        counter.Record(record);
    }

    public IReadOnlyList<DbSubsystemActivitySnapshot> GetSnapshot(DateTimeOffset nowUtc)
    {
        return _counters
            .Select(x => x.Value.BuildSnapshot(x.Key, nowUtc))
            .OrderByDescending(x => x.Recent.TotalDurationMs)
            .ThenBy(x => x.Subsystem, StringComparer.Ordinal)
            .ToArray();
    }

    private sealed class SubsystemCounter
    {
        private readonly object _sync = new();
        private readonly Dictionary<DateTimeOffset, DbActivityAggregateMutable> _minuteBuckets = [];
        private readonly DbActivityAggregateMutable _lifetime = new();
        private DateTimeOffset? _lastActivityAtUtc;
        private DateTimeOffset? _lastErrorAtUtc;
        private string? _lastCommandType;

        public void Record(DbActivityRecord record)
        {
            if (record.ActivityKind == DbActivityKind.Other)
            {
                return;
            }

            lock (_sync)
            {
                _lifetime.Apply(record);

                var minuteBucket = new DateTimeOffset(
                    record.OccurredAtUtc.Year,
                    record.OccurredAtUtc.Month,
                    record.OccurredAtUtc.Day,
                    record.OccurredAtUtc.Hour,
                    record.OccurredAtUtc.Minute,
                    0,
                    TimeSpan.Zero);
                if (!_minuteBuckets.TryGetValue(minuteBucket, out var bucket))
                {
                    bucket = new DbActivityAggregateMutable();
                    _minuteBuckets[minuteBucket] = bucket;
                }

                bucket.Apply(record);
                _lastActivityAtUtc = _lastActivityAtUtc is null || record.OccurredAtUtc > _lastActivityAtUtc
                    ? record.OccurredAtUtc
                    : _lastActivityAtUtc;
                if (!record.Succeeded)
                {
                    _lastErrorAtUtc = _lastErrorAtUtc is null || record.OccurredAtUtc > _lastErrorAtUtc
                        ? record.OccurredAtUtc
                        : _lastErrorAtUtc;
                }

                if (!string.IsNullOrWhiteSpace(record.CommandType))
                {
                    _lastCommandType = record.CommandType;
                }
            }
        }

        public DbSubsystemActivitySnapshot BuildSnapshot(string subsystem, DateTimeOffset nowUtc)
        {
            lock (_sync)
            {
                var cutoff = nowUtc - RecentWindow;
                var cutoffMinute = new DateTimeOffset(
                    cutoff.Year,
                    cutoff.Month,
                    cutoff.Day,
                    cutoff.Hour,
                    cutoff.Minute,
                    0,
                    TimeSpan.Zero);
                var stale = _minuteBuckets.Keys.Where(k => k < cutoffMinute).ToArray();
                foreach (var key in stale)
                {
                    _minuteBuckets.Remove(key);
                }

                var recent = new DbActivityAggregateMutable();
                foreach (var pair in _minuteBuckets)
                {
                    if (pair.Key >= cutoffMinute)
                    {
                        recent.MergeFrom(pair.Value);
                    }
                }

                return new DbSubsystemActivitySnapshot
                {
                    Subsystem = subsystem,
                    Lifetime = _lifetime.ToSnapshot(),
                    Recent = recent.ToSnapshot(),
                    LastActivityAtUtc = _lastActivityAtUtc,
                    LastErrorAtUtc = _lastErrorAtUtc,
                    LastCommandType = _lastCommandType
                };
            }
        }
    }

    private sealed class DbActivityAggregateMutable
    {
        public long ReadCount { get; private set; }
        public long WriteCount { get; private set; }
        public long ReadErrorCount { get; private set; }
        public long WriteErrorCount { get; private set; }
        public long ReadDurationMs { get; private set; }
        public long WriteDurationMs { get; private set; }
        public long WriteRows { get; private set; }

        public void Apply(DbActivityRecord record)
        {
            var durationMs = Math.Max(0, (long)record.Duration.TotalMilliseconds);
            if (record.ActivityKind == DbActivityKind.Read)
            {
                ReadCount += 1;
                ReadDurationMs += durationMs;
                if (!record.Succeeded)
                {
                    ReadErrorCount += 1;
                }

                return;
            }

            if (record.ActivityKind == DbActivityKind.Write)
            {
                WriteCount += 1;
                WriteDurationMs += durationMs;
                if (!record.Succeeded)
                {
                    WriteErrorCount += 1;
                }

                if (record.Rows.HasValue && record.Rows.Value > 0)
                {
                    WriteRows += record.Rows.Value;
                }
            }
        }

        public void MergeFrom(DbActivityAggregateMutable other)
        {
            ReadCount += other.ReadCount;
            WriteCount += other.WriteCount;
            ReadErrorCount += other.ReadErrorCount;
            WriteErrorCount += other.WriteErrorCount;
            ReadDurationMs += other.ReadDurationMs;
            WriteDurationMs += other.WriteDurationMs;
            WriteRows += other.WriteRows;
        }

        public DbActivityAggregateSnapshot ToSnapshot()
        {
            return new DbActivityAggregateSnapshot
            {
                ReadCount = ReadCount,
                WriteCount = WriteCount,
                ReadErrorCount = ReadErrorCount,
                WriteErrorCount = WriteErrorCount,
                ReadDurationMs = ReadDurationMs,
                WriteDurationMs = WriteDurationMs,
                WriteRows = WriteRows
            };
        }
    }
}

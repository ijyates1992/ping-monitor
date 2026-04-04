namespace PingMonitor.Web.Services.Metrics;

public sealed class IngestRateTracker
{
    private const int WindowSizeSeconds = 60;
    private readonly object _sync = new();
    private readonly int[] _ingestBuckets = new int[WindowSizeSeconds];
    private readonly int[] _dropBuckets = new int[WindowSizeSeconds];
    private long _lastSecond;
    private int _ingestTotal;
    private int _dropTotal;
    private bool _initialized;

    public void RecordIngest(int count)
    {
        if (count <= 0)
        {
            return;
        }

        lock (_sync)
        {
            var nowSecond = DateTimeOffset.UtcNow.ToUnixTimeSeconds();
            AdvanceWindow(nowSecond);

            var bucketIndex = (int)(nowSecond % WindowSizeSeconds);
            _ingestBuckets[bucketIndex] += count;
            _ingestTotal += count;
        }
    }

    public void RecordDrop(int count)
    {
        if (count <= 0)
        {
            return;
        }

        lock (_sync)
        {
            var nowSecond = DateTimeOffset.UtcNow.ToUnixTimeSeconds();
            AdvanceWindow(nowSecond);

            var bucketIndex = (int)(nowSecond % WindowSizeSeconds);
            _dropBuckets[bucketIndex] += count;
            _dropTotal += count;
        }
    }

    public int GetIngestPerMinute()
    {
        lock (_sync)
        {
            AdvanceWindow(DateTimeOffset.UtcNow.ToUnixTimeSeconds());
            return _ingestTotal;
        }
    }

    public int GetDropPerMinute()
    {
        lock (_sync)
        {
            AdvanceWindow(DateTimeOffset.UtcNow.ToUnixTimeSeconds());
            return _dropTotal;
        }
    }

    private void AdvanceWindow(long nowSecond)
    {
        if (!_initialized)
        {
            _lastSecond = nowSecond;
            _initialized = true;
            return;
        }

        if (nowSecond <= _lastSecond)
        {
            return;
        }

        var elapsed = nowSecond - _lastSecond;
        if (elapsed >= WindowSizeSeconds)
        {
            Array.Clear(_ingestBuckets);
            Array.Clear(_dropBuckets);
            _ingestTotal = 0;
            _dropTotal = 0;
            _lastSecond = nowSecond;
            return;
        }

        for (var second = _lastSecond + 1; second <= nowSecond; second++)
        {
            var bucketIndex = (int)(second % WindowSizeSeconds);
            _ingestTotal -= _ingestBuckets[bucketIndex];
            _dropTotal -= _dropBuckets[bucketIndex];
            _ingestBuckets[bucketIndex] = 0;
            _dropBuckets[bucketIndex] = 0;
        }

        _lastSecond = nowSecond;
    }
}

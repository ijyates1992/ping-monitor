using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;
using PingMonitor.Web.Options;
using PingMonitor.Web.Services.BufferedResults;
using PingMonitor.Web.Services.StartupGate;
using PingMonitor.Web.Services.State;

using Xunit;

namespace PingMonitor.Web.Tests;

public sealed class BufferedResultFlushBackgroundServiceTests
{
    [Fact]
    public async Task StopAsync_WhenHostShutdownTokenIsCanceled_UsesIndependentFlushToken()
    {
        var buffer = new FakeBufferedResultIngestionService([
            new BufferedCheckResult
            {
                CheckResultId = "result-1",
                AssignmentId = "assignment-1",
                CheckedAtUtc = DateTimeOffset.Parse("2026-06-17T00:00:00Z"),
                ReceivedAtUtc = DateTimeOffset.Parse("2026-06-17T00:00:01Z"),
                BatchId = "batch-1",
                Success = true
            }
        ]);
        var service = new TestBufferedResultFlushBackgroundService(
            buffer,
            new OperationalStartupGateRuntimeState(),
            new ResultBufferOptions
            {
                ResultBufferMaxBatchSize = 500,
                ResultBufferFlushIntervalSeconds = 60,
                ResultBufferShutdownFlushTimeoutSeconds = 30
            });

        await service.StartAsync(CancellationToken.None);
        using var canceledShutdown = new CancellationTokenSource();
        await canceledShutdown.CancelAsync();

        await service.StopAsync(canceledShutdown.Token);

        Assert.Equal(1, service.PersistCallCount);
        Assert.False(service.LastPersistCancellationTokenWasCanceled);
        Assert.Equal(1, buffer.LastFlushPersistedCount);
    }

    [Fact]
    public void CreateShutdownFlushCancellationTokenSource_EnforcesPositiveTimeout()
    {
        using var tokenSource = BufferedResultFlushBackgroundService.CreateShutdownFlushCancellationTokenSource(0);

        Assert.False(tokenSource.IsCancellationRequested);
    }

    private sealed class TestBufferedResultFlushBackgroundService : BufferedResultFlushBackgroundService
    {
        public TestBufferedResultFlushBackgroundService(
            IBufferedResultIngestionService buffer,
            IStartupGateRuntimeState startupGateRuntimeState,
            ResultBufferOptions options)
            : base(
                new ServiceCollection().BuildServiceProvider().GetRequiredService<IServiceScopeFactory>(),
                buffer,
                new FakeAssignmentProcessingQueue(),
                startupGateRuntimeState,
                Microsoft.Extensions.Options.Options.Create(options),
                NullLogger<BufferedResultFlushBackgroundService>.Instance)
        {
        }

        public int PersistCallCount { get; private set; }
        public bool LastPersistCancellationTokenWasCanceled { get; private set; }

        protected override Task<FlushResult> PersistAndEnqueueAssignmentsAsync(IReadOnlyList<BufferedCheckResult> batch, CancellationToken cancellationToken)
        {
            PersistCallCount++;
            LastPersistCancellationTokenWasCanceled = cancellationToken.IsCancellationRequested;
            return Task.FromResult(new FlushResult(batch.Count, 1, 1, DateTimeOffset.Parse("2026-06-17T00:00:02Z")));
        }
    }

    private sealed class FakeBufferedResultIngestionService : IBufferedResultIngestionService
    {
        private readonly Queue<BufferedCheckResult> _items;

        public FakeBufferedResultIngestionService(IEnumerable<BufferedCheckResult> items)
        {
            _items = new Queue<BufferedCheckResult>(items);
        }

        public int LastFlushPersistedCount { get; private set; }

        public void Enqueue(IReadOnlyCollection<BufferedCheckResult> results)
        {
            foreach (var result in results)
            {
                _items.Enqueue(result);
            }
        }

        public bool HasPendingItems() => _items.Count > 0;

        public bool HasPendingFullBatch() => _items.Count >= 500;

        public IReadOnlyList<BufferedCheckResult> DequeueBatch(int maxBatchSize)
        {
            var results = new List<BufferedCheckResult>();
            while (_items.Count > 0 && results.Count < maxBatchSize)
            {
                results.Add(_items.Dequeue());
            }

            return results;
        }

        public BufferedResultBufferSnapshot GetSnapshot() => new(
            _items.Count,
            0,
            0,
            0,
            0,
            0,
            null,
            0,
            0,
            null,
            0,
            0,
            null);

        public Task<bool> WaitForSignalAsync(TimeSpan timeout, CancellationToken cancellationToken) => Task.Delay(timeout, cancellationToken).ContinueWith(_ => false, CancellationToken.None);

        public void RecordFlushOutcome(
            int attemptedCount,
            int persistedCount,
            DateTimeOffset completedAtUtc,
            Exception? error,
            long persistDurationMs,
            int enqueuedAssignmentCount,
            DateTimeOffset? lastAssignmentsEnqueuedAtUtc)
        {
            LastFlushPersistedCount = persistedCount;
        }
    }

    private sealed class FakeAssignmentProcessingQueue : IAssignmentProcessingQueue
    {
        public AssignmentProcessingQueueEnqueueResult EnqueueAssignments(IReadOnlyCollection<string> assignmentIds) => new(assignmentIds.Count, 0);
        public IReadOnlyList<string> DequeueBatch(int maxBatchSize) => [];
        public bool HasPendingItems() => false;
        public Task<bool> WaitForSignalAsync(TimeSpan timeout, CancellationToken cancellationToken) => Task.FromResult(false);
        public void RecordProcessedCount(int processedCount, DateTimeOffset processedAtUtc) { }
        public void RecordFailure(Exception exception, DateTimeOffset failedAtUtc) { }
        public AssignmentProcessingQueueSnapshot GetSnapshot() => new(0, 0, 0, 0, 0, 0, 0, null, null, null, null, null);
    }

    private sealed class OperationalStartupGateRuntimeState : IStartupGateRuntimeState
    {
        public StartupMode CurrentMode => StartupMode.Normal;
        public bool IsOperationalMode => true;
        public void Update(StartupGateStatus status) { }
    }
}

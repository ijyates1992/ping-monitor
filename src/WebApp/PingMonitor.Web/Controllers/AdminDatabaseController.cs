using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using PingMonitor.Web.Services.DatabaseStatus;
using PingMonitor.Web.Services.Identity;
using PingMonitor.Web.ViewModels.Admin;

namespace PingMonitor.Web.Controllers;

[Authorize(Roles = ApplicationRoles.Admin)]
[Route("admin/database")]
public sealed class AdminDatabaseController : Controller
{
    private readonly IDatabaseStatusQueryService _databaseStatusQueryService;

    public AdminDatabaseController(IDatabaseStatusQueryService databaseStatusQueryService)
    {
        _databaseStatusQueryService = databaseStatusQueryService;
    }

    [HttpGet]
    public async Task<IActionResult> Index(CancellationToken cancellationToken)
    {
        var snapshot = await _databaseStatusQueryService.GetSnapshotAsync(cancellationToken);

        var tables = snapshot.Tables
            .Select(x => new DatabaseStatusTableViewModel
            {
                TableName = x.TableName,
                ApproximateRowCount = x.ApproximateRowCount,
                DataBytes = x.DataBytes,
                IndexBytes = x.IndexBytes,
                TotalBytes = x.DataBytes + x.IndexBytes
            })
            .ToArray();

        var runtimeBuffer = snapshot.ResultBuffer;
        var queueUtilizationPercent = runtimeBuffer.ConfiguredMaxQueueSize <= 0
            ? 0
            : (runtimeBuffer.CurrentQueueDepth / (double)runtimeBuffer.ConfiguredMaxQueueSize) * 100;
        var bufferDropRatePercent = runtimeBuffer.TotalEnqueueCount == 0
            ? 0
            : (runtimeBuffer.DroppedResultCount / (double)runtimeBuffer.TotalEnqueueCount) * 100;
        var flushSuccessRatePercent = runtimeBuffer.FlushCount == 0
            ? 100
            : ((runtimeBuffer.FlushCount - runtimeBuffer.FailedFlushCount) / (double)runtimeBuffer.FlushCount) * 100;

        var model = new DatabaseStatusPageViewModel
        {
            ProviderName = snapshot.ProviderName,
            DatabaseName = snapshot.DatabaseName,
            DataSource = snapshot.DataSource,
            ServerVersion = snapshot.ServerVersion,
            ConnectionHealthy = snapshot.ConnectionHealthy,
            CurrentSchemaVersion = snapshot.CurrentSchemaVersion,
            RequiredSchemaVersion = snapshot.RequiredSchemaVersion,
            TableCount = snapshot.TableCount,
            TotalDataBytes = snapshot.TotalDataBytes,
            TotalIndexBytes = snapshot.TotalIndexBytes,
            Tables = tables,
            RuntimeBuffer = new DatabaseStatusRuntimeBufferViewModel
            {
                BufferingEnabled = runtimeBuffer.BufferingEnabled,
                ConfiguredMaxBatchSize = runtimeBuffer.ConfiguredMaxBatchSize,
                ConfiguredFlushIntervalSeconds = runtimeBuffer.ConfiguredFlushIntervalSeconds,
                ConfiguredMaxQueueSize = runtimeBuffer.ConfiguredMaxQueueSize,
                CurrentQueueDepth = runtimeBuffer.CurrentQueueDepth,
                DroppedResultCount = runtimeBuffer.DroppedResultCount,
                TotalEnqueueCount = runtimeBuffer.TotalEnqueueCount,
                FlushCount = runtimeBuffer.FlushCount,
                FailedFlushCount = runtimeBuffer.FailedFlushCount,
                PersistedResultCount = runtimeBuffer.PersistedResultCount,
                LastFlushAttemptedCount = runtimeBuffer.LastFlushAttemptedCount,
                LastFlushPersistedCount = runtimeBuffer.LastFlushPersistedCount,
                LastFlushCompletedAtUtc = runtimeBuffer.LastFlushCompletedAtUtc,
                LastFlushError = runtimeBuffer.LastFlushError,
                QueueUtilizationPercent = queueUtilizationPercent,
                BufferDropRatePercent = bufferDropRatePercent,
                FlushSuccessRatePercent = flushSuccessRatePercent
            }
        };

        return View("Index", model);
    }
}

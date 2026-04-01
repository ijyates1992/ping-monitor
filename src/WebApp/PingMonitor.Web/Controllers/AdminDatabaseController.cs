using System.Security.Claims;
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
    private readonly IDatabaseMaintenanceService _databaseMaintenanceService;

    public AdminDatabaseController(
        IDatabaseStatusQueryService databaseStatusQueryService,
        IDatabaseMaintenanceService databaseMaintenanceService)
    {
        _databaseStatusQueryService = databaseStatusQueryService;
        _databaseMaintenanceService = databaseMaintenanceService;
    }

    [HttpGet]
    public async Task<IActionResult> Index(CancellationToken cancellationToken)
    {
        var model = await BuildModelAsync(
            new DatabasePruneForm(),
            null,
            pruneStatusMessage: TempData["DbPruneStatus"] as string,
            backupStatusMessage: TempData["DbBackupStatus"] as string,
            cancellationToken);

        return View("Index", model);
    }

    [HttpPost("prune/preview")]
    [ValidateAntiForgeryToken]
    public async Task<IActionResult> PreviewPrune([FromForm(Name = "PruneForm")] DatabasePruneForm pruneForm, CancellationToken cancellationToken)
    {
        if (!ModelState.IsValid)
        {
            var invalidModel = await BuildModelAsync(pruneForm, null, null, null, cancellationToken);
            return View("Index", invalidModel);
        }

        var cutoffUtc = DateTimeOffset.UtcNow.AddDays(-pruneForm.AgeDays);
        var preview = await _databaseMaintenanceService.PreviewPruneAsync(
            new DatabasePrunePreviewRequest
            {
                Target = pruneForm.Target,
                CutoffUtc = cutoffUtc,
                RequestedBy = GetOperatorIdentity()
            },
            cancellationToken);

        var model = await BuildModelAsync(
            pruneForm,
            new DatabasePrunePreviewViewModel
            {
                Target = preview.Target,
                AgeDays = pruneForm.AgeDays,
                CutoffUtc = preview.CutoffUtc,
                EligibleRowCount = preview.EligibleRowCount
            },
            null,
            null,
            cancellationToken);

        return View("Index", model);
    }

    [HttpPost("prune/execute")]
    [ValidateAntiForgeryToken]
    public async Task<IActionResult> ExecutePrune([FromForm(Name = "PruneForm")] DatabasePruneForm pruneForm, CancellationToken cancellationToken)
    {
        if (!ModelState.IsValid)
        {
            var invalidModel = await BuildModelAsync(pruneForm, null, null, null, cancellationToken);
            return View("Index", invalidModel);
        }

        if (!string.Equals(pruneForm.ConfirmationText?.Trim(), DatabasePruneExecuteRequest.ConfirmationKeyword, StringComparison.Ordinal))
        {
            ModelState.AddModelError($"{nameof(DatabasePruneForm)}.{nameof(DatabasePruneForm.ConfirmationText)}", $"Type {DatabasePruneExecuteRequest.ConfirmationKeyword} to confirm prune.");
            var invalidModel = await BuildModelAsync(pruneForm, null, null, null, cancellationToken);
            return View("Index", invalidModel);
        }

        var cutoffUtc = DateTimeOffset.UtcNow.AddDays(-pruneForm.AgeDays);
        var result = await _databaseMaintenanceService.ExecutePruneAsync(
            new DatabasePruneExecuteRequest
            {
                Target = pruneForm.Target,
                CutoffUtc = cutoffUtc,
                ConfirmationText = pruneForm.ConfirmationText ?? string.Empty,
                RequestedBy = GetOperatorIdentity()
            },
            cancellationToken);

        TempData["DbPruneStatus"] = $"Prune completed for {result.Target}. Deleted {result.RowsDeleted} rows older than {result.CutoffUtc:yyyy-MM-dd HH:mm:ss} UTC.";
        return RedirectToAction(nameof(Index));
    }

    [HttpPost("backup/create")]
    [ValidateAntiForgeryToken]
    public async Task<IActionResult> CreateBackup([FromForm(Name = "BackupForm")] DatabaseBackupForm backupForm, CancellationToken cancellationToken)
    {
        if (!backupForm.ConfirmBackup)
        {
            ModelState.AddModelError($"{nameof(DatabaseBackupForm)}.{nameof(DatabaseBackupForm.ConfirmBackup)}", "Confirm backup creation before continuing.");
            var invalidModel = await BuildModelAsync(new DatabasePruneForm(), null, null, null, cancellationToken);
            return View("Index", invalidModel);
        }

        var result = await _databaseMaintenanceService.CreateBackupAsync(
            new DatabaseBackupCreateRequest
            {
                RequestedBy = GetOperatorIdentity()
            },
            cancellationToken);

        TempData["DbBackupStatus"] = result.Succeeded
            ? $"Database backup created: {result.FileName} ({result.FileSizeBytes} bytes)."
            : $"Database backup failed: {result.Message}";

        return RedirectToAction(nameof(Index));
    }

    [HttpGet("backup/download")]
    public IActionResult DownloadBackup([FromQuery] string fileId)
    {
        try
        {
            var fullPath = _databaseMaintenanceService.ResolveBackupDownloadPath(fileId);
            return PhysicalFile(fullPath, "application/sql", Path.GetFileName(fullPath));
        }
        catch (FileNotFoundException)
        {
            return NotFound();
        }
    }

    private async Task<DatabaseStatusPageViewModel> BuildModelAsync(
        DatabasePruneForm pruneForm,
        DatabasePrunePreviewViewModel? prunePreview,
        string? pruneStatusMessage,
        string? backupStatusMessage,
        CancellationToken cancellationToken)
    {
        var snapshot = await _databaseStatusQueryService.GetSnapshotAsync(cancellationToken);
        var backups = await _databaseMaintenanceService.ListBackupsAsync(cancellationToken);

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

        return new DatabaseStatusPageViewModel
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
            PruneForm = pruneForm,
            PrunePreview = prunePreview,
            PruneStatusMessage = pruneStatusMessage,
            BackupStatusMessage = backupStatusMessage,
            BackupForm = new DatabaseBackupForm(),
            Backups = backups.Select(x => new DatabaseBackupRowViewModel
            {
                FileName = x.FileName,
                FileId = x.FileId,
                CreatedAtUtc = x.CreatedAtUtc,
                SizeBytes = x.FileSizeBytes
            }).ToArray(),
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
    }

    private string? GetOperatorIdentity()
    {
        var userId = User.FindFirstValue(ClaimTypes.NameIdentifier);
        if (!string.IsNullOrWhiteSpace(userId))
        {
            return userId;
        }

        return User.Identity?.Name;
    }
}

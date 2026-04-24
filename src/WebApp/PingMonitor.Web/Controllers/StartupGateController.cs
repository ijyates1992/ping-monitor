using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.Extensions.Options;
using PingMonitor.Web.Options;
using PingMonitor.Web.Services.DatabaseStatus;
using PingMonitor.Web.Services.StartupGate;
using PingMonitor.Web.Support;
using PingMonitor.Web.ViewModels.StartupGate;

namespace PingMonitor.Web.Controllers;

[AllowAnonymous]
[Route("startup-gate")]
public sealed class StartupGateController : Controller
{
    private readonly IStartupGateService _startupGateService;
    private readonly IStartupDatabaseConfigurationStore _configurationStore;
    private readonly IStartupSchemaService _schemaService;
    private readonly IStartupAdminBootstrapService _adminBootstrapService;
    private readonly IDatabaseMaintenanceService _databaseMaintenanceService;
    private readonly IStartupGateDiagnosticsLogger _startupGateDiagnosticsLogger;
    private readonly IDatabaseMaintenanceProgressTracker _progressTracker;
    private readonly IServiceScopeFactory _serviceScopeFactory;
    private readonly StartupGateOptions _options;
    private readonly ILogger<StartupGateController> _logger;

    public StartupGateController(
        IStartupGateService startupGateService,
        IStartupDatabaseConfigurationStore configurationStore,
        IStartupSchemaService schemaService,
        IStartupAdminBootstrapService adminBootstrapService,
        IDatabaseMaintenanceService databaseMaintenanceService,
        IStartupGateDiagnosticsLogger startupGateDiagnosticsLogger,
        IDatabaseMaintenanceProgressTracker progressTracker,
        IServiceScopeFactory serviceScopeFactory,
        IOptions<StartupGateOptions> options,
        ILogger<StartupGateController> logger)
    {
        _startupGateService = startupGateService;
        _configurationStore = configurationStore;
        _schemaService = schemaService;
        _adminBootstrapService = adminBootstrapService;
        _databaseMaintenanceService = databaseMaintenanceService;
        _startupGateDiagnosticsLogger = startupGateDiagnosticsLogger;
        _progressTracker = progressTracker;
        _serviceScopeFactory = serviceScopeFactory;
        _options = options.Value;
        _logger = logger;
    }

    [HttpGet("")]
    public async Task<IActionResult> Index(CancellationToken cancellationToken)
    {
        var status = await _startupGateService.EvaluateAsync(HttpContext, cancellationToken);
        return View("Index", await BuildViewModelAsync(status, null, null, null, null, cancellationToken: cancellationToken));
    }

    [HttpPost("database")]
    [ValidateAntiForgeryToken]
    public async Task<IActionResult> SaveDatabaseConfiguration([FromForm(Name = "DatabaseForm")] StartupDatabaseConfigurationForm form, CancellationToken cancellationToken)
    {
        var status = await _startupGateService.EvaluateAsync(HttpContext, cancellationToken);
        if (!status.CanPerformWriteActions)
        {
            return Forbid();
        }

        if (!ModelState.IsValid)
        {
            return View("Index", await BuildViewModelAsync(status, form, null, null, null, errorMessage: "Database configuration could not be saved.", cancellationToken: cancellationToken));
        }

        _logger.LogInformation(
            "Startup gate database configuration save attempt for {Host}:{Port}/{DatabaseName}.",
            LogValueSanitizer.ForLog(form.Host),
            form.Port,
            LogValueSanitizer.ForLog(form.DatabaseName));

        try
        {
            await _configurationStore.SaveAsync(new StartupDatabaseConfigurationInput
            {
                Host = form.Host,
                Port = form.Port,
                DatabaseName = form.DatabaseName,
                Username = form.Username,
                Password = form.Password
            }, cancellationToken);
        }
        catch (Exception exception)
        {
            _logger.LogWarning(exception, "Startup gate database configuration save failed.");
            status = await _startupGateService.EvaluateAsync(HttpContext, cancellationToken);
            return View("Index", await BuildViewModelAsync(status, form, null, null, null, errorMessage: $"Database configuration save failed: {exception.Message}", cancellationToken: cancellationToken));
        }

        status = await _startupGateService.EvaluateAsync(HttpContext, cancellationToken);
        return View("Index", await BuildViewModelAsync(status, null, null, null, null, statusMessage: "Database configuration saved. Password values are not shown after saving.", cancellationToken: cancellationToken));
    }

    [HttpPost("schema/apply")]
    [ValidateAntiForgeryToken]
    public async Task<IActionResult> ApplySchema(CancellationToken cancellationToken)
    {
        var status = await _startupGateService.EvaluateAsync(HttpContext, cancellationToken);
        if (!status.CanPerformWriteActions)
        {
            return Forbid();
        }

        try
        {
            await _schemaService.ApplySchemaAsync(cancellationToken);
        }
        catch (Exception exception)
        {
            _logger.LogWarning(exception, "Startup gate schema apply failed.");
            status = await _startupGateService.EvaluateAsync(HttpContext, cancellationToken);
            return View("Index", await BuildViewModelAsync(status, null, null, null, null, errorMessage: $"Schema apply failed: {exception.Message}", cancellationToken: cancellationToken));
        }

        status = await _startupGateService.EvaluateAsync(HttpContext, cancellationToken);
        return View("Index", await BuildViewModelAsync(status, null, null, null, null, statusMessage: "Schema apply completed.", cancellationToken: cancellationToken));
    }

    [HttpPost("admin")]
    [ValidateAntiForgeryToken]
    public async Task<IActionResult> CreateInitialAdmin([FromForm(Name = "AdminForm")] StartupAdminBootstrapForm form, CancellationToken cancellationToken)
    {
        var status = await _startupGateService.EvaluateAsync(HttpContext, cancellationToken);
        if (!status.CanPerformWriteActions)
        {
            return Forbid();
        }

        if (!ModelState.IsValid)
        {
            return View("Index", await BuildViewModelAsync(status, null, form, null, null, errorMessage: "Initial admin could not be created.", cancellationToken: cancellationToken));
        }

        var result = await _adminBootstrapService.CreateInitialAdminAsync(form, cancellationToken);
        if (!result.Succeeded)
        {
            foreach (var error in result.Errors)
            {
                ModelState.AddModelError(string.Empty, error);
            }

            status = await _startupGateService.EvaluateAsync(HttpContext, cancellationToken);
            return View("Index", await BuildViewModelAsync(status, null, form, null, null, errorMessage: "Initial admin could not be created.", cancellationToken: cancellationToken));
        }

        status = await _startupGateService.EvaluateAsync(HttpContext, cancellationToken);
        return View("Index", await BuildViewModelAsync(status, null, null, null, null, statusMessage: "Initial admin created.", cancellationToken: cancellationToken));
    }

    [HttpPost("database-backup/upload")]
    [DisableRequestSizeLimit]
    [RequestFormLimits(MultipartBodyLengthLimit = long.MaxValue)]
    [ValidateAntiForgeryToken]
    public async Task<IActionResult> UploadDatabaseBackup([FromForm(Name = "DatabaseBackupUploadForm")] StartupDatabaseBackupUploadForm form, CancellationToken cancellationToken)
    {
        var status = await _startupGateService.EvaluateAsync(HttpContext, cancellationToken);
        if (!status.CanPerformWriteActions)
        {
            return Forbid();
        }

        if (form.BackupFile is null || form.BackupFile.Length <= 0)
        {
            ModelState.AddModelError($"{nameof(StartupDatabaseBackupUploadForm)}.{nameof(StartupDatabaseBackupUploadForm.BackupFile)}", "Select a DATABASE backup file to upload.");
            return View("Index", await BuildViewModelAsync(status, null, null, form, null, errorMessage: "DATABASE backup upload failed.", cancellationToken: cancellationToken));
        }

        await using var stream = form.BackupFile.OpenReadStream();
        var result = await _databaseMaintenanceService.UploadBackupAsync(
            new DatabaseBackupUploadRequest
            {
                OriginalFileName = form.BackupFile.FileName,
                Content = stream,
                RequestedBy = "startup-gate"
            },
            cancellationToken);

        status = await _startupGateService.EvaluateAsync(HttpContext, cancellationToken);
        return View("Index", await BuildViewModelAsync(
            status,
            null,
            null,
            null,
            null,
            statusMessage: result.Succeeded
                ? $"DATABASE backup upload completed: {result.FileName} ({result.FileSizeBytes} bytes)."
                : null,
            errorMessage: result.Succeeded
                ? null
                : $"DATABASE backup upload failed: {result.Message}",
            cancellationToken: cancellationToken));
    }

    [HttpPost("database-backup/restore")]
    [ValidateAntiForgeryToken]
    public async Task<IActionResult> RestoreDatabaseBackup([FromForm(Name = "DatabaseBackupRestoreForm")] StartupDatabaseBackupRestoreForm form, CancellationToken cancellationToken)
    {
        _startupGateDiagnosticsLogger.Write(
            "startup-gate.database-restore.request.entry",
            $"Restore request entered for fileId='{form.FileId ?? "(null)"}'. ConfirmationProvided={(!string.IsNullOrWhiteSpace(form.ConfirmationText)).ToString().ToLowerInvariant()}.");

        var status = await _startupGateService.EvaluateAsync(HttpContext, cancellationToken);
        if (!status.CanPerformWriteActions)
        {
            _startupGateDiagnosticsLogger.Write(
                "startup-gate.database-restore.request.blocked",
                $"Restore request denied because Startup Gate write actions are disabled. mode={status.Mode}, failingStage={status.FailingStage}.");
            return Forbid();
        }

        if (!ModelState.IsValid)
        {
            return View("Index", await BuildViewModelAsync(status, null, null, null, form, errorMessage: "DATABASE restore could not be started.", cancellationToken: cancellationToken));
        }

        if (!string.Equals(form.ConfirmationText?.Trim(), DatabaseBackupRestoreRequest.ConfirmationKeyword, StringComparison.Ordinal))
        {
            ModelState.AddModelError($"{nameof(StartupDatabaseBackupRestoreForm)}.{nameof(StartupDatabaseBackupRestoreForm.ConfirmationText)}", $"Type {DatabaseBackupRestoreRequest.ConfirmationKeyword} to confirm restore.");
            return View("Index", await BuildViewModelAsync(status, null, null, null, form, errorMessage: "DATABASE restore could not be started.", cancellationToken: cancellationToken));
        }

        DatabaseBackupRestoreResult result;
        try
        {
            result = await _databaseMaintenanceService.RestoreBackupAsync(
                new DatabaseBackupRestoreRequest
                {
                    FileId = form.FileId ?? string.Empty,
                    ConfirmationText = form.ConfirmationText ?? string.Empty,
                    RequestedBy = "startup-gate"
                },
                cancellationToken);
        }
        catch (FileNotFoundException)
        {
            _startupGateDiagnosticsLogger.Write(
                "startup-gate.database-restore.file-not-found",
                $"Restore file not found for fileId='{form.FileId ?? "(null)"}'.");
            status = await _startupGateService.EvaluateAsync(HttpContext, cancellationToken);
            return View("Index", await BuildViewModelAsync(status, null, null, null, form, errorMessage: "DATABASE restore failed: selected backup file was not found.", cancellationToken: cancellationToken));
        }
        catch (Exception exception)
        {
            _startupGateDiagnosticsLogger.WriteException(
                "startup-gate.database-restore.exception",
                exception,
                $"Unhandled controller exception while invoking restore for fileId='{form.FileId ?? "(null)"}'.");
            throw;
        }

        status = await _startupGateService.EvaluateAsync(HttpContext, cancellationToken);
        _startupGateDiagnosticsLogger.Write(
            "startup-gate.database-restore.readiness-recheck",
            $"Post-restore Startup Gate re-check completed. mode={status.Mode}, failingStage={status.FailingStage}, canPerformWriteActions={status.CanPerformWriteActions.ToString().ToLowerInvariant()}.");
        return View("Index", await BuildViewModelAsync(
            status,
            null,
            null,
            null,
            null,
            statusMessage: result.Succeeded
                ? result.BackupMode == DatabaseBackupMode.Compact
                    ? $"Compact DATABASE backup restored from {result.RestoredFileName}. Runtime history tables excluded by compact profile were reset to empty. Pre-restore backup: {result.PreRestoreBackupFileName ?? "(none)"}."
                    : $"Full DATABASE backup restored from {result.RestoredFileName}. Pre-restore backup: {result.PreRestoreBackupFileName ?? "(none)"}."
                : null,
            errorMessage: result.Succeeded
                ? null
                : $"DATABASE restore failed: {result.Message}",
            cancellationToken: cancellationToken));
    }

    [HttpPost("database-backup/restore/start")]
    [ValidateAntiForgeryToken]
    public async Task<IActionResult> StartRestoreDatabaseBackup([FromForm(Name = "DatabaseBackupRestoreForm")] StartupDatabaseBackupRestoreForm form, CancellationToken cancellationToken)
    {
        var status = await _startupGateService.EvaluateAsync(HttpContext, cancellationToken);
        if (!status.CanPerformWriteActions)
        {
            return Forbid();
        }

        if (string.IsNullOrWhiteSpace(form.FileId))
        {
            return BadRequest(new { succeeded = false, message = "Select a DATABASE backup file to restore." });
        }

        if (!string.Equals(form.ConfirmationText?.Trim(), DatabaseBackupRestoreRequest.ConfirmationKeyword, StringComparison.Ordinal))
        {
            return BadRequest(new { succeeded = false, message = $"Type {DatabaseBackupRestoreRequest.ConfirmationKeyword} to confirm restore." });
        }

        var start = _progressTracker.TryStartOperation(
            DatabaseMaintenanceOperationType.Restore,
            stage: "validating backup",
            fileName: form.FileId,
            detailsMessage: "startup-gate");
        if (!start.Started || start.Operation is null)
        {
            return Conflict(new { succeeded = false, message = start.Message, activeOperation = start.Operation });
        }

        var operationId = start.Operation.OperationId;
        _ = Task.Run(async () =>
        {
            try
            {
                using var scope = _serviceScopeFactory.CreateScope();
                var maintenanceService = scope.ServiceProvider.GetRequiredService<IDatabaseMaintenanceService>();
                var result = await maintenanceService.RestoreBackupAsync(
                    new DatabaseBackupRestoreRequest
                    {
                        OperationId = operationId,
                        FileId = form.FileId ?? string.Empty,
                        ConfirmationText = form.ConfirmationText ?? string.Empty,
                        RequestedBy = "startup-gate"
                    },
                    CancellationToken.None);

                if (!result.Succeeded)
                {
                    _progressTracker.CompleteFailure(operationId, "failed", result.Message, result.RestoredFileName);
                }
            }
            catch (FileNotFoundException)
            {
                _progressTracker.CompleteFailure(operationId, "failed", "Selected backup file was not found.", form.FileId);
            }
            catch (Exception ex)
            {
                _progressTracker.CompleteFailure(operationId, "failed", ex.Message, form.FileId);
            }
        });

        return Json(new { succeeded = true, operationId });
    }

    [HttpGet("database-backup/progress")]
    public IActionResult GetDatabaseBackupProgress([FromQuery] string? operationId = null)
    {
        var operation = string.IsNullOrWhiteSpace(operationId)
            ? _progressTracker.GetCurrentOperation()
            : _progressTracker.GetOperation(operationId);
        return Json(new { operation });
    }

    private async Task<StartupGatePageViewModel> BuildViewModelAsync(
        StartupGateStatus status,
        StartupDatabaseConfigurationForm? databaseForm,
        StartupAdminBootstrapForm? adminForm,
        StartupDatabaseBackupUploadForm? databaseBackupUploadForm,
        StartupDatabaseBackupRestoreForm? databaseBackupRestoreForm,
        string? statusMessage = null,
        string? errorMessage = null,
        CancellationToken cancellationToken = default)
    {
        var existingConfig = status.DatabaseConfiguration;
        var databaseBackups = await _databaseMaintenanceService.ListBackupsAsync(cancellationToken);

        return new StartupGatePageViewModel
        {
            Status = status,
            StatusMessage = statusMessage,
            ErrorMessage = errorMessage,
            DatabaseForm = databaseForm ?? new StartupDatabaseConfigurationForm
            {
                Host = existingConfig?.Host ?? string.Empty,
                Port = existingConfig?.Port > 0 ? existingConfig.Port : _options.DefaultMySqlPort,
                DatabaseName = existingConfig?.DatabaseName ?? string.Empty,
                Username = existingConfig?.Username ?? string.Empty
            },
            AdminForm = adminForm ?? new StartupAdminBootstrapForm(),
            DatabaseBackupUploadForm = databaseBackupUploadForm ?? new StartupDatabaseBackupUploadForm(),
            DatabaseBackupRestoreForm = databaseBackupRestoreForm ?? new StartupDatabaseBackupRestoreForm(),
            DatabaseBackups = databaseBackups
        };
    }
}

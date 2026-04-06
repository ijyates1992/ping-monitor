using System.ComponentModel;
using System.Diagnostics;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using System.Text.RegularExpressions;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Options;
using MySqlConnector;
using PingMonitor.Web.Data;
using PingMonitor.Web.Models;
using PingMonitor.Web.Options;
using PingMonitor.Web.Services.EventLogs;
using PingMonitor.Web.Services.StartupGate;

namespace PingMonitor.Web.Services.DatabaseStatus;

internal sealed class DatabaseMaintenanceService : IDatabaseMaintenanceService
{
    private const int MaxUploadBytes = 100 * 1024 * 1024;
    private const string BackupModeCommentPrefix = "-- pingmonitor_backup_mode:";
    private const string BackupProfileCommentPrefix = "-- pingmonitor_backup_profile:";
    private const string CompactBackupProfileName = "compact_runtime_excluded_v1";
    private const int MetadataProbeBytes = 16 * 1024;
    private static readonly string[] CompactExcludedTables =
    [
        "AgentHeartbeatHistory",
        "CheckResults",
        "ResultBatches",
        "StateTransitions",
        "AssignmentRttMinuteBuckets",
        "AssignmentStateIntervals",
        "EventLogs",
        "SecurityAuthLogs"
    ];

    private readonly PingMonitorDbContext _dbContext;
    private readonly IEventLogService _eventLogService;
    private readonly IOptions<DatabaseMaintenanceOptions> _options;
    private readonly IWebHostEnvironment _webHostEnvironment;
    private readonly IStartupGateDiagnosticsLogger _startupGateDiagnosticsLogger;
    private readonly ILogger<DatabaseMaintenanceService> _logger;

    public DatabaseMaintenanceService(
        PingMonitorDbContext dbContext,
        IEventLogService eventLogService,
        IOptions<DatabaseMaintenanceOptions> options,
        IWebHostEnvironment webHostEnvironment,
        IStartupGateDiagnosticsLogger startupGateDiagnosticsLogger,
        ILogger<DatabaseMaintenanceService> logger)
    {
        _dbContext = dbContext;
        _eventLogService = eventLogService;
        _options = options;
        _webHostEnvironment = webHostEnvironment;
        _startupGateDiagnosticsLogger = startupGateDiagnosticsLogger;
        _logger = logger;
    }

    public async Task<DatabasePrunePreviewResult> PreviewPruneAsync(DatabasePrunePreviewRequest request, CancellationToken cancellationToken)
    {
        var eligibleRowCount = await GetEligibleRowCountAsync(request.Target, request.CutoffUtc, cancellationToken);

        await _eventLogService.WriteAsync(new EventLogWriteRequest
        {
            Category = EventCategory.System,
            EventType = EventType.DatabasePrunePreviewRequested,
            Severity = EventSeverity.Info,
            Message = $"Database prune preview requested for {request.Target} with cutoff {request.CutoffUtc:O}.",
            DetailsJson = JsonSerializer.Serialize(new
            {
                target = request.Target.ToString(),
                cutoffUtc = request.CutoffUtc,
                eligibleRowCount,
                requestedBy = request.RequestedBy
            })
        }, cancellationToken);

        return new DatabasePrunePreviewResult
        {
            Target = request.Target,
            CutoffUtc = request.CutoffUtc,
            EligibleRowCount = eligibleRowCount
        };
    }

    public async Task<DatabasePruneExecuteResult> ExecutePruneAsync(DatabasePruneExecuteRequest request, CancellationToken cancellationToken)
    {
        if (!string.Equals(request.ConfirmationText?.Trim(), DatabasePruneExecuteRequest.ConfirmationKeyword, StringComparison.Ordinal))
        {
            throw new InvalidOperationException($"Type {DatabasePruneExecuteRequest.ConfirmationKeyword} to confirm prune.");
        }

        var eligibleRowCountBeforeDelete = await GetEligibleRowCountAsync(request.Target, request.CutoffUtc, cancellationToken);

        await _eventLogService.WriteAsync(new EventLogWriteRequest
        {
            Category = EventCategory.System,
            EventType = EventType.DatabasePruneExecuted,
            Severity = EventSeverity.Warning,
            Message = $"Database prune started for {request.Target} with cutoff {request.CutoffUtc:O}.",
            DetailsJson = JsonSerializer.Serialize(new
            {
                target = request.Target.ToString(),
                cutoffUtc = request.CutoffUtc,
                eligibleRowCountBeforeDelete,
                requestedBy = request.RequestedBy
            })
        }, cancellationToken);

        var rowsDeleted = await ExecutePruneDeleteAsync(request.Target, request.CutoffUtc, cancellationToken);

        await _eventLogService.WriteAsync(new EventLogWriteRequest
        {
            Category = EventCategory.System,
            EventType = EventType.DatabasePruneCompleted,
            Severity = EventSeverity.Warning,
            Message = $"Database prune completed for {request.Target}. Deleted {rowsDeleted} rows.",
            DetailsJson = JsonSerializer.Serialize(new
            {
                target = request.Target.ToString(),
                cutoffUtc = request.CutoffUtc,
                eligibleRowCountBeforeDelete,
                rowsDeleted,
                requestedBy = request.RequestedBy
            })
        }, cancellationToken);

        return new DatabasePruneExecuteResult
        {
            Target = request.Target,
            CutoffUtc = request.CutoffUtc,
            EligibleRowCountBeforeDelete = eligibleRowCountBeforeDelete,
            RowsDeleted = rowsDeleted
        };
    }

    public async Task<DatabaseBackupCreateResult> CreateBackupAsync(DatabaseBackupCreateRequest request, CancellationToken cancellationToken)
    {
        var options = _options.Value;
        var backupDirectory = GetBackupDirectory(options.BackupStoragePath);
        Directory.CreateDirectory(backupDirectory);
        var suppressEventLogWrites = request.SuppressEventLogWrites;

        await WriteDatabaseBackupEventAsync(
            new EventLogWriteRequest
            {
                Category = EventCategory.System,
                EventType = EventType.DatabaseBackupStarted,
                Severity = EventSeverity.Info,
                Message = "Database backup export started.",
                DetailsJson = JsonSerializer.Serialize(new
                {
                    backupDirectory,
                    requestedBy = request.RequestedBy
                })
            },
            suppressEventLogWrites,
            cancellationToken);

        var dbConnectionString = _dbContext.Database.GetConnectionString();
        if (string.IsNullOrWhiteSpace(dbConnectionString))
        {
            return await CreateBackupFailureAsync("Database connection string is unavailable.", request.RequestedBy, suppressEventLogWrites, cancellationToken);
        }

        var builder = new MySqlConnectionStringBuilder(dbConnectionString);
        var backupTimestamp = DateTimeOffset.UtcNow;
        var safeDatabaseName = string.IsNullOrWhiteSpace(builder.Database) ? "database" : builder.Database.Trim();
        var normalizedBackupMode = NormalizeBackupMode(request.BackupMode);
        var fileName = $"db-backup-{safeDatabaseName}-{backupTimestamp:yyyyMMdd-HHmmss}.sql";
        var fullPath = Path.Combine(backupDirectory, fileName);
        var mysqldumpExecutablePath = ResolveExecutablePath(options.MySqlDumpExecutablePath, "mysqldump");
        if (mysqldumpExecutablePath is null)
        {
            return await CreateBackupFailureAsync(
                "mysqldump executable was not found. Configure DatabaseMaintenance:MySqlDumpExecutablePath with the full executable path.",
                request.RequestedBy,
                suppressEventLogWrites,
                cancellationToken);
        }

        var processInfo = new ProcessStartInfo
        {
            FileName = mysqldumpExecutablePath,
            WorkingDirectory = backupDirectory,
            RedirectStandardOutput = true,
            RedirectStandardError = true,
            RedirectStandardInput = false,
            UseShellExecute = false,
            CreateNoWindow = true
        };

        processInfo.ArgumentList.Add("--single-transaction");
        processInfo.ArgumentList.Add("--routines");
        processInfo.ArgumentList.Add("--triggers");
        processInfo.ArgumentList.Add("--events");
        processInfo.ArgumentList.Add("--host");
        processInfo.ArgumentList.Add(builder.Server);
        processInfo.ArgumentList.Add("--port");
        processInfo.ArgumentList.Add(builder.Port.ToString(System.Globalization.CultureInfo.InvariantCulture));
        processInfo.ArgumentList.Add("--user");
        processInfo.ArgumentList.Add(builder.UserID);
        processInfo.ArgumentList.Add(builder.Database);
        if (normalizedBackupMode == DatabaseBackupMode.Compact)
        {
            foreach (var excludedTable in CompactExcludedTables)
            {
                processInfo.ArgumentList.Add($"--ignore-table={builder.Database}.{excludedTable}");
            }
        }

        processInfo.Environment["MYSQL_PWD"] = builder.Password;

        try
        {
            using var process = Process.Start(processInfo);
            if (process is null)
            {
                return await CreateBackupFailureAsync("Unable to start mysqldump process.", request.RequestedBy, suppressEventLogWrites, cancellationToken);
            }

            await using (var fileStream = new FileStream(fullPath, FileMode.Create, FileAccess.Write, FileShare.None))
            {
                var headerText = BuildBackupHeader(normalizedBackupMode, backupTimestamp);
                var headerBytes = Encoding.UTF8.GetBytes(headerText);
                await fileStream.WriteAsync(headerBytes, cancellationToken);
                await process.StandardOutput.BaseStream.CopyToAsync(fileStream, cancellationToken);
            }

            var stderr = await process.StandardError.ReadToEndAsync(cancellationToken);
            await process.WaitForExitAsync(cancellationToken);

            if (process.ExitCode != 0)
            {
                if (File.Exists(fullPath))
                {
                    File.Delete(fullPath);
                }

                var message = $"mysqldump exited with code {process.ExitCode}. {TrimDiagnostic(stderr)}";
                return await CreateBackupFailureAsync(message, request.RequestedBy, suppressEventLogWrites, cancellationToken);
            }

            var fileInfo = new FileInfo(fullPath);
            await WriteDatabaseBackupEventAsync(
                new EventLogWriteRequest
                {
                    Category = EventCategory.System,
                    EventType = EventType.DatabaseBackupCompleted,
                    Severity = EventSeverity.Info,
                    Message = $"Database backup export completed. File: {fileName}.",
                    DetailsJson = JsonSerializer.Serialize(new
                    {
                        fileName,
                        fullPath,
                        backupMode = normalizedBackupMode.ToString(),
                        fileSizeBytes = fileInfo.Length,
                        createdAtUtc = backupTimestamp,
                        requestedBy = request.RequestedBy
                    })
                },
                suppressEventLogWrites,
                cancellationToken);

            return new DatabaseBackupCreateResult
            {
                Succeeded = true,
                Message = "Database backup export completed successfully.",
                FileName = fileName,
                FullPath = fullPath,
                BackupMode = normalizedBackupMode,
                FileSizeBytes = fileInfo.Length,
                CreatedAtUtc = backupTimestamp
            };
        }
        catch (Exception ex) when (ex is Win32Exception || ex is InvalidOperationException || ex is IOException)
        {
            if (File.Exists(fullPath))
            {
                File.Delete(fullPath);
            }

            _logger.LogWarning(ex, "Database backup export failed.");
            return await CreateBackupFailureAsync($"Database backup export failed: {ex.Message}", request.RequestedBy, suppressEventLogWrites, cancellationToken);
        }
    }

    public async Task<DatabaseBackupUploadResult> UploadBackupAsync(DatabaseBackupUploadRequest request, CancellationToken cancellationToken)
    {
        if (request.Content is null || request.Content == Stream.Null)
        {
            throw new InvalidOperationException("Database backup upload file is required.");
        }

        var backupDirectory = GetBackupDirectory(_options.Value.BackupStoragePath);
        Directory.CreateDirectory(backupDirectory);

        var safeOriginalName = SanitizeFileName(request.OriginalFileName);
        if (string.IsNullOrWhiteSpace(safeOriginalName))
        {
            return new DatabaseBackupUploadResult { Succeeded = false, Message = "Backup file name is invalid." };
        }

        if (!safeOriginalName.EndsWith(".sql", StringComparison.OrdinalIgnoreCase))
        {
            return new DatabaseBackupUploadResult { Succeeded = false, Message = "Only .sql database backup files are accepted." };
        }

        await using var uploadBuffer = new MemoryStream();
        await request.Content.CopyToAsync(uploadBuffer, cancellationToken);
        if (uploadBuffer.Length <= 0)
        {
            return new DatabaseBackupUploadResult { Succeeded = false, Message = "Backup file is empty." };
        }

        if (uploadBuffer.Length > MaxUploadBytes)
        {
            return new DatabaseBackupUploadResult { Succeeded = false, Message = $"Backup file exceeds the {MaxUploadBytes / (1024 * 1024)} MB upload limit." };
        }

        var contentBytes = uploadBuffer.ToArray();
        var validationMessage = ValidateSqlBackupContent(contentBytes);
        if (validationMessage is not null)
        {
            return new DatabaseBackupUploadResult { Succeeded = false, Message = validationMessage };
        }

        var uploadedAtUtc = DateTimeOffset.UtcNow;
        var storedFileName = BuildStoredUploadFileName(safeOriginalName, uploadedAtUtc);
        var fullPath = Path.Combine(backupDirectory, storedFileName);
        EnsurePathIsWithinDirectory(backupDirectory, fullPath);

        await File.WriteAllBytesAsync(fullPath, contentBytes, cancellationToken);
        var fileInfo = new FileInfo(fullPath);

        await _eventLogService.WriteAsync(new EventLogWriteRequest
        {
            Category = EventCategory.System,
            EventType = EventType.DatabaseBackupUploadCompleted,
            Severity = EventSeverity.Warning,
            Message = $"Database backup upload completed. File: {storedFileName}.",
            DetailsJson = JsonSerializer.Serialize(new
            {
                storedFileName,
                originalFileName = request.OriginalFileName,
                uploadedAtUtc,
                fileSizeBytes = fileInfo.Length,
                requestedBy = request.RequestedBy
            })
        }, cancellationToken);

        return new DatabaseBackupUploadResult
        {
            Succeeded = true,
            Message = "Database backup upload completed successfully.",
            FileName = storedFileName,
            UploadedAtUtc = uploadedAtUtc,
            FileSizeBytes = fileInfo.Length
        };
    }

    public async Task<DatabaseBackupRestoreResult> RestoreBackupAsync(DatabaseBackupRestoreRequest request, CancellationToken cancellationToken)
    {
        var restoreStopwatch = Stopwatch.StartNew();
        var isStartupGateRestoreRequest = IsStartupGateRestoreRequest(request);
        _startupGateDiagnosticsLogger.Write(
            "database-restore.entry",
            $"Restore requested. requestedBy='{request.RequestedBy ?? "(null)"}', startupGateModeRequest={isStartupGateRestoreRequest.ToString().ToLowerInvariant()}, fileId='{request.FileId}'.");

        if (!string.Equals(request.ConfirmationText?.Trim(), DatabaseBackupRestoreRequest.ConfirmationKeyword, StringComparison.Ordinal))
        {
            throw new InvalidOperationException($"Type {DatabaseBackupRestoreRequest.ConfirmationKeyword} to confirm restore.");
        }

        var backupPath = ResolveBackupDownloadPath(request.FileId);
        var backupFileInfo = new FileInfo(backupPath);
        _startupGateDiagnosticsLogger.Write(
            "database-restore.backup-selected",
            $"Selected backup file. fileId='{request.FileId}', fileName='{backupFileInfo.Name}', fullPath='{backupPath}', fileSizeBytes={backupFileInfo.Length}.");
        var contentBytes = await File.ReadAllBytesAsync(backupPath, cancellationToken);
        var backupDescriptor = ReadBackupDescriptorFromContent(contentBytes);
        var validationMessage = ValidateSqlBackupContent(contentBytes);
        if (validationMessage is not null)
        {
            _logger.LogWarning(
                "Database backup restore rejected before execution. FileId: {FileId}, FileName: {FileName}, ValidationMessage: {ValidationMessage}, RequestedBy: {RequestedBy}",
                request.FileId,
                backupFileInfo.Name,
                validationMessage,
                request.RequestedBy);

            return new DatabaseBackupRestoreResult
            {
                Succeeded = false,
                Message = validationMessage,
                RestoredFileName = backupFileInfo.Name,
                RestoredFileCreatedAtUtc = new DateTimeOffset(backupFileInfo.LastWriteTimeUtc, TimeSpan.Zero),
                BackupMode = backupDescriptor.Mode,
                PreRestoreBackupCreated = false
            };
        }

        _logger.LogInformation(
            "Database backup restore started. FileId: {FileId}, FileName: {FileName}, BackupMode: {BackupMode}, FileSizeBytes: {FileSizeBytes}, RequestedBy: {RequestedBy}",
            request.FileId,
            backupFileInfo.Name,
            backupDescriptor.Mode,
            backupFileInfo.Length,
            request.RequestedBy);

        _startupGateDiagnosticsLogger.Write(
            "database-restore.pre-restore-backup.start",
            $"Starting pre-restore backup. fileName='{backupFileInfo.Name}', backupMode={backupDescriptor.Mode}, elapsedMs={restoreStopwatch.ElapsedMilliseconds}.");
        var preRestoreBackup = await CreateBackupAsync(
            new DatabaseBackupCreateRequest
            {
                RequestedBy = request.RequestedBy,
                SuppressEventLogWrites = isStartupGateRestoreRequest
            },
            cancellationToken);
        _startupGateDiagnosticsLogger.Write(
            "database-restore.pre-restore-backup.complete",
            $"Pre-restore backup completed. succeeded={preRestoreBackup.Succeeded.ToString().ToLowerInvariant()}, fileName='{preRestoreBackup.FileName ?? "(none)"}', message='{preRestoreBackup.Message}', elapsedMs={restoreStopwatch.ElapsedMilliseconds}.");
        if (!preRestoreBackup.Succeeded)
        {
            _logger.LogWarning(
                "Database backup restore aborted because pre-restore backup failed. FileId: {FileId}, FileName: {FileName}, PreRestoreBackupMessage: {PreRestoreBackupMessage}, RequestedBy: {RequestedBy}",
                request.FileId,
                backupFileInfo.Name,
                preRestoreBackup.Message,
                request.RequestedBy);

            return new DatabaseBackupRestoreResult
            {
                Succeeded = false,
                Message = $"Restore was aborted because pre-restore backup failed: {preRestoreBackup.Message}",
                RestoredFileName = backupFileInfo.Name,
                RestoredFileCreatedAtUtc = new DateTimeOffset(backupFileInfo.LastWriteTimeUtc, TimeSpan.Zero),
                BackupMode = backupDescriptor.Mode,
                PreRestoreBackupCreated = false
            };
        }

        var dbConnectionString = _dbContext.Database.GetConnectionString();
        if (string.IsNullOrWhiteSpace(dbConnectionString))
        {
            return await CreateRestoreFailureAsync("Database connection string is unavailable.", backupFileInfo, request, backupDescriptor.Mode, true, preRestoreBackup.FileName, cancellationToken);
        }

        var builder = new MySqlConnectionStringBuilder(dbConnectionString);
        var mysqlExecutablePath = ResolveExecutablePath(null, "mysql");
        if (mysqlExecutablePath is null)
        {
            return await CreateRestoreFailureAsync("mysql executable was not found. Ensure mysql client is installed and available in PATH.", backupFileInfo, request, backupDescriptor.Mode, true, preRestoreBackup.FileName, cancellationToken);
        }

        var processInfo = new ProcessStartInfo
        {
            FileName = mysqlExecutablePath,
            RedirectStandardInput = true,
            RedirectStandardOutput = true,
            RedirectStandardError = true,
            UseShellExecute = false,
            CreateNoWindow = true
        };

        processInfo.ArgumentList.Add("--host");
        processInfo.ArgumentList.Add(builder.Server);
        processInfo.ArgumentList.Add("--port");
        processInfo.ArgumentList.Add(builder.Port.ToString(System.Globalization.CultureInfo.InvariantCulture));
        processInfo.ArgumentList.Add("--user");
        processInfo.ArgumentList.Add(builder.UserID);
        processInfo.ArgumentList.Add(builder.Database);
        processInfo.Environment["MYSQL_PWD"] = builder.Password;
        var argumentSummary = string.Join(' ', processInfo.ArgumentList.Select(QuoteProcessArgument));

        try
        {
            _startupGateDiagnosticsLogger.Write(
                "database-restore.process.launch",
                $"Launching mysql restore process. executable='{processInfo.FileName}', arguments=\"{argumentSummary}\", elapsedMs={restoreStopwatch.ElapsedMilliseconds}.");
            using var process = Process.Start(processInfo);
            if (process is null)
            {
                return await CreateRestoreFailureAsync("Unable to start mysql restore process.", backupFileInfo, request, backupDescriptor.Mode, true, preRestoreBackup.FileName, cancellationToken);
            }

            _startupGateDiagnosticsLogger.Write(
                "database-restore.process.started",
                $"mysql restore process started. processId={process.Id}, elapsedMs={restoreStopwatch.ElapsedMilliseconds}.");
            _startupGateDiagnosticsLogger.Write(
                "database-restore.stdin-copy.start",
                $"Starting stdin copy of restore payload. bytes={contentBytes.Length}, processId={process.Id}, elapsedMs={restoreStopwatch.ElapsedMilliseconds}.");
            await process.StandardInput.BaseStream.WriteAsync(contentBytes, cancellationToken);
            await process.StandardInput.BaseStream.FlushAsync(cancellationToken);
            _startupGateDiagnosticsLogger.Write(
                "database-restore.stdin-copy.complete",
                $"Completed stdin copy of restore payload. bytes={contentBytes.Length}, processId={process.Id}, elapsedMs={restoreStopwatch.ElapsedMilliseconds}.");
            process.StandardInput.Close();
            _startupGateDiagnosticsLogger.Write(
                "database-restore.stdin.close",
                $"Closed mysql restore process stdin. processId={process.Id}, elapsedMs={restoreStopwatch.ElapsedMilliseconds}.");
            var stderrReadTask = process.StandardError.ReadToEndAsync(cancellationToken);
            var stdoutReadTask = process.StandardOutput.ReadToEndAsync(cancellationToken);
            _startupGateDiagnosticsLogger.Write(
                "database-restore.process.waiting-for-exit",
                $"Waiting for mysql restore process exit. processId={process.Id}, elapsedMs={restoreStopwatch.ElapsedMilliseconds}.");
            await process.WaitForExitAsync(cancellationToken);
            var stderr = await stderrReadTask;
            var stdout = await stdoutReadTask;
            _startupGateDiagnosticsLogger.Write(
                "database-restore.process.exit",
                $"mysql restore process exited. processId={process.Id}, exitCode={process.ExitCode}, elapsedMs={restoreStopwatch.ElapsedMilliseconds}.");
            _startupGateDiagnosticsLogger.Write(
                "database-restore.process.output-summary",
                $"stdout='{TrimDiagnostic(stdout)}', stderr='{TrimDiagnostic(stderr)}', elapsedMs={restoreStopwatch.ElapsedMilliseconds}.");

            if (process.ExitCode != 0)
            {
                return await CreateRestoreFailureAsync($"mysql restore exited with code {process.ExitCode}. {TrimDiagnostic(stderr)}", backupFileInfo, request, backupDescriptor.Mode, true, preRestoreBackup.FileName, cancellationToken);
            }

            if (backupDescriptor.Mode == DatabaseBackupMode.Compact)
            {
                await ResetCompactExcludedTablesAsync(cancellationToken);
            }

            await _eventLogService.WriteAsync(new EventLogWriteRequest
            {
                Category = EventCategory.System,
                EventType = EventType.DatabaseBackupRestoreCompleted,
                Severity = EventSeverity.Warning,
                Message = $"Database backup restore completed. File: {backupFileInfo.Name}.",
                DetailsJson = JsonSerializer.Serialize(new
                {
                    request.FileId,
                    fileName = backupFileInfo.Name,
                    backupMode = backupDescriptor.Mode.ToString(),
                    restoredFileCreatedAtUtc = backupFileInfo.LastWriteTimeUtc,
                    preRestoreBackupFileName = preRestoreBackup.FileName,
                    requestedBy = request.RequestedBy
                })
            }, cancellationToken);

            return new DatabaseBackupRestoreResult
            {
                Succeeded = true,
                Message = "Database restore completed successfully.",
                RestoredFileName = backupFileInfo.Name,
                RestoredFileCreatedAtUtc = new DateTimeOffset(backupFileInfo.LastWriteTimeUtc, TimeSpan.Zero),
                BackupMode = backupDescriptor.Mode,
                PreRestoreBackupCreated = true,
                PreRestoreBackupFileName = preRestoreBackup.FileName
            };
        }
        catch (Exception ex) when (ex is Win32Exception || ex is InvalidOperationException || ex is IOException)
        {
            _logger.LogWarning(ex, "Database backup restore failed.");
            _startupGateDiagnosticsLogger.WriteException(
                "database-restore.exception",
                ex,
                $"Restore failed with handled exception category. elapsedMs={restoreStopwatch.ElapsedMilliseconds}.");
            return await CreateRestoreFailureAsync($"Database restore failed: {ex.Message}", backupFileInfo, request, backupDescriptor.Mode, true, preRestoreBackup.FileName, cancellationToken);
        }
        catch (Exception ex)
        {
            _startupGateDiagnosticsLogger.WriteException(
                "database-restore.exception-unhandled",
                ex,
                $"Restore failed with unhandled exception category. elapsedMs={restoreStopwatch.ElapsedMilliseconds}.");
            throw;
        }
    }

    public async Task<DatabaseBackupDeleteResult> DeleteBackupAsync(DatabaseBackupDeleteRequest request, CancellationToken cancellationToken)
    {
        if (!request.ConfirmDelete)
        {
            return new DatabaseBackupDeleteResult
            {
                Succeeded = false,
                Message = "Delete confirmation is required."
            };
        }

        var backupPath = ResolveBackupDownloadPath(request.FileId);
        var fileName = Path.GetFileName(backupPath);
        File.Delete(backupPath);

        await _eventLogService.WriteAsync(new EventLogWriteRequest
        {
            Category = EventCategory.System,
            EventType = EventType.DatabaseBackupDeleted,
            Severity = EventSeverity.Warning,
            Message = $"Database backup deleted. File: {fileName}.",
            DetailsJson = JsonSerializer.Serialize(new
            {
                request.FileId,
                fileName,
                requestedBy = request.RequestedBy
            })
        }, cancellationToken);

        return new DatabaseBackupDeleteResult
        {
            Succeeded = true,
            Message = "Database backup deleted.",
            FileName = fileName
        };
    }

    private string? ResolveExecutablePath(string? configuredPath, string fallbackExecutableName)
    {
        var candidateNames = new List<string>();
        if (!string.IsNullOrWhiteSpace(configuredPath))
        {
            candidateNames.Add(configuredPath.Trim());
        }

        candidateNames.Add(fallbackExecutableName);

        foreach (var trimmedPath in candidateNames)
        {
            if (string.IsNullOrWhiteSpace(trimmedPath))
            {
                continue;
            }

            if (Path.IsPathRooted(trimmedPath) || trimmedPath.Contains(Path.DirectorySeparatorChar) || trimmedPath.Contains(Path.AltDirectorySeparatorChar))
            {
                var rootedPath = Path.IsPathRooted(trimmedPath)
                    ? trimmedPath
                    : Path.GetFullPath(trimmedPath, _webHostEnvironment.ContentRootPath);
                if (File.Exists(rootedPath))
                {
                    return rootedPath;
                }
            }

            var pathValue = Environment.GetEnvironmentVariable("PATH");
            if (!string.IsNullOrWhiteSpace(pathValue))
            {
                foreach (var pathEntry in pathValue.Split(Path.PathSeparator, StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries))
                {
                    var candidate = Path.Combine(pathEntry, trimmedPath);
                    if (File.Exists(candidate))
                    {
                        return candidate;
                    }

                    if (OperatingSystem.IsWindows())
                    {
                        var candidateWithExtension = candidate.EndsWith(".exe", StringComparison.OrdinalIgnoreCase)
                            ? candidate
                            : $"{candidate}.exe";
                        if (File.Exists(candidateWithExtension))
                        {
                            return candidateWithExtension;
                        }
                    }
                }
            }
        }

        if (!OperatingSystem.IsWindows())
        {
            return null;
        }

        foreach (var programFilesDirectory in EnumerateWindowsProgramFilesRoots())
        {
            if (!Directory.Exists(programFilesDirectory))
            {
                continue;
            }

            var mysqlRoot = Path.Combine(programFilesDirectory, "MySQL");
            if (!Directory.Exists(mysqlRoot))
            {
                continue;
            }

            var serverDirectories = Directory
                .EnumerateDirectories(mysqlRoot, "MySQL Server *", SearchOption.TopDirectoryOnly)
                .OrderByDescending(x => x, StringComparer.OrdinalIgnoreCase);
            foreach (var serverDirectory in serverDirectories)
            {
                var candidate = Path.Combine(serverDirectory, "bin", "mysqldump.exe");
                if (File.Exists(candidate))
                {
                    return candidate;
                }
            }
        }

        return null;
    }

    private static IEnumerable<string> EnumerateWindowsProgramFilesRoots()
    {
        var roots = new[]
        {
            Environment.GetFolderPath(Environment.SpecialFolder.ProgramFiles),
            Environment.GetFolderPath(Environment.SpecialFolder.ProgramFilesX86)
        };

        foreach (var root in roots.Where(x => !string.IsNullOrWhiteSpace(x)).Distinct(StringComparer.OrdinalIgnoreCase))
        {
            yield return root;
        }
    }

    public Task<IReadOnlyList<DatabaseBackupFileSnapshot>> ListBackupsAsync(CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();

        var backupDirectory = GetBackupDirectory(_options.Value.BackupStoragePath);
        if (!Directory.Exists(backupDirectory))
        {
            return Task.FromResult<IReadOnlyList<DatabaseBackupFileSnapshot>>(Array.Empty<DatabaseBackupFileSnapshot>());
        }

        var files = Directory
            .GetFiles(backupDirectory, "*.sql", SearchOption.TopDirectoryOnly)
            .Select(path => new FileInfo(path))
            .OrderByDescending(x => x.LastWriteTimeUtc)
            .Select(x =>
            {
                // Probe lightweight backup metadata from the SQL comment header when available.
                // This allows operator-visible mode (Full vs Compact) and restore warnings.
                // Uploaded files without PingMonitor metadata default to Full mode semantics.
                // The restore path also independently re-parses metadata from content bytes.
                var descriptor = ReadBackupDescriptorFromFile(x.FullName);
                return new DatabaseBackupFileSnapshot
                {
                    FileName = x.Name,
                    FileId = ComputeFileId(x.Name),
                    CreatedAtUtc = new DateTimeOffset(x.LastWriteTimeUtc, TimeSpan.Zero),
                    FileSizeBytes = x.Length,
                    FullPath = x.FullName,
                    MetadataSummary = BuildMetadataSummary(x.FullName),
                    BackupSource = x.Name.StartsWith("uploaded-db-backup-", StringComparison.OrdinalIgnoreCase) ? "Uploaded" : "Created",
                    BackupMode = descriptor.Mode,
                    BackupModeDisplayName = descriptor.Mode == DatabaseBackupMode.Compact
                        ? "Compact database backup"
                        : "Full database backup"
                };
            })
            .ToArray();

        return Task.FromResult<IReadOnlyList<DatabaseBackupFileSnapshot>>(files);
    }

    public string ResolveBackupDownloadPath(string fileId)
    {
        if (string.IsNullOrWhiteSpace(fileId))
        {
            throw new FileNotFoundException("Backup file id is missing.");
        }

        var backupDirectory = GetBackupDirectory(_options.Value.BackupStoragePath);
        if (!Directory.Exists(backupDirectory))
        {
            throw new FileNotFoundException("Backup directory not found.");
        }

        var matched = Directory
            .GetFiles(backupDirectory, "*.sql", SearchOption.TopDirectoryOnly)
            .FirstOrDefault(path => string.Equals(ComputeFileId(Path.GetFileName(path)), fileId.Trim(), StringComparison.Ordinal));

        if (matched is null)
        {
            throw new FileNotFoundException("Backup file not found.");
        }

        return matched;
    }

    private async Task<long> GetEligibleRowCountAsync(DatabasePruneTarget target, DateTimeOffset cutoffUtc, CancellationToken cancellationToken)
    {
        return target switch
        {
            DatabasePruneTarget.SecurityAuthLogs => await _dbContext.SecurityAuthLogs.AsNoTracking().LongCountAsync(x => x.OccurredAtUtc < cutoffUtc, cancellationToken),
            DatabasePruneTarget.EventLogs => await _dbContext.EventLogs.AsNoTracking().LongCountAsync(x => x.OccurredAtUtc < cutoffUtc, cancellationToken),
            DatabasePruneTarget.CheckResults => await _dbContext.CheckResults.AsNoTracking().LongCountAsync(x => x.CheckedAtUtc < cutoffUtc, cancellationToken),
            DatabasePruneTarget.StateTransitions => await _dbContext.StateTransitions.AsNoTracking().LongCountAsync(x => x.TransitionAtUtc < cutoffUtc, cancellationToken),
            _ => throw new InvalidOperationException("Unsupported prune target.")
        };
    }

    private async Task<int> ExecutePruneDeleteAsync(DatabasePruneTarget target, DateTimeOffset cutoffUtc, CancellationToken cancellationToken)
    {
        return target switch
        {
            DatabasePruneTarget.SecurityAuthLogs => await _dbContext.SecurityAuthLogs.Where(x => x.OccurredAtUtc < cutoffUtc).ExecuteDeleteAsync(cancellationToken),
            DatabasePruneTarget.EventLogs => await _dbContext.EventLogs.Where(x => x.OccurredAtUtc < cutoffUtc).ExecuteDeleteAsync(cancellationToken),
            DatabasePruneTarget.CheckResults => await _dbContext.CheckResults.Where(x => x.CheckedAtUtc < cutoffUtc).ExecuteDeleteAsync(cancellationToken),
            DatabasePruneTarget.StateTransitions => await _dbContext.StateTransitions.Where(x => x.TransitionAtUtc < cutoffUtc).ExecuteDeleteAsync(cancellationToken),
            _ => throw new InvalidOperationException("Unsupported prune target.")
        };
    }

    private async Task<DatabaseBackupCreateResult> CreateBackupFailureAsync(
        string message,
        string? requestedBy,
        bool suppressEventLogWrites,
        CancellationToken cancellationToken)
    {
        await WriteDatabaseBackupEventAsync(
            new EventLogWriteRequest
            {
                Category = EventCategory.System,
                EventType = EventType.DatabaseBackupFailed,
                Severity = EventSeverity.Error,
                Message = message,
                DetailsJson = JsonSerializer.Serialize(new
                {
                    requestedBy
                })
            },
            suppressEventLogWrites,
            cancellationToken);

        return new DatabaseBackupCreateResult
        {
            Succeeded = false,
            Message = message
        };
    }

    private Task<DatabaseBackupRestoreResult> CreateRestoreFailureAsync(
        string message,
        FileInfo backupFileInfo,
        DatabaseBackupRestoreRequest request,
        DatabaseBackupMode backupMode,
        bool preRestoreBackupCreated,
        string? preRestoreBackupFileName,
        CancellationToken cancellationToken)
    {
        _ = cancellationToken;

        _logger.LogWarning(
            "Database backup restore failed before completion. FileId: {FileId}, FileName: {FileName}, BackupMode: {BackupMode}, PreRestoreBackupCreated: {PreRestoreBackupCreated}, PreRestoreBackupFileName: {PreRestoreBackupFileName}, RequestedBy: {RequestedBy}, Message: {Message}",
            request.FileId,
            backupFileInfo.Name,
            backupMode,
            preRestoreBackupCreated,
            preRestoreBackupFileName,
            request.RequestedBy,
            message);

        return Task.FromResult(new DatabaseBackupRestoreResult
        {
            Succeeded = false,
            Message = message,
            RestoredFileName = backupFileInfo.Name,
            RestoredFileCreatedAtUtc = new DateTimeOffset(backupFileInfo.LastWriteTimeUtc, TimeSpan.Zero),
            BackupMode = backupMode,
            PreRestoreBackupCreated = preRestoreBackupCreated,
            PreRestoreBackupFileName = preRestoreBackupFileName
        });
    }

    private static string BuildBackupHeader(DatabaseBackupMode mode, DateTimeOffset createdAtUtc)
    {
        var profile = mode == DatabaseBackupMode.Compact ? CompactBackupProfileName : "full_database";
        var modeToken = mode == DatabaseBackupMode.Compact ? "compact" : "full";
        return
            $"-- PingMonitor database backup metadata{Environment.NewLine}" +
            $"{BackupModeCommentPrefix}{modeToken}{Environment.NewLine}" +
            $"{BackupProfileCommentPrefix}{profile}{Environment.NewLine}" +
            $"-- pingmonitor_backup_created_at_utc:{createdAtUtc:O}{Environment.NewLine}";
    }

    private static DatabaseBackupMode NormalizeBackupMode(DatabaseBackupMode mode)
    {
        return mode == DatabaseBackupMode.Compact ? DatabaseBackupMode.Compact : DatabaseBackupMode.Full;
    }

    private async Task ResetCompactExcludedTablesAsync(CancellationToken cancellationToken)
    {
        foreach (var tableName in CompactExcludedTables)
        {
            var sql = $"DELETE FROM `{tableName}`;";
            await _dbContext.Database.ExecuteSqlRawAsync(sql, cancellationToken);
        }
    }

    private static BackupDescriptor ReadBackupDescriptorFromFile(string fullPath)
    {
        try
        {
            using var stream = new FileStream(fullPath, FileMode.Open, FileAccess.Read, FileShare.ReadWrite);
            var probeLength = (int)Math.Min(MetadataProbeBytes, stream.Length);
            if (probeLength <= 0)
            {
                return BackupDescriptor.FullByDefault;
            }

            var buffer = new byte[probeLength];
            _ = stream.Read(buffer, 0, probeLength);
            return ReadBackupDescriptorFromContent(buffer);
        }
        catch
        {
            return BackupDescriptor.FullByDefault;
        }
    }

    private static BackupDescriptor ReadBackupDescriptorFromContent(byte[] contentBytes)
    {
        try
        {
            var prefixLength = Math.Min(contentBytes.Length, MetadataProbeBytes);
            if (prefixLength <= 0)
            {
                return BackupDescriptor.FullByDefault;
            }

            var text = Encoding.UTF8.GetString(contentBytes, 0, prefixLength);
            foreach (var line in text.Split('\n', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries))
            {
                if (!line.StartsWith(BackupModeCommentPrefix, StringComparison.OrdinalIgnoreCase))
                {
                    continue;
                }

                var rawMode = line[BackupModeCommentPrefix.Length..].Trim();
                if (rawMode.Equals("compact", StringComparison.OrdinalIgnoreCase))
                {
                    return new BackupDescriptor(DatabaseBackupMode.Compact);
                }

                if (rawMode.Equals("full", StringComparison.OrdinalIgnoreCase))
                {
                    return new BackupDescriptor(DatabaseBackupMode.Full);
                }
            }
        }
        catch
        {
            // Fall back to full mode semantics for unknown or non-UTF8 files.
        }

        return BackupDescriptor.FullByDefault;
    }

    private string GetBackupDirectory(string configuredPath)
    {
        var contentRoot = _webHostEnvironment.ContentRootPath;
        return Path.IsPathRooted(configuredPath)
            ? configuredPath
            : Path.GetFullPath(Path.Combine(contentRoot, configuredPath));
    }

    private static string ComputeFileId(string fileName)
    {
        var bytes = SHA256.HashData(System.Text.Encoding.UTF8.GetBytes(fileName));
        return Convert.ToHexString(bytes).ToLowerInvariant();
    }

    private static string TrimDiagnostic(string? value)
    {
        if (string.IsNullOrWhiteSpace(value))
        {
            return "No stderr details were returned.";
        }

        var trimmed = value.Trim();
        return trimmed.Length <= 300 ? trimmed : trimmed[..300];
    }

    private static string QuoteProcessArgument(string argument)
    {
        if (argument.Contains(' ', StringComparison.Ordinal))
        {
            return $"\"{argument}\"";
        }

        return argument;
    }

    private static string SanitizeFileName(string? originalFileName)
    {
        var input = string.IsNullOrWhiteSpace(originalFileName)
            ? "uploaded.sql"
            : Path.GetFileName(originalFileName.Trim());
        var invalidChars = Path.GetInvalidFileNameChars();
        var builder = new StringBuilder(input.Length);
        foreach (var character in input)
        {
            builder.Append(invalidChars.Contains(character) ? '-' : character);
        }

        return builder.ToString().Trim();
    }

    private static string BuildStoredUploadFileName(string safeOriginalName, DateTimeOffset uploadedAtUtc)
    {
        var baseName = Path.GetFileNameWithoutExtension(safeOriginalName);
        var cleanedBaseName = Regex.Replace(baseName, "[^A-Za-z0-9._-]+", "-", RegexOptions.CultureInvariant).Trim('-');
        if (string.IsNullOrWhiteSpace(cleanedBaseName))
        {
            cleanedBaseName = "uploaded";
        }

        return $"uploaded-db-backup-{cleanedBaseName}-{uploadedAtUtc:yyyyMMdd-HHmmss}.sql";
    }

    private static void EnsurePathIsWithinDirectory(string baseDirectory, string targetPath)
    {
        var fullBase = Path.GetFullPath(baseDirectory).TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar);
        var fullTarget = Path.GetFullPath(targetPath);
        if (!fullTarget.StartsWith(fullBase + Path.DirectorySeparatorChar, StringComparison.Ordinal))
        {
            throw new InvalidOperationException("Resolved backup path is invalid.");
        }
    }

    private static string? ValidateSqlBackupContent(byte[] contentBytes)
    {
        string contentText;
        try
        {
            contentText = Encoding.UTF8.GetString(contentBytes);
        }
        catch
        {
            return "Database backup file could not be read as UTF-8 SQL text.";
        }

        if (string.IsNullOrWhiteSpace(contentText))
        {
            return "Database backup file is empty.";
        }

        var hasSqlIndicators =
            contentText.Contains("-- MySQL dump", StringComparison.OrdinalIgnoreCase) ||
            contentText.Contains("CREATE TABLE", StringComparison.OrdinalIgnoreCase) ||
            contentText.Contains("INSERT INTO", StringComparison.OrdinalIgnoreCase) ||
            contentText.Contains("DROP TABLE", StringComparison.OrdinalIgnoreCase);

        if (!hasSqlIndicators)
        {
            return "Database backup file is not a valid SQL backup format.";
        }

        return null;
    }

    private static string BuildMetadataSummary(string fullPath)
    {
        var descriptor = ReadBackupDescriptorFromFile(fullPath);
        if (descriptor.Mode == DatabaseBackupMode.Compact)
        {
            return "Compact profile: excludes results, metrics history, and logs";
        }

        return "Full profile: includes complete MySQL logical SQL dump";
    }

    private readonly record struct BackupDescriptor(DatabaseBackupMode Mode)
    {
        public static BackupDescriptor FullByDefault => new(DatabaseBackupMode.Full);
    }

    private static bool IsStartupGateRestoreRequest(DatabaseBackupRestoreRequest request)
    {
        return string.Equals(request.RequestedBy?.Trim(), "startup-gate", StringComparison.OrdinalIgnoreCase);
    }

    private async Task WriteDatabaseBackupEventAsync(
        EventLogWriteRequest eventRequest,
        bool suppressEventLogWrites,
        CancellationToken cancellationToken)
    {
        if (suppressEventLogWrites)
        {
            _logger.LogInformation(
                "Database backup event log suppressed because event-log tables may be unavailable. EventType: {EventType}, Message: {Message}",
                eventRequest.EventType,
                eventRequest.Message);
            return;
        }

        await _eventLogService.WriteAsync(eventRequest, cancellationToken);
    }
}

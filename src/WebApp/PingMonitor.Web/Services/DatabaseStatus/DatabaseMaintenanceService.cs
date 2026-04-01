using System.ComponentModel;
using System.Diagnostics;
using System.Security.Cryptography;
using System.Text.Json;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Options;
using MySqlConnector;
using PingMonitor.Web.Data;
using PingMonitor.Web.Models;
using PingMonitor.Web.Options;
using PingMonitor.Web.Services.EventLogs;

namespace PingMonitor.Web.Services.DatabaseStatus;

internal sealed class DatabaseMaintenanceService : IDatabaseMaintenanceService
{
    private readonly PingMonitorDbContext _dbContext;
    private readonly IEventLogService _eventLogService;
    private readonly IOptions<DatabaseMaintenanceOptions> _options;
    private readonly IWebHostEnvironment _webHostEnvironment;
    private readonly ILogger<DatabaseMaintenanceService> _logger;

    public DatabaseMaintenanceService(
        PingMonitorDbContext dbContext,
        IEventLogService eventLogService,
        IOptions<DatabaseMaintenanceOptions> options,
        IWebHostEnvironment webHostEnvironment,
        ILogger<DatabaseMaintenanceService> logger)
    {
        _dbContext = dbContext;
        _eventLogService = eventLogService;
        _options = options;
        _webHostEnvironment = webHostEnvironment;
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

        await _eventLogService.WriteAsync(new EventLogWriteRequest
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
        }, cancellationToken);

        var dbConnectionString = _dbContext.Database.GetConnectionString();
        if (string.IsNullOrWhiteSpace(dbConnectionString))
        {
            return await CreateBackupFailureAsync("Database connection string is unavailable.", request.RequestedBy, cancellationToken);
        }

        var builder = new MySqlConnectionStringBuilder(dbConnectionString);
        var backupTimestamp = DateTimeOffset.UtcNow;
        var safeDatabaseName = string.IsNullOrWhiteSpace(builder.Database) ? "database" : builder.Database.Trim();
        var fileName = $"db-backup-{safeDatabaseName}-{backupTimestamp:yyyyMMdd-HHmmss}.sql";
        var fullPath = Path.Combine(backupDirectory, fileName);
        var mysqldumpExecutablePath = ResolveMySqlDumpExecutablePath(options.MySqlDumpExecutablePath);
        if (mysqldumpExecutablePath is null)
        {
            return await CreateBackupFailureAsync(
                "mysqldump executable was not found. Configure DatabaseMaintenance:MySqlDumpExecutablePath with the full executable path.",
                request.RequestedBy,
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
        processInfo.Environment["MYSQL_PWD"] = builder.Password;

        try
        {
            using var process = Process.Start(processInfo);
            if (process is null)
            {
                return await CreateBackupFailureAsync("Unable to start mysqldump process.", request.RequestedBy, cancellationToken);
            }

            await using (var fileStream = new FileStream(fullPath, FileMode.Create, FileAccess.Write, FileShare.None))
            {
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
                return await CreateBackupFailureAsync(message, request.RequestedBy, cancellationToken);
            }

            var fileInfo = new FileInfo(fullPath);
            await _eventLogService.WriteAsync(new EventLogWriteRequest
            {
                Category = EventCategory.System,
                EventType = EventType.DatabaseBackupCompleted,
                Severity = EventSeverity.Info,
                Message = $"Database backup export completed. File: {fileName}.",
                DetailsJson = JsonSerializer.Serialize(new
                {
                    fileName,
                    fullPath,
                    fileSizeBytes = fileInfo.Length,
                    createdAtUtc = backupTimestamp,
                    requestedBy = request.RequestedBy
                })
            }, cancellationToken);

            return new DatabaseBackupCreateResult
            {
                Succeeded = true,
                Message = "Database backup export completed successfully.",
                FileName = fileName,
                FullPath = fullPath,
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
            return await CreateBackupFailureAsync($"Database backup export failed: {ex.Message}", request.RequestedBy, cancellationToken);
        }
    }

    private string? ResolveMySqlDumpExecutablePath(string configuredPath)
    {
        if (string.IsNullOrWhiteSpace(configuredPath))
        {
            return null;
        }

        var trimmedPath = configuredPath.Trim();
        if (Path.IsPathRooted(trimmedPath) || trimmedPath.Contains(Path.DirectorySeparatorChar) || trimmedPath.Contains(Path.AltDirectorySeparatorChar))
        {
            var rootedPath = Path.IsPathRooted(trimmedPath)
                ? trimmedPath
                : Path.GetFullPath(trimmedPath, _webHostEnvironment.ContentRootPath);
            return File.Exists(rootedPath) ? rootedPath : null;
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
            .Select(x => new DatabaseBackupFileSnapshot
            {
                FileName = x.Name,
                FileId = ComputeFileId(x.Name),
                CreatedAtUtc = new DateTimeOffset(x.LastWriteTimeUtc, TimeSpan.Zero),
                FileSizeBytes = x.Length,
                FullPath = x.FullName
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

    private async Task<DatabaseBackupCreateResult> CreateBackupFailureAsync(string message, string? requestedBy, CancellationToken cancellationToken)
    {
        await _eventLogService.WriteAsync(new EventLogWriteRequest
        {
            Category = EventCategory.System,
            EventType = EventType.DatabaseBackupFailed,
            Severity = EventSeverity.Error,
            Message = message,
            DetailsJson = JsonSerializer.Serialize(new
            {
                requestedBy
            })
        }, cancellationToken);

        return new DatabaseBackupCreateResult
        {
            Succeeded = false,
            Message = message
        };
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
}

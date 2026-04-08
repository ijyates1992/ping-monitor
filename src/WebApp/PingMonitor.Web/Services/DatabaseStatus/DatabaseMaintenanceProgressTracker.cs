using System.Text.Json;

namespace PingMonitor.Web.Services.DatabaseStatus;

internal sealed class DatabaseMaintenanceProgressTracker : IDatabaseMaintenanceProgressTracker
{
    private static readonly object Sync = new();

    private readonly IWebHostEnvironment _environment;
    private readonly ILogger<DatabaseMaintenanceProgressTracker> _logger;
    private readonly JsonSerializerOptions _jsonOptions = new(JsonSerializerDefaults.Web) { WriteIndented = true };

    private DatabaseMaintenanceOperationProgress? _currentOperation;

    public DatabaseMaintenanceProgressTracker(
        IWebHostEnvironment environment,
        ILogger<DatabaseMaintenanceProgressTracker> logger)
    {
        _environment = environment;
        _logger = logger;
    }

    public DatabaseMaintenanceOperationStartResult TryStartOperation(
        DatabaseMaintenanceOperationType operationType,
        string stage,
        string? fileName,
        string? detailsMessage)
    {
        lock (Sync)
        {
            if (_currentOperation is { IsRunning: true })
            {
                return new DatabaseMaintenanceOperationStartResult
                {
                    Started = false,
                    Message = "A DATABASE maintenance operation is already running.",
                    Operation = _currentOperation
                };
            }

            var now = DateTimeOffset.UtcNow;
            _currentOperation = new DatabaseMaintenanceOperationProgress
            {
                OperationId = Guid.NewGuid().ToString("N"),
                OperationType = operationType,
                StartedAtUtc = now,
                LastUpdatedAtUtc = now,
                IsRunning = true,
                Succeeded = false,
                Failed = false,
                Stage = stage,
                ApproximatePercentComplete = 0,
                FileName = fileName,
                BackupName = fileName,
                DetailsMessage = detailsMessage
            };

            PersistSnapshot(_currentOperation);

            return new DatabaseMaintenanceOperationStartResult
            {
                Started = true,
                Message = "DATABASE maintenance operation started.",
                Operation = _currentOperation
            };
        }
    }

    public DatabaseMaintenanceOperationProgress? GetCurrentOperation()
    {
        lock (Sync)
        {
            return _currentOperation;
        }
    }

    public DatabaseMaintenanceOperationProgress? GetOperation(string operationId)
    {
        if (string.IsNullOrWhiteSpace(operationId))
        {
            return null;
        }

        lock (Sync)
        {
            if (_currentOperation is not null && string.Equals(_currentOperation.OperationId, operationId, StringComparison.Ordinal))
            {
                return _currentOperation;
            }
        }

        var historyPath = GetHistoryFilePath(operationId);
        if (!File.Exists(historyPath))
        {
            return null;
        }

        try
        {
            var json = File.ReadAllText(historyPath);
            return JsonSerializer.Deserialize<DatabaseMaintenanceOperationProgress>(json, _jsonOptions);
        }
        catch (Exception exception)
        {
            _logger.LogWarning(exception, "Failed to read DATABASE maintenance progress snapshot {Path}.", historyPath);
            return null;
        }
    }

    public void UpdateProgress(
        string operationId,
        string stage,
        int approximatePercentComplete,
        long? bytesProcessed,
        long? totalBytes,
        string? statusMessage,
        string? detailsMessage)
    {
        lock (Sync)
        {
            if (_currentOperation is null ||
                !_currentOperation.IsRunning ||
                !string.Equals(_currentOperation.OperationId, operationId, StringComparison.Ordinal))
            {
                return;
            }

            var now = DateTimeOffset.UtcNow;
            var normalizedPercent = Math.Clamp(approximatePercentComplete, 0, 100);
            _currentOperation = _currentOperation with
            {
                LastUpdatedAtUtc = now,
                Stage = stage,
                ApproximatePercentComplete = normalizedPercent,
                BytesProcessed = bytesProcessed,
                TotalBytes = totalBytes,
                StatusMessage = statusMessage,
                DetailsMessage = detailsMessage
            };

            PersistSnapshot(_currentOperation);
        }
    }

    public void CompleteSuccess(string operationId, string stage, string? statusMessage, string? detailsMessage)
    {
        CompleteCore(operationId, stage, succeeded: true, failed: false, statusMessage, detailsMessage, errorMessage: null);
    }

    public void CompleteFailure(string operationId, string stage, string errorMessage, string? detailsMessage)
    {
        CompleteCore(operationId, stage, succeeded: false, failed: true, statusMessage: null, detailsMessage, errorMessage);
    }

    private void CompleteCore(
        string operationId,
        string stage,
        bool succeeded,
        bool failed,
        string? statusMessage,
        string? detailsMessage,
        string? errorMessage)
    {
        lock (Sync)
        {
            if (_currentOperation is null || !string.Equals(_currentOperation.OperationId, operationId, StringComparison.Ordinal))
            {
                return;
            }

            var now = DateTimeOffset.UtcNow;
            _currentOperation = _currentOperation with
            {
                LastUpdatedAtUtc = now,
                CompletedAtUtc = now,
                IsRunning = false,
                Succeeded = succeeded,
                Failed = failed,
                Stage = stage,
                ApproximatePercentComplete = succeeded ? 100 : Math.Max(_currentOperation.ApproximatePercentComplete, 1),
                StatusMessage = statusMessage,
                DetailsMessage = detailsMessage,
                ErrorMessage = errorMessage
            };

            PersistSnapshot(_currentOperation);
        }
    }

    private void PersistSnapshot(DatabaseMaintenanceOperationProgress operation)
    {
        try
        {
            var directory = GetProgressDirectoryPath();
            Directory.CreateDirectory(directory);
            var json = JsonSerializer.Serialize(operation, _jsonOptions);

            File.WriteAllText(GetCurrentFilePath(), json);
            File.WriteAllText(GetHistoryFilePath(operation.OperationId), json);
        }
        catch (Exception exception)
        {
            _logger.LogWarning(exception, "Failed to persist DATABASE maintenance progress snapshot.");
        }
    }

    private string GetProgressDirectoryPath()
    {
        return Path.Combine(_environment.ContentRootPath, "App_Data", "DbMaintenanceProgress");
    }

    private string GetCurrentFilePath()
    {
        return Path.Combine(GetProgressDirectoryPath(), "current-operation.json");
    }

    private string GetHistoryFilePath(string operationId)
    {
        return Path.Combine(GetProgressDirectoryPath(), $"{operationId}.json");
    }
}

namespace PingMonitor.Web.Services.DatabaseStatus;

public interface IDatabaseMaintenanceProgressTracker
{
    DatabaseMaintenanceOperationStartResult TryStartOperation(
        DatabaseMaintenanceOperationType operationType,
        string stage,
        string? fileName,
        string? detailsMessage);

    DatabaseMaintenanceOperationProgress? GetCurrentOperation();
    DatabaseMaintenanceOperationProgress? GetOperation(string operationId);

    void UpdateProgress(
        string operationId,
        string stage,
        int approximatePercentComplete,
        long? bytesProcessed,
        long? totalBytes,
        string? statusMessage,
        string? detailsMessage);

    void CompleteSuccess(string operationId, string stage, string? statusMessage, string? detailsMessage);
    void CompleteFailure(string operationId, string stage, string errorMessage, string? detailsMessage);
}

namespace PingMonitor.Web.Services.DatabaseStatus;

public interface IDatabaseStatusQueryService
{
    Task<DatabaseStatusSnapshot> GetSnapshotAsync(CancellationToken cancellationToken);
}

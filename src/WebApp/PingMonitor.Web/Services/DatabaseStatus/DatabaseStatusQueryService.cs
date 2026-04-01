using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Options;
using PingMonitor.Web.Data;
using PingMonitor.Web.Options;
using PingMonitor.Web.Services.BufferedResults;
using PingMonitor.Web.Services.StartupGate;
using System.Data;

namespace PingMonitor.Web.Services.DatabaseStatus;

internal sealed class DatabaseStatusQueryService : IDatabaseStatusQueryService
{
    private readonly PingMonitorDbContext _dbContext;
    private readonly IBufferedResultIngestionService _bufferedResultIngestionService;
    private readonly IOptions<ResultBufferOptions> _resultBufferOptions;
    private readonly IOptions<StartupGateOptions> _startupGateOptions;

    public DatabaseStatusQueryService(
        PingMonitorDbContext dbContext,
        IBufferedResultIngestionService bufferedResultIngestionService,
        IOptions<ResultBufferOptions> resultBufferOptions,
        IOptions<StartupGateOptions> startupGateOptions)
    {
        _dbContext = dbContext;
        _bufferedResultIngestionService = bufferedResultIngestionService;
        _resultBufferOptions = resultBufferOptions;
        _startupGateOptions = startupGateOptions;
    }

    public async Task<DatabaseStatusSnapshot> GetSnapshotAsync(CancellationToken cancellationToken)
    {
        var connection = _dbContext.Database.GetDbConnection();
        if (connection.State != ConnectionState.Open)
        {
            await connection.OpenAsync(cancellationToken);
        }

        var databaseName = connection.Database;
        var tables = await GetTableSnapshotsAsync(connection, databaseName, cancellationToken);
        var currentSchemaVersion = await _dbContext.AppSchemaInfos
            .OrderByDescending(x => x.AppSchemaInfoId)
            .Select(x => (int?)x.CurrentSchemaVersion)
            .FirstOrDefaultAsync(cancellationToken);

        var dataBytes = tables.Sum(x => x.DataBytes);
        var indexBytes = tables.Sum(x => x.IndexBytes);
        var bufferSnapshot = _bufferedResultIngestionService.GetSnapshot();
        var bufferOptions = _resultBufferOptions.Value;

        return new DatabaseStatusSnapshot
        {
            ProviderName = "MySQL",
            DatabaseName = databaseName,
            DataSource = connection.DataSource,
            ServerVersion = connection.ServerVersion,
            ConnectionHealthy = true,
            CurrentSchemaVersion = currentSchemaVersion,
            RequiredSchemaVersion = _startupGateOptions.Value.RequiredSchemaVersion,
            TableCount = tables.Count,
            TotalDataBytes = dataBytes,
            TotalIndexBytes = indexBytes,
            Tables = tables,
            ResultBuffer = new ResultBufferRuntimeSnapshot
            {
                BufferingEnabled = bufferOptions.ResultBufferEnabled,
                ConfiguredMaxBatchSize = bufferOptions.ResultBufferMaxBatchSize,
                ConfiguredFlushIntervalSeconds = bufferOptions.ResultBufferFlushIntervalSeconds,
                ConfiguredMaxQueueSize = bufferOptions.ResultBufferMaxQueueSize,
                CurrentQueueDepth = bufferSnapshot.QueueDepth,
                DroppedResultCount = bufferSnapshot.DroppedCount,
                TotalEnqueueCount = bufferSnapshot.TotalEnqueueCount,
                FlushCount = bufferSnapshot.FlushCount,
                FailedFlushCount = bufferSnapshot.FailedFlushCount,
                PersistedResultCount = bufferSnapshot.TotalPersistedCount,
                LastFlushAttemptedCount = bufferSnapshot.LastFlushAttemptedCount,
                LastFlushPersistedCount = bufferSnapshot.LastFlushPersistedCount,
                LastFlushCompletedAtUtc = bufferSnapshot.LastFlushCompletedAtUtc,
                LastFlushError = bufferSnapshot.LastFlushError
            }
        };
    }

    private static async Task<IReadOnlyList<DatabaseTableStatusSnapshot>> GetTableSnapshotsAsync(
        System.Data.Common.DbConnection connection,
        string databaseName,
        CancellationToken cancellationToken)
    {
        using var command = connection.CreateCommand();
        command.CommandText = @"SELECT table_name, table_rows, data_length, index_length
FROM information_schema.tables
WHERE table_schema = @schema
ORDER BY table_name;";

        var schemaParameter = command.CreateParameter();
        schemaParameter.ParameterName = "@schema";
        schemaParameter.Value = databaseName;
        command.Parameters.Add(schemaParameter);

        var results = new List<DatabaseTableStatusSnapshot>();
        await using var reader = await command.ExecuteReaderAsync(cancellationToken);
        while (await reader.ReadAsync(cancellationToken))
        {
            var tableName = reader.IsDBNull(0) ? string.Empty : reader.GetString(0);
            var tableRows = reader.IsDBNull(1) ? 0L : Convert.ToInt64(reader.GetValue(1));
            var dataLength = reader.IsDBNull(2) ? 0L : Convert.ToInt64(reader.GetValue(2));
            var indexLength = reader.IsDBNull(3) ? 0L : Convert.ToInt64(reader.GetValue(3));

            results.Add(new DatabaseTableStatusSnapshot
            {
                TableName = tableName,
                ApproximateRowCount = tableRows,
                DataBytes = dataLength,
                IndexBytes = indexLength
            });
        }

        return results;
    }
}

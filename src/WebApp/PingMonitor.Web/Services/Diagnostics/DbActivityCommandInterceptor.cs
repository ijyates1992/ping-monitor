using System.Data.Common;
using Microsoft.EntityFrameworkCore.Diagnostics;

namespace PingMonitor.Web.Services.Diagnostics;

internal sealed class DbActivityCommandInterceptor : DbCommandInterceptor
{
    private readonly IDbActivityTracker _tracker;
    private readonly IDbActivityScope _scope;

    public DbActivityCommandInterceptor(IDbActivityTracker tracker, IDbActivityScope scope)
    {
        _tracker = tracker;
        _scope = scope;
    }

    public override DbDataReader ReaderExecuted(DbCommand command, CommandExecutedEventData eventData, DbDataReader result)
    {
        Record(command, eventData.Duration, succeeded: true, rows: null);
        return result;
    }

    public override ValueTask<DbDataReader> ReaderExecutedAsync(
        DbCommand command,
        CommandExecutedEventData eventData,
        DbDataReader result,
        CancellationToken cancellationToken = default)
    {
        Record(command, eventData.Duration, succeeded: true, rows: null);
        return ValueTask.FromResult(result);
    }

    public override object? ScalarExecuted(DbCommand command, CommandExecutedEventData eventData, object? result)
    {
        Record(command, eventData.Duration, succeeded: true, rows: null);
        return result;
    }

    public override ValueTask<object?> ScalarExecutedAsync(
        DbCommand command,
        CommandExecutedEventData eventData,
        object? result,
        CancellationToken cancellationToken = default)
    {
        Record(command, eventData.Duration, succeeded: true, rows: null);
        return ValueTask.FromResult(result);
    }

    public override int NonQueryExecuted(DbCommand command, CommandExecutedEventData eventData, int result)
    {
        Record(command, eventData.Duration, succeeded: true, rows: result);
        return result;
    }

    public override ValueTask<int> NonQueryExecutedAsync(
        DbCommand command,
        CommandExecutedEventData eventData,
        int result,
        CancellationToken cancellationToken = default)
    {
        Record(command, eventData.Duration, succeeded: true, rows: result);
        return ValueTask.FromResult(result);
    }

    public override void CommandFailed(DbCommand command, CommandErrorEventData eventData)
    {
        Record(command, eventData.Duration, succeeded: false, rows: null);
    }

    public override Task CommandFailedAsync(DbCommand command, CommandErrorEventData eventData, CancellationToken cancellationToken = default)
    {
        Record(command, eventData.Duration, succeeded: false, rows: null);
        return Task.CompletedTask;
    }

    private void Record(DbCommand command, TimeSpan duration, bool succeeded, int? rows)
    {
        var commandType = GetCommandType(command.CommandText);
        _tracker.Record(new DbActivityRecord
        {
            Subsystem = _scope.CurrentSubsystem,
            ActivityKind = commandType,
            Duration = duration,
            Succeeded = succeeded,
            OccurredAtUtc = DateTimeOffset.UtcNow,
            Rows = rows,
            CommandType = commandType.ToString()
        });
    }

    private static DbActivityKind GetCommandType(string? commandText)
    {
        if (string.IsNullOrWhiteSpace(commandText))
        {
            return DbActivityKind.Other;
        }

        var normalized = commandText.TrimStart();
        if (normalized.StartsWith("SELECT", StringComparison.OrdinalIgnoreCase))
        {
            return DbActivityKind.Read;
        }

        if (normalized.StartsWith("INSERT", StringComparison.OrdinalIgnoreCase) ||
            normalized.StartsWith("UPDATE", StringComparison.OrdinalIgnoreCase) ||
            normalized.StartsWith("DELETE", StringComparison.OrdinalIgnoreCase) ||
            normalized.StartsWith("REPLACE", StringComparison.OrdinalIgnoreCase))
        {
            return DbActivityKind.Write;
        }

        return DbActivityKind.Other;
    }
}

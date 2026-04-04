using System.Threading;

namespace PingMonitor.Web.Services.Diagnostics;

internal sealed class DbActivityScope : IDbActivityScope
{
    private const string UnknownSubsystem = "Unknown";
    private static readonly AsyncLocal<string?> CurrentScope = new();

    public string CurrentSubsystem => string.IsNullOrWhiteSpace(CurrentScope.Value)
        ? UnknownSubsystem
        : CurrentScope.Value!;

    public IDisposable BeginScope(string subsystem)
    {
        var previous = CurrentScope.Value;
        CurrentScope.Value = string.IsNullOrWhiteSpace(subsystem)
            ? UnknownSubsystem
            : subsystem.Trim();

        return new ScopeHandle(previous);
    }

    private sealed class ScopeHandle : IDisposable
    {
        private readonly string? _previous;
        private bool _disposed;

        public ScopeHandle(string? previous)
        {
            _previous = previous;
        }

        public void Dispose()
        {
            if (_disposed)
            {
                return;
            }

            CurrentScope.Value = _previous;
            _disposed = true;
        }
    }
}

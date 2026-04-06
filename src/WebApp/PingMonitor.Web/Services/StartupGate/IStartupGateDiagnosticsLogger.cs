namespace PingMonitor.Web.Services.StartupGate;

public interface IStartupGateDiagnosticsLogger
{
    string LogFilePath { get; }

    void Write(string milestone, string message);

    void WriteException(string milestone, Exception exception, string? message = null);
}

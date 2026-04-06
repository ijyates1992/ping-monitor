using Microsoft.Extensions.Options;
using PingMonitor.Web.Options;
using System.Text;

namespace PingMonitor.Web.Services.StartupGate;

internal sealed class StartupGateDiagnosticsLogger : IStartupGateDiagnosticsLogger
{
    private const string FileName = "startup-gate-debug.log";
    private static readonly object Sync = new();

    private readonly ILogger<StartupGateDiagnosticsLogger> _logger;
    private readonly string _logFilePath;

    public StartupGateDiagnosticsLogger(
        IOptions<StartupGateOptions> options,
        IWebHostEnvironment environment,
        ILogger<StartupGateDiagnosticsLogger> logger)
    {
        _logger = logger;
        var startupGateOptions = options.Value;
        var storageDirectory = Path.IsPathRooted(startupGateOptions.StorageDirectory)
            ? startupGateOptions.StorageDirectory
            : Path.Combine(environment.ContentRootPath, startupGateOptions.StorageDirectory);
        _logFilePath = Path.Combine(storageDirectory, FileName);
    }

    public string LogFilePath => _logFilePath;

    public void Write(string milestone, string message)
    {
        AppendCore(milestone, message);
    }

    public void WriteException(string milestone, Exception exception, string? message = null)
    {
        var detail = new StringBuilder();
        if (!string.IsNullOrWhiteSpace(message))
        {
            detail.Append(message.Trim());
            detail.Append(" | ");
        }

        detail.Append("Exception: ");
        detail.Append(exception.ToString());
        AppendCore(milestone, detail.ToString());
    }

    private void AppendCore(string milestone, string message)
    {
        try
        {
            var directory = Path.GetDirectoryName(_logFilePath);
            if (!string.IsNullOrWhiteSpace(directory))
            {
                Directory.CreateDirectory(directory);
            }

            var line = $"{DateTimeOffset.UtcNow:O} | pid={Environment.ProcessId} | {milestone} | {message}{Environment.NewLine}";
            lock (Sync)
            {
                File.AppendAllText(_logFilePath, line, Encoding.UTF8);
            }
        }
        catch (Exception exception)
        {
            _logger.LogWarning(exception, "Startup gate diagnostics write failed for milestone {Milestone}.", milestone);
        }
    }
}

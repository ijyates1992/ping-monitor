using Microsoft.Extensions.Options;
using PingMonitor.Web.Options;

namespace PingMonitor.Web.Services.ApplicationUpdater;

public sealed class PowerShellPrerequisiteStatus
{
    public bool IsAvailable { get; init; }
    public string ConfiguredExecutablePath { get; init; } = string.Empty;
    public string? ResolvedExecutablePath { get; init; }
    public string Message { get; init; } = string.Empty;
}

public interface IPowerShellPrerequisiteDetector
{
    PowerShellPrerequisiteStatus GetStatus();
}

internal sealed class PowerShellPrerequisiteDetector : IPowerShellPrerequisiteDetector
{
    private readonly IExternalUpdaterProcessLauncher _launcher;
    private readonly ApplicationUpdaterOptions _options;

    public PowerShellPrerequisiteDetector(
        IExternalUpdaterProcessLauncher launcher,
        IOptions<ApplicationUpdaterOptions> options)
    {
        _launcher = launcher;
        _options = options.Value;
    }

    public PowerShellPrerequisiteStatus GetStatus()
    {
        var configuredExecutablePath = _options.PowerShellExecutablePath;
        if (_launcher.TryResolveExecutablePath(configuredExecutablePath, out var resolvedPath, out var errorMessage))
        {
            return new PowerShellPrerequisiteStatus
            {
                IsAvailable = true,
                ConfiguredExecutablePath = configuredExecutablePath,
                ResolvedExecutablePath = resolvedPath,
                Message = "PowerShell 7 prerequisite check passed. Updater apply operations are available."
            };
        }

        return new PowerShellPrerequisiteStatus
        {
            IsAvailable = false,
            ConfiguredExecutablePath = configuredExecutablePath,
            ResolvedExecutablePath = null,
            Message = "PowerShell 7 (`pwsh`) was not found in PATH. Update apply operations are unavailable until PowerShell 7 is installed and accessible to the application." +
                      (string.IsNullOrWhiteSpace(errorMessage) ? string.Empty : $" ({errorMessage})")
        };
    }
}

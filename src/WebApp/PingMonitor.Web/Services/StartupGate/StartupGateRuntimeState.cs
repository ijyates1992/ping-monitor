namespace PingMonitor.Web.Services.StartupGate;

public interface IStartupGateRuntimeState
{
    StartupMode CurrentMode { get; }
    bool IsOperationalMode { get; }
    void Update(StartupGateStatus status);
}

internal sealed class StartupGateRuntimeState : IStartupGateRuntimeState
{
    private readonly ILogger<StartupGateRuntimeState> _logger;
    private readonly object _sync = new();
    private StartupMode _currentMode = StartupMode.Gate;
    private StartupGateStage _failingStage = StartupGateStage.DatabaseConfiguration;

    public StartupGateRuntimeState(ILogger<StartupGateRuntimeState> logger)
    {
        _logger = logger;
    }

    public StartupMode CurrentMode
    {
        get
        {
            lock (_sync)
            {
                return _currentMode;
            }
        }
    }

    public bool IsOperationalMode
    {
        get
        {
            lock (_sync)
            {
                return _currentMode == StartupMode.Normal;
            }
        }
    }

    public void Update(StartupGateStatus status)
    {
        ArgumentNullException.ThrowIfNull(status);

        lock (_sync)
        {
            var modeChanged = _currentMode != status.Mode;
            var stageChanged = _failingStage != status.FailingStage;
            _currentMode = status.Mode;
            _failingStage = status.FailingStage;

            if (modeChanged)
            {
                if (status.Mode == StartupMode.Normal)
                {
                    _logger.LogInformation("Startup gate runtime state changed to normal mode. Background services may resume operational work.");
                }
                else
                {
                    _logger.LogInformation("Startup gate runtime state changed to gate mode at stage {Stage}. Background services requiring operational mode should remain paused.", status.FailingStage);
                }

                return;
            }

            if (status.Mode == StartupMode.Gate && stageChanged)
            {
                _logger.LogInformation("Startup gate runtime stage changed to {Stage}.", status.FailingStage);
            }
        }
    }
}

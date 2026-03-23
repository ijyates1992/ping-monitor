namespace PingMonitor.Web.Models;

public static class StateTransitionReasonCodes
{
    public const string FailureThresholdReached = "failure_threshold_reached";
    public const string RecoveryThresholdReached = "recovery_threshold_reached";
    public const string DependencyDown = "dependency_down";
    public const string DependencyCleared = "dependency_cleared";
    public const string AdministrativeReset = "administrative_reset";
    public const string AgentContextLost = "agent_context_lost";
}

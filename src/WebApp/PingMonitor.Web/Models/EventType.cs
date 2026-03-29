namespace PingMonitor.Web.Models;

public static class EventType
{
    public const string EndpointStateChanged = "endpoint_state_changed";
    public const string EndpointSuppressionApplied = "endpoint_suppression_applied";
    public const string EndpointSuppressionCleared = "endpoint_suppression_cleared";
    public const string AgentAuthenticated = "agent_authenticated";
    public const string AgentBecameOnline = "agent_became_online";
    public const string AgentBecameStale = "agent_became_stale";
    public const string AgentBecameOffline = "agent_became_offline";
    public const string AgentConfigFetched = "agent_config_fetched";
    public const string SecuritySettingsUpdated = "security_settings_updated";
    public const string SecurityManualIpBlockAdded = "security_manual_ip_block_added";
    public const string SecurityIpBlockRemoved = "security_ip_block_removed";
    public const string SecurityAutomaticTemporaryIpBlockAdded = "security_automatic_temporary_ip_block_added";
    public const string SecurityAutomaticPermanentIpBlockAdded = "security_automatic_permanent_ip_block_added";
    public const string SecurityAutomaticUserLockoutApplied = "security_automatic_user_lockout_applied";
    public const string SecurityManualIpBlockRemoveRejected = "security_manual_ip_block_remove_rejected";
    public const string SecurityManualUserUnlockApplied = "security_manual_user_unlock_applied";
    public const string SecurityManualUserUnlockRejected = "security_manual_user_unlock_rejected";
    public const string SecurityAuthLogManualPruneRequested = "security_auth_log_manual_prune_requested";
    public const string SecurityAuthLogManualPruneCompleted = "security_auth_log_manual_prune_completed";
    public const string SecurityAuthLogAutomaticPruneCompleted = "security_auth_log_automatic_prune_completed";
}

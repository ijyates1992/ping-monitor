namespace PingMonitor.Web.Models;

public static class EventType
{
    public const string EndpointStateChanged = "endpoint_state_changed";
    public const string EndpointSuppressionApplied = "endpoint_suppression_applied";
    public const string EndpointSuppressionCleared = "endpoint_suppression_cleared";
    public const string AgentAuthenticated = "agent_authenticated";
    public const string AgentHeartbeatReceived = "agent_heartbeat_received";
    public const string AgentOnline = "agent_online";
    public const string AgentConfigFetched = "agent_config_fetched";
}

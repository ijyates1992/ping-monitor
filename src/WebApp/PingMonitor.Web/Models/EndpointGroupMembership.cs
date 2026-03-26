namespace PingMonitor.Web.Models;

public sealed class EndpointGroupMembership
{
    public string EndpointGroupMembershipId { get; set; } = string.Empty;
    public string EndpointId { get; set; } = string.Empty;
    public string GroupId { get; set; } = string.Empty;
    public DateTimeOffset CreatedAtUtc { get; set; }
}

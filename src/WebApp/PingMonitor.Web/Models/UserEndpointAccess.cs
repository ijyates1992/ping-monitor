namespace PingMonitor.Web.Models;

public sealed class UserEndpointAccess
{
    public required string UserEndpointAccessId { get; set; }
    public required string UserId { get; set; }
    public required string EndpointId { get; set; }
    public required DateTimeOffset CreatedAtUtc { get; set; }
}

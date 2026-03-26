namespace PingMonitor.Web.Models;

public sealed class UserGroupAccess
{
    public required string UserGroupAccessId { get; set; }
    public required string UserId { get; set; }
    public required string GroupId { get; set; }
    public required DateTimeOffset CreatedAtUtc { get; set; }
}

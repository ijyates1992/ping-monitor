namespace PingMonitor.Web.Services.Identity;

public sealed class UserManagementListItem
{
    public required string UserId { get; init; }
    public required string UserName { get; init; }
    public required string Email { get; init; }
    public required string Role { get; init; }
    public required bool Enabled { get; init; }
}

public sealed class UserManagementEditModel
{
    public required string UserId { get; init; }
    public required string UserName { get; init; }
    public required string Email { get; init; }
    public required string Role { get; init; }
    public required bool Enabled { get; init; }
    public required IReadOnlyList<string> SelectedGroupIds { get; init; }
    public required IReadOnlyList<string> SelectedEndpointIds { get; init; }
}

public sealed class UserManagementSaveCommand
{
    public string? UserId { get; init; }
    public required string UserName { get; init; }
    public required string Email { get; init; }
    public string? Password { get; init; }
    public required string Role { get; init; }
    public required bool Enabled { get; init; }
    public required IReadOnlyList<string> GroupIds { get; init; }
    public required IReadOnlyList<string> EndpointIds { get; init; }
}

public sealed class UserManagementOption
{
    public required string Id { get; init; }
    public required string Name { get; init; }
}

public sealed class UserManagementResult
{
    public required bool Success { get; init; }
    public required bool Found { get; init; }
    public required IReadOnlyList<string> Errors { get; init; }
}

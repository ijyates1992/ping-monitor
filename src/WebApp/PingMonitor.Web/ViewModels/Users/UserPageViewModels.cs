using System.ComponentModel.DataAnnotations;

namespace PingMonitor.Web.ViewModels.Users;

public sealed class ManageUsersPageViewModel
{
    public string? StatusMessage { get; set; }
    public required IReadOnlyList<UserRowViewModel> Users { get; init; } = [];
}

public sealed class UserRowViewModel
{
    public required string UserId { get; init; }
    public required string UserName { get; init; }
    public required string Email { get; init; }
    public required string Role { get; init; }
    public required bool Enabled { get; init; }
}

public sealed class UserEditPageViewModel
{
    public string? UserId { get; set; }

    [Required]
    public string UserName { get; set; } = string.Empty;

    [Required]
    [EmailAddress]
    public string Email { get; set; } = string.Empty;

    [DataType(DataType.Password)]
    public string Password { get; set; } = string.Empty;

    [Required]
    public string Role { get; set; } = "User";

    public bool Enabled { get; set; } = true;

    public List<string> GroupIds { get; set; } = [];
    public List<string> EndpointIds { get; set; } = [];

    public IReadOnlyList<UserOptionViewModel> AvailableGroups { get; set; } = [];
    public IReadOnlyList<UserOptionViewModel> AvailableEndpoints { get; set; } = [];
}

public sealed class UserOptionViewModel
{
    public required string Id { get; init; }
    public required string Name { get; init; }
}

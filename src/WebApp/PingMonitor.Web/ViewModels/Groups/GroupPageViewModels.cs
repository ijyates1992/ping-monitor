using System.ComponentModel.DataAnnotations;

namespace PingMonitor.Web.ViewModels.Groups;

public sealed class ManageGroupsPageViewModel
{
    public IReadOnlyList<ManageGroupRowViewModel> Rows { get; init; } = [];
    public string? StatusMessage { get; set; }
}

public sealed class ManageGroupRowViewModel
{
    public string GroupId { get; init; } = string.Empty;
    public string Name { get; init; } = string.Empty;
    public string? Description { get; init; }
    public DateTimeOffset CreatedAtUtc { get; init; }
}

public sealed class GroupEditPageViewModel
{
    public string GroupId { get; set; } = string.Empty;

    [Required(ErrorMessage = "Group name is required.")]
    public string Name { get; set; } = string.Empty;

    public string Description { get; set; } = string.Empty;
}

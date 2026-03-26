using PingMonitor.Web.ViewModels.Endpoints;
using PingMonitor.Web.ViewModels.Groups;

namespace PingMonitor.Web.Services.Groups;

public interface IGroupManagementService
{
    Task<GroupCreateResult> CreateAsync(GroupUpsertCommand command, CancellationToken cancellationToken);
    Task<GroupUpdateResult> UpdateAsync(GroupUpsertCommand command, CancellationToken cancellationToken);
    Task<ManageGroupsPageViewModel> GetManagePageAsync(CancellationToken cancellationToken);
    Task<GroupEditPageViewModel?> GetEditPageAsync(string groupId, CancellationToken cancellationToken);
    Task<IReadOnlyList<EndpointGroupOptionViewModel>> GetGroupOptionsAsync(CancellationToken cancellationToken);
}

public sealed class GroupUpsertCommand
{
    public string? GroupId { get; init; }
    public string Name { get; init; } = string.Empty;
    public string? Description { get; init; }
}

public sealed class GroupCreateResult
{
    public bool Success { get; init; }
    public string? GroupId { get; init; }
    public IReadOnlyList<string> ValidationErrors { get; init; } = [];
}

public sealed class GroupUpdateResult
{
    public bool Success { get; init; }
    public bool Found { get; init; }
    public IReadOnlyList<string> ValidationErrors { get; init; } = [];
}

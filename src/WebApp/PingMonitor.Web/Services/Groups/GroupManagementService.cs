using Microsoft.EntityFrameworkCore;
using PingMonitor.Web.Data;
using PingMonitor.Web.Models;
using PingMonitor.Web.ViewModels.Endpoints;
using PingMonitor.Web.ViewModels.Groups;

namespace PingMonitor.Web.Services.Groups;

internal sealed class GroupManagementService : IGroupManagementService
{
    private readonly PingMonitorDbContext _dbContext;

    public GroupManagementService(PingMonitorDbContext dbContext)
    {
        _dbContext = dbContext;
    }

    public async Task<GroupCreateResult> CreateAsync(GroupUpsertCommand command, CancellationToken cancellationToken)
    {
        var errors = await ValidateAsync(command, isCreate: true, cancellationToken);
        if (errors.Count > 0)
        {
            return new GroupCreateResult { Success = false, ValidationErrors = errors };
        }

        var group = new Group
        {
            GroupId = Guid.NewGuid().ToString(),
            Name = command.Name.Trim(),
            Description = string.IsNullOrWhiteSpace(command.Description) ? null : command.Description.Trim(),
            CreatedAtUtc = DateTimeOffset.UtcNow
        };

        _dbContext.Groups.Add(group);
        await _dbContext.SaveChangesAsync(cancellationToken);

        return new GroupCreateResult { Success = true, GroupId = group.GroupId };
    }

    public async Task<GroupUpdateResult> UpdateAsync(GroupUpsertCommand command, CancellationToken cancellationToken)
    {
        var groupId = command.GroupId?.Trim();
        if (string.IsNullOrWhiteSpace(groupId))
        {
            return new GroupUpdateResult { Success = false, Found = false, ValidationErrors = ["Group ID is required."] };
        }

        var group = await _dbContext.Groups.SingleOrDefaultAsync(x => x.GroupId == groupId, cancellationToken);
        if (group is null)
        {
            return new GroupUpdateResult { Success = false, Found = false };
        }

        var errors = await ValidateAsync(command, isCreate: false, cancellationToken);
        if (errors.Count > 0)
        {
            return new GroupUpdateResult { Success = false, Found = true, ValidationErrors = errors };
        }

        group.Name = command.Name.Trim();
        group.Description = string.IsNullOrWhiteSpace(command.Description) ? null : command.Description.Trim();
        await _dbContext.SaveChangesAsync(cancellationToken);

        return new GroupUpdateResult { Success = true, Found = true };
    }

    public async Task<ManageGroupsPageViewModel> GetManagePageAsync(CancellationToken cancellationToken)
    {
        var groups = await _dbContext.Groups.AsNoTracking()
            .OrderBy(x => x.Name)
            .Select(x => new ManageGroupRowViewModel
            {
                GroupId = x.GroupId,
                Name = x.Name,
                Description = x.Description,
                CreatedAtUtc = x.CreatedAtUtc
            })
            .ToArrayAsync(cancellationToken);

        return new ManageGroupsPageViewModel { Rows = groups };
    }

    public async Task<GroupEditPageViewModel?> GetEditPageAsync(string groupId, CancellationToken cancellationToken)
    {
        var normalizedGroupId = groupId.Trim();
        if (string.IsNullOrWhiteSpace(normalizedGroupId))
        {
            return null;
        }

        return await _dbContext.Groups.AsNoTracking()
            .Where(x => x.GroupId == normalizedGroupId)
            .Select(x => new GroupEditPageViewModel
            {
                GroupId = x.GroupId,
                Name = x.Name,
                Description = x.Description ?? string.Empty
            })
            .SingleOrDefaultAsync(cancellationToken);
    }

    public async Task<IReadOnlyList<EndpointGroupOptionViewModel>> GetGroupOptionsAsync(CancellationToken cancellationToken)
    {
        return await _dbContext.Groups.AsNoTracking()
            .OrderBy(x => x.Name)
            .Select(x => new EndpointGroupOptionViewModel
            {
                GroupId = x.GroupId,
                Name = x.Name
            })
            .ToArrayAsync(cancellationToken);
    }

    private async Task<IReadOnlyList<string>> ValidateAsync(GroupUpsertCommand command, bool isCreate, CancellationToken cancellationToken)
    {
        var errors = new List<string>();
        var name = command.Name.Trim();

        if (string.IsNullOrWhiteSpace(name))
        {
            errors.Add("Group name is required.");
            return errors;
        }

        var groupId = command.GroupId?.Trim();
        var duplicate = await _dbContext.Groups.AsNoTracking()
            .AnyAsync(x => x.Name == name && (isCreate || x.GroupId != groupId), cancellationToken);

        if (duplicate)
        {
            errors.Add("A group with this name already exists.");
        }

        return errors;
    }
}

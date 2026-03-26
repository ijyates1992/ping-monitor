namespace PingMonitor.Web.Services.Identity;

public interface IUserManagementService
{
    Task<IReadOnlyList<UserManagementListItem>> ListUsersAsync(CancellationToken cancellationToken);
    Task<UserManagementEditModel?> GetUserAsync(string userId, CancellationToken cancellationToken);
    Task<IReadOnlyList<UserManagementOption>> GetGroupOptionsAsync(CancellationToken cancellationToken);
    Task<IReadOnlyList<UserManagementOption>> GetEndpointOptionsAsync(CancellationToken cancellationToken);
    Task<UserManagementResult> CreateAsync(UserManagementSaveCommand command, CancellationToken cancellationToken);
    Task<UserManagementResult> UpdateAsync(UserManagementSaveCommand command, CancellationToken cancellationToken);
}

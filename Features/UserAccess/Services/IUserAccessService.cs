using NetworldParkingLot.Api.Features.UserAccess.Dtos;

namespace NetworldParkingLot.Api.Features.UserAccess.Services;

public interface IUserAccessService
{
    Task<LoginResultDto> LoginAsync(LoginRequestDto request, CancellationToken cancellationToken = default);
    Task<CurrentParkingUserDto> GetCurrentUserAsync(int userId, CancellationToken cancellationToken = default);
    Task<bool> HasPermissionAsync(int userId, string moduleKey, string actionKey, CancellationToken cancellationToken = default);

    Task<PagedResultDto<SystemUserListItemDto>> GetUsersAsync(string? tab, string? search, int pageNumber, int pageSize, CancellationToken cancellationToken = default);
    Task<SystemUserCountsDto> GetUserCountsAsync(CancellationToken cancellationToken = default);
    Task<SystemUserListItemDto> GetUserAsync(int userId, CancellationToken cancellationToken = default);
    Task<SystemUserListItemDto> CreateUserAsync(CreateSystemUserDto request, int operatorId, CancellationToken cancellationToken = default);
    Task<SystemUserListItemDto> UpdateUserAsync(int userId, UpdateSystemUserDto request, int operatorId, CancellationToken cancellationToken = default);
    Task SetUserStatusAsync(int userId, string status, int operatorId, CancellationToken cancellationToken = default);
    Task DeleteUserAsync(int userId, int operatorId, CancellationToken cancellationToken = default);

    Task<IReadOnlyList<AccessRoleDto>> GetRolesAsync(CancellationToken cancellationToken = default);
    Task<AccessRoleDto> CreateRoleAsync(CreateAccessRoleDto request, CancellationToken cancellationToken = default);
    Task<AccessRoleDto> UpdateRoleAsync(int roleId, UpdateAccessRoleDto request, CancellationToken cancellationToken = default);
    Task<IReadOnlyList<AccessPermissionDto>> GetRolePermissionsAsync(int roleId, CancellationToken cancellationToken = default);
    Task SaveRolePermissionsAsync(int roleId, SaveRolePermissionsDto request, CancellationToken cancellationToken = default);
    Task<PagedResultDto<AccessUserLookupDto>> SearchUsersForAccessAsync(string? search, int pageNumber, int pageSize, CancellationToken cancellationToken = default);
    Task<UserRoleAssignmentDto> GetUserRolesAsync(int userId, CancellationToken cancellationToken = default);
    Task SaveUserRolesAsync(int userId, SaveUserRolesDto request, CancellationToken cancellationToken = default);
    Task<IReadOnlyList<AccessPermissionDto>> GetUserPermissionsAsync(int userId, CancellationToken cancellationToken = default);
    Task SaveUserPermissionsAsync(int userId, SaveUserPermissionsDto request, CancellationToken cancellationToken = default);
    Task<IReadOnlyList<UserPermissionOverrideDto>> GetUserPermissionOverridesAsync(int userId, CancellationToken cancellationToken = default);
    Task SaveUserPermissionOverridesAsync(int userId, SaveUserPermissionOverridesDto request, CancellationToken cancellationToken = default);
}

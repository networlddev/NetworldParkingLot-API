namespace NetworldParkingLot.Api.Features.UserAccess.Dtos;

public sealed class LoginRequestDto
{
    public string Username { get; set; } = string.Empty;
    public string Password { get; set; } = string.Empty;
}

public sealed class LoginResultDto
{
    public string Token { get; set; } = string.Empty;
    public DateTime ExpiryTime { get; set; }
    public CurrentParkingUserDto User { get; set; } = new();
}

public sealed class CurrentParkingUserDto
{
    public int UserId { get; set; }
    public string Username { get; set; } = string.Empty;
    public string FullName { get; set; } = string.Empty;
    public string Status { get; set; } = string.Empty;
    public List<string> Roles { get; set; } = [];
    public List<string> Permissions { get; set; } = [];

    public bool HasPermission(string moduleKey, string actionKey) =>
        Roles.Contains("super_admin", StringComparer.OrdinalIgnoreCase) ||
        Permissions.Contains($"{moduleKey}.{actionKey}", StringComparer.OrdinalIgnoreCase);
}

public sealed class PagedResultDto<T>
{
    public List<T> Items { get; set; } = [];
    public int PageNumber { get; set; }
    public int PageSize { get; set; }
    public int TotalRecords { get; set; }
    public int TotalPages => PageSize <= 0 ? 0 : (int)Math.Ceiling(TotalRecords / (double)PageSize);
}

public sealed class SystemUserListItemDto
{
    public int UserId { get; set; }
    public string Username { get; set; } = string.Empty;
    public string FullName { get; set; } = string.Empty;
    public string? Email { get; set; }
    public string? Phone { get; set; }
    public string Status { get; set; } = string.Empty;
    public bool Active { get; set; }
    public DateTime CreatedDate { get; set; }
    public string RolesText { get; set; } = string.Empty;
    public List<string> RoleKeys { get; set; } = [];
}

public sealed class SystemUserCountsDto
{
    public int ActiveCount { get; set; }
    public int SuspendedCount { get; set; }
    public int DeletedCount { get; set; }
    public int AllCount { get; set; }
}

public sealed class CreateSystemUserDto
{
    public string Username { get; set; } = string.Empty;
    public string FullName { get; set; } = string.Empty;
    public string? Email { get; set; }
    public string? Phone { get; set; }
    public string Password { get; set; } = string.Empty;
    public string? RoleKey { get; set; }
}

public sealed class UpdateSystemUserDto
{
    public string FullName { get; set; } = string.Empty;
    public string? Email { get; set; }
    public string? Phone { get; set; }
    public string? Password { get; set; }
    public string Status { get; set; } = "Active";
    public string? RoleKey { get; set; }
}

public sealed class SetUserStatusDto
{
    public string Status { get; set; } = "Active";
}

public sealed class AccessRoleDto
{
    public int RoleId { get; set; }
    public string RoleKey { get; set; } = string.Empty;
    public string RoleName { get; set; } = string.Empty;
    public bool Active { get; set; }
    public bool IsDefault { get; set; }
}

public sealed class CreateAccessRoleDto
{
    public string RoleKey { get; set; } = string.Empty;
    public string RoleName { get; set; } = string.Empty;
    public bool IsDefault { get; set; }
}

public sealed class UpdateAccessRoleDto
{
    public string RoleName { get; set; } = string.Empty;
    public bool Active { get; set; } = true;
    public bool IsDefault { get; set; }
}

public sealed class AccessPermissionDto
{
    public int ModuleId { get; set; }
    public string ModuleKey { get; set; } = string.Empty;
    public string ModuleName { get; set; } = string.Empty;
    public int ActionId { get; set; }
    public string ActionKey { get; set; } = string.Empty;
    public string ActionName { get; set; } = string.Empty;
    public bool IsAllowed { get; set; }
}

public sealed class SavePermissionDto
{
    public int ModuleId { get; set; }
    public int ActionId { get; set; }
    public string ActionKey { get; set; } = string.Empty;
    public bool IsAllowed { get; set; }
}

public sealed class SaveRolePermissionsDto
{
    public List<SavePermissionDto> Permissions { get; set; } = [];
}

public sealed class SaveUserRolesDto
{
    public List<int> RoleIds { get; set; } = [];
}

public sealed class SaveUserPermissionsDto
{
    public List<SavePermissionDto> Permissions { get; set; } = [];
}

public sealed class UserPermissionOverrideDto
{
    public int ModuleId { get; set; }
    public string ModuleKey { get; set; } = string.Empty;
    public string ModuleName { get; set; } = string.Empty;
    public int ActionId { get; set; }
    public string ActionKey { get; set; } = string.Empty;
    public string ActionName { get; set; } = string.Empty;
    public bool RoleAllowed { get; set; }
    public bool EffectiveAllowed { get; set; }
    public string OverrideState { get; set; } = "Default";
}

public sealed class SaveUserPermissionOverrideDto
{
    public int ModuleId { get; set; }
    public int ActionId { get; set; }
    public string OverrideState { get; set; } = "Default";
}

public sealed class SaveUserPermissionOverridesDto
{
    public List<SaveUserPermissionOverrideDto> Permissions { get; set; } = [];
}

public sealed class AccessUserLookupDto
{
    public int UserId { get; set; }
    public string Username { get; set; } = string.Empty;
    public string FullName { get; set; } = string.Empty;
    public string Status { get; set; } = string.Empty;
    public string RolesText { get; set; } = string.Empty;
}

public sealed class UserRoleAssignmentDto
{
    public int UserId { get; set; }
    public List<int> RoleIds { get; set; } = [];
    public List<string> RoleKeys { get; set; } = [];
}

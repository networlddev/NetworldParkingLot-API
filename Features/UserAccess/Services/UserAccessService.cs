using Microsoft.EntityFrameworkCore;
using NetworldParkingLot.Api.Common.Security;
using NetworldParkingLot.Api.Data;
using NetworldParkingLot.Api.Domain.Entities;
using NetworldParkingLot.Api.Features.UserAccess.Dtos;

namespace NetworldParkingLot.Api.Features.UserAccess.Services;

public sealed class UserAccessService(
    NetworldParkingDbContext db,
    JwtTokenService tokenService) : IUserAccessService
{
    public async Task<LoginResultDto> LoginAsync(LoginRequestDto request, CancellationToken cancellationToken = default)
    {
        var username = (request.Username ?? string.Empty).Trim();
        if (string.IsNullOrWhiteSpace(username)) throw new InvalidOperationException("Username is required.");
        if (string.IsNullOrWhiteSpace(request.Password)) throw new InvalidOperationException("Password is required.");

        var user = await db.AppUsers.FirstOrDefaultAsync(x => x.Username == username && x.Active, cancellationToken);
        if (user == null || !PasswordHashHelper.VerifyPassword(request.Password, user.PasswordHash))
            throw new UnauthorizedAccessException("Invalid username or password.");
        if (!string.Equals(user.Status, "Active", StringComparison.OrdinalIgnoreCase))
            throw new UnauthorizedAccessException("This user is suspended.");

        var currentUser = await GetCurrentUserAsync(user.UserId, cancellationToken);
        var token = tokenService.CreateToken(currentUser);

        return new LoginResultDto
        {
            Token = token.Token,
            ExpiryTime = token.ExpiryTime,
            User = currentUser
        };
    }

    public async Task<CurrentParkingUserDto> GetCurrentUserAsync(int userId, CancellationToken cancellationToken = default)
    {
        var user = await db.AppUsers.AsNoTracking().FirstOrDefaultAsync(x => x.UserId == userId && x.Active, cancellationToken);
        if (user == null) throw new InvalidOperationException("User not found.");

        var roles = await db.AppUserRoles.AsNoTracking()
            .Where(x => x.UserId == userId && x.Role != null && x.Role.Active)
            .Select(x => x.Role!.RoleKey)
            .Distinct()
            .ToListAsync(cancellationToken);

        if (roles.Count == 0 && !string.IsNullOrWhiteSpace(user.Role))
            roles.Add(ToRoleKey(user.Role));

        var permissions = await GetFinalPermissionKeysAsync(userId, roles, cancellationToken);

        return new CurrentParkingUserDto
        {
            UserId = user.UserId,
            Username = user.Username,
            FullName = user.FullName,
            Status = user.Status,
            Roles = roles.Distinct(StringComparer.OrdinalIgnoreCase).ToList(),
            Permissions = permissions
        };
    }

    public async Task<bool> HasPermissionAsync(int userId, string moduleKey, string actionKey, CancellationToken cancellationToken = default)
    {
        var user = await GetCurrentUserAsync(userId, cancellationToken);
        return user.HasPermission(moduleKey, actionKey);
    }

    public async Task<PagedResultDto<SystemUserListItemDto>> GetUsersAsync(string? tab, string? search, int pageNumber, int pageSize, CancellationToken cancellationToken = default)
    {
        pageNumber = Math.Max(pageNumber, 1);
        pageSize = pageSize <= 0 ? 20 : Math.Min(pageSize, 100);
        var normalizedTab = (tab ?? "active").Trim().ToLowerInvariant();
        var q = (search ?? string.Empty).Trim();

        var query = db.AppUsers.AsNoTracking().AsQueryable();
        query = normalizedTab switch
        {
            "suspended" => query.Where(x => x.Active && x.Status == "Suspended"),
            "deleted" => query.Where(x => !x.Active),
            "all" => query,
            _ => query.Where(x => x.Active && x.Status == "Active")
        };

        if (!string.IsNullOrWhiteSpace(q))
            query = query.Where(x => x.Username.Contains(q) || x.FullName.Contains(q) || (x.Email != null && x.Email.Contains(q)) || (x.Phone != null && x.Phone.Contains(q)));

        var result = new PagedResultDto<SystemUserListItemDto>
        {
            PageNumber = pageNumber,
            PageSize = pageSize,
            TotalRecords = await query.CountAsync(cancellationToken)
        };

        var users = await query.OrderByDescending(x => x.CreatedDate)
            .Skip((pageNumber - 1) * pageSize)
            .Take(pageSize)
            .ToListAsync(cancellationToken);

        foreach (var user in users)
            result.Items.Add(await BuildUserDtoAsync(user, cancellationToken));

        return result;
    }

    public async Task<SystemUserCountsDto> GetUserCountsAsync(CancellationToken cancellationToken = default)
    {
        return new SystemUserCountsDto
        {
            ActiveCount = await db.AppUsers.CountAsync(x => x.Active && x.Status == "Active", cancellationToken),
            SuspendedCount = await db.AppUsers.CountAsync(x => x.Active && x.Status == "Suspended", cancellationToken),
            DeletedCount = await db.AppUsers.CountAsync(x => !x.Active, cancellationToken),
            AllCount = await db.AppUsers.CountAsync(cancellationToken)
        };
    }

    public async Task<SystemUserListItemDto> GetUserAsync(int userId, CancellationToken cancellationToken = default)
    {
        var user = await db.AppUsers.AsNoTracking().FirstOrDefaultAsync(x => x.UserId == userId, cancellationToken)
            ?? throw new InvalidOperationException("User not found.");
        return await BuildUserDtoAsync(user, cancellationToken);
    }

    public async Task<SystemUserListItemDto> CreateUserAsync(CreateSystemUserDto request, int operatorId, CancellationToken cancellationToken = default)
    {
        var username = (request.Username ?? string.Empty).Trim();
        if (string.IsNullOrWhiteSpace(username)) throw new InvalidOperationException("Username is required.");
        if (string.IsNullOrWhiteSpace(request.FullName)) throw new InvalidOperationException("Full name is required.");
        if (string.IsNullOrWhiteSpace(request.Password) || request.Password.Length < 6) throw new InvalidOperationException("Password must be at least 6 characters.");

        if (await db.AppUsers.AnyAsync(x => x.Username == username, cancellationToken))
            throw new InvalidOperationException("Username already exists.");

        var role = await ResolveRoleAsync(request.RoleKey, cancellationToken);
        var user = new AppUser
        {
            Username = username,
            FullName = request.FullName.Trim(),
            Email = Clean(request.Email),
            Phone = Clean(request.Phone),
            PasswordHash = PasswordHashHelper.HashPassword(request.Password),
            Role = role.RoleName,
            Status = "Active",
            Active = true,
            CreatedDate = DateTime.Now,
            ModifiedBy = operatorId
        };

        await db.AppUsers.AddAsync(user, cancellationToken);
        await db.SaveChangesAsync(cancellationToken);
        await db.AppUserRoles.AddAsync(new AppUserRole { UserId = user.UserId, RoleId = role.RoleId }, cancellationToken);
        await db.SaveChangesAsync(cancellationToken);
        return await GetUserAsync(user.UserId, cancellationToken);
    }

    public async Task<SystemUserListItemDto> UpdateUserAsync(int userId, UpdateSystemUserDto request, int operatorId, CancellationToken cancellationToken = default)
    {
        var user = await db.AppUsers.FirstOrDefaultAsync(x => x.UserId == userId, cancellationToken)
            ?? throw new InvalidOperationException("User not found.");
        if (string.IsNullOrWhiteSpace(request.FullName)) throw new InvalidOperationException("Full name is required.");

        if (IsBreakGlassAccount(user))
        {
            if (!string.IsNullOrWhiteSpace(request.Password) && operatorId != user.UserId)
                throw new InvalidOperationException("Only the Super Admin break-glass account can change its own password.");
            if (!string.Equals(NormalizeStatus(request.Status), "Active", StringComparison.OrdinalIgnoreCase))
                throw new InvalidOperationException("The Super Admin break-glass account must remain active.");
            if (!string.IsNullOrWhiteSpace(request.RoleKey) && !IsBreakGlassRoleKey(request.RoleKey))
                throw new InvalidOperationException("The Super Admin break-glass account role cannot be changed.");
        }

        user.FullName = request.FullName.Trim();
        user.Email = Clean(request.Email);
        user.Phone = Clean(request.Phone);
        user.Status = NormalizeStatus(request.Status);
        user.ModifiedBy = operatorId;
        user.ModifiedDate = DateTime.Now;
        if (!string.IsNullOrWhiteSpace(request.Password))
        {
            if (request.Password.Length < 6) throw new InvalidOperationException("Password must be at least 6 characters.");
            user.PasswordHash = PasswordHashHelper.HashPassword(request.Password);
        }

        if (!string.IsNullOrWhiteSpace(request.RoleKey))
        {
            var role = await ResolveRoleAsync(request.RoleKey, cancellationToken);
            user.Role = role.RoleName;
            db.AppUserRoles.RemoveRange(db.AppUserRoles.Where(x => x.UserId == userId));
            await db.AppUserRoles.AddAsync(new AppUserRole { UserId = userId, RoleId = role.RoleId }, cancellationToken);
        }

        await db.SaveChangesAsync(cancellationToken);
        return await GetUserAsync(userId, cancellationToken);
    }

    public async Task SetUserStatusAsync(int userId, string status, int operatorId, CancellationToken cancellationToken = default)
    {
        var user = await db.AppUsers.FirstOrDefaultAsync(x => x.UserId == userId, cancellationToken)
            ?? throw new InvalidOperationException("User not found.");
        if (IsBreakGlassAccount(user))
            throw new InvalidOperationException("The Super Admin break-glass account status cannot be changed.");
        user.Status = NormalizeStatus(status);
        user.ModifiedBy = operatorId;
        user.ModifiedDate = DateTime.Now;
        await db.SaveChangesAsync(cancellationToken);
    }

    public async Task DeleteUserAsync(int userId, int operatorId, CancellationToken cancellationToken = default)
    {
        if (userId == operatorId)
            throw new InvalidOperationException("You cannot delete your own account.");

        var user = await db.AppUsers.FirstOrDefaultAsync(x => x.UserId == userId, cancellationToken)
            ?? throw new InvalidOperationException("User not found.");

        if (await IsProtectedAdminUserAsync(user, cancellationToken))
            throw new InvalidOperationException("System Admin and Super Admin accounts cannot be deleted.");

        user.Active = false;
        user.Status = "Deleted";
        user.ModifiedBy = operatorId;
        user.ModifiedDate = DateTime.Now;
        await db.SaveChangesAsync(cancellationToken);
    }

    public async Task<IReadOnlyList<AccessRoleDto>> GetRolesAsync(CancellationToken cancellationToken = default)
    {
        return await db.AppRoles.AsNoTracking()
            .OrderBy(x => x.RoleName)
            .Select(x => new AccessRoleDto { RoleId = x.RoleId, RoleKey = x.RoleKey, RoleName = x.RoleName, Active = x.Active, IsDefault = x.IsDefault })
            .ToListAsync(cancellationToken);
    }

    public async Task<AccessRoleDto> CreateRoleAsync(CreateAccessRoleDto request, CancellationToken cancellationToken = default)
    {
        var key = ToRoleKey(request.RoleKey);
        if (string.IsNullOrWhiteSpace(key)) throw new InvalidOperationException("Role key is required.");
        if (string.IsNullOrWhiteSpace(request.RoleName)) throw new InvalidOperationException("Role name is required.");
        if (await db.AppRoles.AnyAsync(x => x.RoleKey == key, cancellationToken)) throw new InvalidOperationException("Role key already exists.");
        if (request.IsDefault) await ClearDefaultRolesAsync(cancellationToken);

        var role = new AppRole { RoleKey = key, RoleName = request.RoleName.Trim(), Active = true, IsDefault = request.IsDefault };
        await db.AppRoles.AddAsync(role, cancellationToken);
        await db.SaveChangesAsync(cancellationToken);
        return new AccessRoleDto { RoleId = role.RoleId, RoleKey = role.RoleKey, RoleName = role.RoleName, Active = role.Active, IsDefault = role.IsDefault };
    }

    public async Task<AccessRoleDto> UpdateRoleAsync(int roleId, UpdateAccessRoleDto request, CancellationToken cancellationToken = default)
    {
        var role = await db.AppRoles.FirstOrDefaultAsync(x => x.RoleId == roleId, cancellationToken)
            ?? throw new InvalidOperationException("Role not found.");
        if (IsBreakGlassRoleKey(role.RoleKey))
            throw new InvalidOperationException("The Super Admin role cannot be changed.");
        if (string.IsNullOrWhiteSpace(request.RoleName)) throw new InvalidOperationException("Role name is required.");
        if (request.IsDefault) await ClearDefaultRolesAsync(cancellationToken);
        role.RoleName = request.RoleName.Trim();
        role.Active = request.Active;
        role.IsDefault = request.IsDefault;
        await db.SaveChangesAsync(cancellationToken);
        return new AccessRoleDto { RoleId = role.RoleId, RoleKey = role.RoleKey, RoleName = role.RoleName, Active = role.Active, IsDefault = role.IsDefault };
    }

    public async Task<IReadOnlyList<AccessPermissionDto>> GetRolePermissionsAsync(int roleId, CancellationToken cancellationToken = default)
    {
        var allowed = await db.AppRolePermissions.AsNoTracking().Where(x => x.RoleId == roleId && x.IsAllowed)
            .Select(x => x.ModuleId + "|" + x.ActionKey)
            .ToListAsync(cancellationToken);
        return await BuildPermissionMatrixAsync(allowed.ToHashSet(), cancellationToken);
    }

    public async Task SaveRolePermissionsAsync(int roleId, SaveRolePermissionsDto request, CancellationToken cancellationToken = default)
    {
        var role = await db.AppRoles.AsNoTracking().FirstOrDefaultAsync(x => x.RoleId == roleId, cancellationToken)
            ?? throw new InvalidOperationException("Role not found.");
        if (IsBreakGlassRoleKey(role.RoleKey))
            throw new InvalidOperationException("The Super Admin role permissions cannot be changed.");
        db.AppRolePermissions.RemoveRange(db.AppRolePermissions.Where(x => x.RoleId == roleId));
        foreach (var p in request.Permissions.Where(x => x.IsAllowed))
        {
            await db.AppRolePermissions.AddAsync(new AppRolePermission { RoleId = roleId, ModuleId = p.ModuleId, ActionKey = p.ActionKey, IsAllowed = true }, cancellationToken);
        }
        await db.SaveChangesAsync(cancellationToken);
    }

    public async Task<PagedResultDto<AccessUserLookupDto>> SearchUsersForAccessAsync(string? search, int pageNumber, int pageSize, CancellationToken cancellationToken = default)
    {
        var users = await GetUsersAsync("all", search, pageNumber, pageSize, cancellationToken);
        return new PagedResultDto<AccessUserLookupDto>
        {
            PageNumber = users.PageNumber,
            PageSize = users.PageSize,
            TotalRecords = users.TotalRecords,
            Items = users.Items.Select(x => new AccessUserLookupDto { UserId = x.UserId, Username = x.Username, FullName = x.FullName, Status = x.Status, RolesText = x.RolesText }).ToList()
        };
    }

    public async Task<UserRoleAssignmentDto> GetUserRolesAsync(int userId, CancellationToken cancellationToken = default)
    {
        var roles = await db.AppUserRoles.AsNoTracking()
            .Where(x => x.UserId == userId && x.Role != null)
            .Select(x => new { x.RoleId, x.Role!.RoleKey })
            .ToListAsync(cancellationToken);
        return new UserRoleAssignmentDto { UserId = userId, RoleIds = roles.Select(x => x.RoleId).ToList(), RoleKeys = roles.Select(x => x.RoleKey).ToList() };
    }

    public async Task SaveUserRolesAsync(int userId, SaveUserRolesDto request, CancellationToken cancellationToken = default)
    {
        var user = await db.AppUsers.AsNoTracking().FirstOrDefaultAsync(x => x.UserId == userId, cancellationToken)
            ?? throw new InvalidOperationException("User not found.");
        if (IsBreakGlassAccount(user))
            throw new InvalidOperationException("The Super Admin break-glass account roles cannot be changed.");
        db.AppUserRoles.RemoveRange(db.AppUserRoles.Where(x => x.UserId == userId));
        foreach (var roleId in request.RoleIds.Distinct())
        {
            if (await db.AppRoles.AnyAsync(x => x.RoleId == roleId, cancellationToken))
                await db.AppUserRoles.AddAsync(new AppUserRole { UserId = userId, RoleId = roleId }, cancellationToken);
        }
        await db.SaveChangesAsync(cancellationToken);
    }

    public async Task<IReadOnlyList<AccessPermissionDto>> GetUserPermissionsAsync(int userId, CancellationToken cancellationToken = default)
    {
        var allowed = await db.AppUserPermissions.AsNoTracking().Where(x => x.UserId == userId && x.IsAllowed)
            .Select(x => x.ModuleId + "|" + x.Action!.ActionKey)
            .ToListAsync(cancellationToken);
        return await BuildPermissionMatrixAsync(allowed.ToHashSet(), cancellationToken);
    }

    public async Task SaveUserPermissionsAsync(int userId, SaveUserPermissionsDto request, CancellationToken cancellationToken = default)
    {
        var user = await db.AppUsers.AsNoTracking().FirstOrDefaultAsync(x => x.UserId == userId, cancellationToken)
            ?? throw new InvalidOperationException("User not found.");
        if (IsBreakGlassAccount(user))
            throw new InvalidOperationException("The Super Admin break-glass account permission overrides cannot be changed.");
        db.AppUserPermissions.RemoveRange(db.AppUserPermissions.Where(x => x.UserId == userId));
        foreach (var p in request.Permissions.Where(x => x.IsAllowed))
        {
            await db.AppUserPermissions.AddAsync(new AppUserPermission { UserId = userId, ModuleId = p.ModuleId, ActionId = p.ActionId, IsAllowed = true }, cancellationToken);
        }
        await db.SaveChangesAsync(cancellationToken);
    }

    public async Task<IReadOnlyList<UserPermissionOverrideDto>> GetUserPermissionOverridesAsync(int userId, CancellationToken cancellationToken = default)
    {
        if (!await db.AppUsers.AnyAsync(x => x.UserId == userId, cancellationToken)) throw new InvalidOperationException("User not found.");

        var roleKeys = await GetUserRoleKeysAsync(userId, cancellationToken);
        var roleAllowed = await GetRoleAllowedActionKeysAsync(roleKeys, cancellationToken);
        var userOverrides = await db.AppUserPermissions.AsNoTracking()
            .Where(x => x.UserId == userId && x.Action != null)
            .Select(x => new { Key = x.ModuleId + "|" + x.ActionId, x.IsAllowed })
            .ToListAsync(cancellationToken);
        var overrideMap = userOverrides.ToDictionary(x => x.Key, x => x.IsAllowed);

        var rows = await db.AppModuleActions.AsNoTracking()
            .Where(x => x.Active && x.Module != null && x.Module.Active)
            .OrderBy(x => x.Module!.SortOrder).ThenBy(x => x.SortOrder).ThenBy(x => x.ActionName)
            .Select(x => new UserPermissionOverrideDto
            {
                ModuleId = x.ModuleId,
                ModuleKey = x.Module!.ModuleKey,
                ModuleName = x.Module.ModuleName,
                ActionId = x.ActionId,
                ActionKey = x.ActionKey,
                ActionName = x.ActionName,
                RoleAllowed = false,
                EffectiveAllowed = false,
                OverrideState = "Default"
            })
            .ToListAsync(cancellationToken);

        foreach (var row in rows)
        {
            var permissionKey = row.ModuleKey + "." + row.ActionKey;
            var rowKey = row.ModuleId + "|" + row.ActionId;
            row.RoleAllowed = roleAllowed.Contains(permissionKey);
            if (overrideMap.TryGetValue(rowKey, out var overrideAllowed))
            {
                row.OverrideState = overrideAllowed ? "Allow" : "Deny";
                row.EffectiveAllowed = overrideAllowed;
            }
            else
            {
                row.EffectiveAllowed = row.RoleAllowed;
            }
        }

        return rows;
    }

    public async Task SaveUserPermissionOverridesAsync(int userId, SaveUserPermissionOverridesDto request, CancellationToken cancellationToken = default)
    {
        var user = await db.AppUsers.AsNoTracking().FirstOrDefaultAsync(x => x.UserId == userId, cancellationToken)
            ?? throw new InvalidOperationException("User not found.");
        if (IsBreakGlassAccount(user))
            throw new InvalidOperationException("The Super Admin break-glass account permission overrides cannot be changed.");
        db.AppUserPermissions.RemoveRange(db.AppUserPermissions.Where(x => x.UserId == userId));

        foreach (var p in request.Permissions)
        {
            var state = (p.OverrideState ?? "Default").Trim();
            if (string.Equals(state, "Default", StringComparison.OrdinalIgnoreCase)) continue;

            var action = await db.AppModuleActions.AsNoTracking().FirstOrDefaultAsync(x => x.ActionId == p.ActionId && x.ModuleId == p.ModuleId && x.Active, cancellationToken)
                ?? throw new InvalidOperationException("Invalid permission action.");
            var isAllowed = string.Equals(state, "Allow", StringComparison.OrdinalIgnoreCase);
            if (!isAllowed && !string.Equals(state, "Deny", StringComparison.OrdinalIgnoreCase))
                throw new InvalidOperationException("Invalid permission override state.");

            await db.AppUserPermissions.AddAsync(new AppUserPermission { UserId = userId, ModuleId = p.ModuleId, ActionId = action.ActionId, IsAllowed = isAllowed }, cancellationToken);
        }

        await db.SaveChangesAsync(cancellationToken);
    }

    private async Task<List<string>> GetFinalPermissionKeysAsync(int userId, List<string> roles, CancellationToken cancellationToken)
    {
        var final = await GetRoleAllowedActionKeysAsync(roles, cancellationToken);

        var userOverrides = await db.AppUserPermissions.AsNoTracking()
            .Where(x => x.UserId == userId && x.Module != null && x.Module.Active && x.Action != null && x.Action.Active)
            .Select(x => new { Key = x.Module!.ModuleKey + "." + x.Action!.ActionKey, x.IsAllowed })
            .ToListAsync(cancellationToken);

        foreach (var userOverride in userOverrides)
        {
            if (userOverride.IsAllowed) final.Add(userOverride.Key);
            else final.Remove(userOverride.Key);
        }

        return final.OrderBy(x => x).ToList();
    }

    private async Task<HashSet<string>> GetRoleAllowedActionKeysAsync(List<string> roles, CancellationToken cancellationToken)
    {
        if (roles.Contains("super_admin", StringComparer.OrdinalIgnoreCase))
        {
            var all = await db.AppModuleActions.AsNoTracking()
                .Where(x => x.Active && x.Module != null && x.Module.Active)
                .Select(x => x.Module!.ModuleKey + "." + x.ActionKey)
                .ToListAsync(cancellationToken);
            return all.ToHashSet(StringComparer.OrdinalIgnoreCase);
        }

        var rolePermissions = await db.AppRolePermissions.AsNoTracking()
            .Where(x => x.IsAllowed && x.Role != null && x.Role.Active && x.Module != null && x.Module.Active && roles.Contains(x.Role.RoleKey))
            .Select(x => x.Module!.ModuleKey + "." + x.ActionKey)
            .ToListAsync(cancellationToken);

        return rolePermissions.ToHashSet(StringComparer.OrdinalIgnoreCase);
    }

    private async Task<List<string>> GetUserRoleKeysAsync(int userId, CancellationToken cancellationToken)
    {
        var roles = await db.AppUserRoles.AsNoTracking()
            .Where(x => x.UserId == userId && x.Role != null && x.Role.Active)
            .Select(x => x.Role!.RoleKey)
            .ToListAsync(cancellationToken);
        if (roles.Count == 0)
        {
            var legacyRole = await db.AppUsers.AsNoTracking().Where(x => x.UserId == userId).Select(x => x.Role).FirstOrDefaultAsync(cancellationToken);
            if (!string.IsNullOrWhiteSpace(legacyRole)) roles.Add(ToRoleKey(legacyRole));
        }
        return roles.Distinct(StringComparer.OrdinalIgnoreCase).ToList();
    }

    private async Task<bool> IsProtectedAdminUserAsync(AppUser user, CancellationToken cancellationToken)
    {
        var roleKeys = await GetUserRoleKeysAsync(user.UserId, cancellationToken);
        if (!string.IsNullOrWhiteSpace(user.Role))
            roleKeys.Add(ToRoleKey(user.Role));

        if (roleKeys.Any(IsProtectedAdminRoleKey))
            return true;

        var normalizedFullName = ToRoleKey(user.FullName);
        return normalizedFullName is "system_admin" or "super_admin" or "superadmin";
    }

    private static bool IsProtectedAdminRoleKey(string roleKey)
    {
        var normalized = ToRoleKey(roleKey);
        return normalized is "super_admin" or "superadmin" or "system_admin" or "admin";
    }

    private static bool IsBreakGlassAccount(AppUser user)
    {
        var username = ToRoleKey(user.Username);
        var fullName = ToRoleKey(user.FullName);
        return username is "superadmin" or "super_admin" ||
               fullName is "super_admin" or "superadmin";
    }

    private static bool IsBreakGlassRoleKey(string roleKey)
    {
        var normalized = ToRoleKey(roleKey);
        return normalized is "super_admin" or "superadmin";
    }

    private async Task<SystemUserListItemDto> BuildUserDtoAsync(AppUser user, CancellationToken cancellationToken)
    {
        var roles = await db.AppUserRoles.AsNoTracking()
            .Where(x => x.UserId == user.UserId && x.Role != null)
            .Select(x => new { x.Role!.RoleKey, x.Role.RoleName })
            .ToListAsync(cancellationToken);
        if (roles.Count == 0 && !string.IsNullOrWhiteSpace(user.Role))
        {
            roles.Add(new { RoleKey = ToRoleKey(user.Role), RoleName = user.Role });
        }

        return new SystemUserListItemDto
        {
            UserId = user.UserId,
            Username = user.Username,
            FullName = user.FullName,
            Email = user.Email,
            Phone = user.Phone,
            Status = user.Status,
            Active = user.Active,
            CreatedDate = user.CreatedDate,
            RolesText = string.Join(", ", roles.Select(x => x.RoleName).Distinct()),
            RoleKeys = roles.Select(x => x.RoleKey).Distinct().ToList()
        };
    }

    private async Task<IReadOnlyList<AccessPermissionDto>> BuildPermissionMatrixAsync(HashSet<string> allowedKeys, CancellationToken cancellationToken)
    {
        var rows = await db.AppModuleActions.AsNoTracking()
            .Where(x => x.Active && x.Module != null && x.Module.Active)
            .OrderBy(x => x.Module!.SortOrder).ThenBy(x => x.SortOrder).ThenBy(x => x.ActionName)
            .Select(x => new AccessPermissionDto
            {
                ModuleId = x.ModuleId,
                ModuleKey = x.Module!.ModuleKey,
                ModuleName = x.Module.ModuleName,
                ActionId = x.ActionId,
                ActionKey = x.ActionKey,
                ActionName = x.ActionName,
                IsAllowed = false
            })
            .ToListAsync(cancellationToken);
        foreach (var row in rows)
            row.IsAllowed = allowedKeys.Contains(row.ModuleId + "|" + row.ActionKey);
        return rows;
    }

    private async Task<AppRole> ResolveRoleAsync(string? roleKey, CancellationToken cancellationToken)
    {
        var key = string.IsNullOrWhiteSpace(roleKey) ? null : ToRoleKey(roleKey);
        var role = key == null
            ? await db.AppRoles.FirstOrDefaultAsync(x => x.IsDefault && x.Active, cancellationToken)
            : await db.AppRoles.FirstOrDefaultAsync(x => x.RoleKey == key && x.Active, cancellationToken);
        return role ?? await db.AppRoles.FirstAsync(x => x.Active, cancellationToken);
    }

    private async Task ClearDefaultRolesAsync(CancellationToken cancellationToken)
    {
        var defaults = await db.AppRoles.Where(x => x.IsDefault).ToListAsync(cancellationToken);
        foreach (var role in defaults) role.IsDefault = false;
    }

    private static string? Clean(string? value) => string.IsNullOrWhiteSpace(value) ? null : value.Trim();
    private static string NormalizeStatus(string? status) => string.Equals(status, "Suspended", StringComparison.OrdinalIgnoreCase) ? "Suspended" : "Active";
    private static string ToRoleKey(string value) => (value ?? string.Empty).Trim().ToLowerInvariant().Replace(" ", "_").Replace("-", "_");
}

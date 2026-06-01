using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using NetworldParkingLot.Api.Common;
using NetworldParkingLot.Api.Features.UserAccess.Dtos;
using NetworldParkingLot.Api.Features.UserAccess.Filters;
using NetworldParkingLot.Api.Features.UserAccess.Services;

namespace NetworldParkingLot.Api.Features.UserAccess.Controllers;

[Authorize]
[ApiController]
[Route("api/access-control")]
public sealed class AccessControlController(IUserAccessService userAccessService) : ControllerBase
{
    [RequireParkingPermission("access_control", "view")]
    [HttpGet("roles")]
    public async Task<ActionResult<ApiResponse<IReadOnlyList<AccessRoleDto>>>> GetRoles(CancellationToken cancellationToken)
    {
        var data = await userAccessService.GetRolesAsync(cancellationToken);
        return Ok(ApiResponse<IReadOnlyList<AccessRoleDto>>.Ok(data));
    }

    [RequireParkingPermission("access_control", "edit")]
    [HttpPost("roles")]
    public async Task<ActionResult<ApiResponse<AccessRoleDto>>> CreateRole([FromBody] CreateAccessRoleDto request, CancellationToken cancellationToken)
    {
        try
        {
            var data = await userAccessService.CreateRoleAsync(request, cancellationToken);
            return Ok(ApiResponse<AccessRoleDto>.Ok(data, "Role created."));
        }
        catch (InvalidOperationException ex)
        {
            return BadRequest(ApiResponse<AccessRoleDto>.Fail(ex.Message));
        }
    }

    [RequireParkingPermission("access_control", "edit")]
    [HttpPut("roles/{roleId:int}")]
    public async Task<ActionResult<ApiResponse<AccessRoleDto>>> UpdateRole(int roleId, [FromBody] UpdateAccessRoleDto request, CancellationToken cancellationToken)
    {
        try
        {
            var data = await userAccessService.UpdateRoleAsync(roleId, request, cancellationToken);
            return Ok(ApiResponse<AccessRoleDto>.Ok(data, "Role updated."));
        }
        catch (InvalidOperationException ex)
        {
            return BadRequest(ApiResponse<AccessRoleDto>.Fail(ex.Message));
        }
    }

    [RequireParkingPermission("access_control", "view")]
    [HttpGet("roles/{roleId:int}/permissions")]
    public async Task<ActionResult<ApiResponse<IReadOnlyList<AccessPermissionDto>>>> GetRolePermissions(int roleId, CancellationToken cancellationToken)
    {
        var data = await userAccessService.GetRolePermissionsAsync(roleId, cancellationToken);
        return Ok(ApiResponse<IReadOnlyList<AccessPermissionDto>>.Ok(data));
    }

    [RequireParkingPermission("access_control", "edit")]
    [HttpPut("roles/{roleId:int}/permissions")]
    public async Task<ActionResult<ApiResponse<object>>> SaveRolePermissions(int roleId, [FromBody] SaveRolePermissionsDto request, CancellationToken cancellationToken)
    {
        try
        {
            await userAccessService.SaveRolePermissionsAsync(roleId, request, cancellationToken);
            return Ok(ApiResponse<object>.Ok(new { success = true }, "Role permissions saved."));
        }
        catch (InvalidOperationException ex)
        {
            return BadRequest(ApiResponse<object>.Fail(ex.Message));
        }
    }

    [RequireParkingPermission("access_control", "view")]
    [HttpGet("users")]
    public async Task<ActionResult<ApiResponse<PagedResultDto<AccessUserLookupDto>>>> SearchUsers(
        [FromQuery] string? search,
        [FromQuery] int pageNumber = 1,
        [FromQuery] int pageSize = 20,
        CancellationToken cancellationToken = default)
    {
        var data = await userAccessService.SearchUsersForAccessAsync(search, pageNumber, pageSize, cancellationToken);
        return Ok(ApiResponse<PagedResultDto<AccessUserLookupDto>>.Ok(data));
    }

    [RequireParkingPermission("access_control", "view")]
    [HttpGet("users/{userId:int}/roles")]
    public async Task<ActionResult<ApiResponse<UserRoleAssignmentDto>>> GetUserRoles(int userId, CancellationToken cancellationToken)
    {
        var data = await userAccessService.GetUserRolesAsync(userId, cancellationToken);
        return Ok(ApiResponse<UserRoleAssignmentDto>.Ok(data));
    }

    [RequireParkingPermission("access_control", "edit")]
    [HttpPut("users/{userId:int}/roles")]
    public async Task<ActionResult<ApiResponse<object>>> SaveUserRoles(int userId, [FromBody] SaveUserRolesDto request, CancellationToken cancellationToken)
    {
        try
        {
            await userAccessService.SaveUserRolesAsync(userId, request, cancellationToken);
            return Ok(ApiResponse<object>.Ok(new { success = true }, "User roles saved."));
        }
        catch (InvalidOperationException ex)
        {
            return BadRequest(ApiResponse<object>.Fail(ex.Message));
        }
    }

    [RequireParkingPermission("access_control", "view")]
    [HttpGet("users/{userId:int}/permissions")]
    public async Task<ActionResult<ApiResponse<IReadOnlyList<AccessPermissionDto>>>> GetUserPermissions(int userId, CancellationToken cancellationToken)
    {
        var data = await userAccessService.GetUserPermissionsAsync(userId, cancellationToken);
        return Ok(ApiResponse<IReadOnlyList<AccessPermissionDto>>.Ok(data));
    }

    [RequireParkingPermission("access_control", "edit")]
    [HttpPut("users/{userId:int}/permissions")]
    public async Task<ActionResult<ApiResponse<object>>> SaveUserPermissions(int userId, [FromBody] SaveUserPermissionsDto request, CancellationToken cancellationToken)
    {
        try
        {
            await userAccessService.SaveUserPermissionsAsync(userId, request, cancellationToken);
            return Ok(ApiResponse<object>.Ok(new { success = true }, "User permission overrides saved."));
        }
        catch (InvalidOperationException ex)
        {
            return BadRequest(ApiResponse<object>.Fail(ex.Message));
        }
    }
}

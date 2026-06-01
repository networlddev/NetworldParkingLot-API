using System.Security.Claims;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using NetworldParkingLot.Api.Common;
using NetworldParkingLot.Api.Features.UserAccess.Dtos;
using NetworldParkingLot.Api.Features.UserAccess.Filters;
using NetworldParkingLot.Api.Features.UserAccess.Services;

namespace NetworldParkingLot.Api.Features.UserAccess.Controllers;

[Authorize]
[ApiController]
[Route("api/system-users")]
public sealed class SystemUsersController(IUserAccessService userAccessService) : ControllerBase
{
    [RequireParkingPermission("system_users", "view")]
    [HttpGet]
    public async Task<ActionResult<ApiResponse<PagedResultDto<SystemUserListItemDto>>>> GetPaged(
        [FromQuery] string? tab,
        [FromQuery] string? search,
        [FromQuery] int pageNumber = 1,
        [FromQuery] int pageSize = 20,
        CancellationToken cancellationToken = default)
    {
        var data = await userAccessService.GetUsersAsync(tab, search, pageNumber, pageSize, cancellationToken);
        return Ok(ApiResponse<PagedResultDto<SystemUserListItemDto>>.Ok(data));
    }

    [RequireParkingPermission("system_users", "view")]
    [HttpGet("counts")]
    public async Task<ActionResult<ApiResponse<SystemUserCountsDto>>> Counts(CancellationToken cancellationToken)
    {
        var data = await userAccessService.GetUserCountsAsync(cancellationToken);
        return Ok(ApiResponse<SystemUserCountsDto>.Ok(data));
    }

    [RequireParkingPermission("system_users", "view")]
    [HttpGet("{userId:int}")]
    public async Task<ActionResult<ApiResponse<SystemUserListItemDto>>> GetById(int userId, CancellationToken cancellationToken)
    {
        try
        {
            var data = await userAccessService.GetUserAsync(userId, cancellationToken);
            return Ok(ApiResponse<SystemUserListItemDto>.Ok(data));
        }
        catch (InvalidOperationException ex)
        {
            return NotFound(ApiResponse<SystemUserListItemDto>.Fail(ex.Message));
        }
    }

    [RequireParkingPermission("system_users", "create")]
    [HttpPost]
    public async Task<ActionResult<ApiResponse<SystemUserListItemDto>>> Create([FromBody] CreateSystemUserDto request, CancellationToken cancellationToken)
    {
        try
        {
            var data = await userAccessService.CreateUserAsync(request, CurrentUserId(), cancellationToken);
            return Ok(ApiResponse<SystemUserListItemDto>.Ok(data, "User created."));
        }
        catch (InvalidOperationException ex)
        {
            return BadRequest(ApiResponse<SystemUserListItemDto>.Fail(ex.Message));
        }
    }

    [RequireParkingPermission("system_users", "edit")]
    [HttpPut("{userId:int}")]
    public async Task<ActionResult<ApiResponse<SystemUserListItemDto>>> Update(int userId, [FromBody] UpdateSystemUserDto request, CancellationToken cancellationToken)
    {
        try
        {
            var data = await userAccessService.UpdateUserAsync(userId, request, CurrentUserId(), cancellationToken);
            return Ok(ApiResponse<SystemUserListItemDto>.Ok(data, "User updated."));
        }
        catch (InvalidOperationException ex)
        {
            return BadRequest(ApiResponse<SystemUserListItemDto>.Fail(ex.Message));
        }
    }

    [RequireParkingPermission("system_users", "suspend")]
    [HttpPut("{userId:int}/status")]
    public async Task<ActionResult<ApiResponse<object>>> SetStatus(int userId, [FromBody] SetUserStatusDto request, CancellationToken cancellationToken)
    {
        try
        {
            await userAccessService.SetUserStatusAsync(userId, request.Status, CurrentUserId(), cancellationToken);
            return Ok(ApiResponse<object>.Ok(new { success = true }, "User status updated."));
        }
        catch (InvalidOperationException ex)
        {
            return BadRequest(ApiResponse<object>.Fail(ex.Message));
        }
    }

    [RequireParkingPermission("system_users", "delete")]
    [HttpDelete("{userId:int}")]
    public async Task<ActionResult<ApiResponse<object>>> Delete(int userId, CancellationToken cancellationToken)
    {
        try
        {
            await userAccessService.DeleteUserAsync(userId, CurrentUserId(), cancellationToken);
            return Ok(ApiResponse<object>.Ok(new { success = true }, "User deleted."));
        }
        catch (InvalidOperationException ex)
        {
            return BadRequest(ApiResponse<object>.Fail(ex.Message));
        }
    }

    private int CurrentUserId()
    {
        var userIdText = User.FindFirstValue(ClaimTypes.NameIdentifier);
        return int.TryParse(userIdText, out var userId) ? userId : 0;
    }
}

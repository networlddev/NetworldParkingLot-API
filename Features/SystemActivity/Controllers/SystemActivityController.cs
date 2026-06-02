using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using NetworldParkingLot.Api.Common;
using NetworldParkingLot.Api.Features.SystemActivity.Services;
using NetworldParkingLot.Api.Features.UserAccess.Filters;

namespace NetworldParkingLot.Api.Features.SystemActivity.Controllers;

[Authorize]
[ApiController]
[Route("api/system-activity")]
public sealed class SystemActivityController(ISystemActivityService activityService) : ControllerBase
{
    [RequireParkingPermission("history", "view")]
    [HttpGet]
    public async Task<ActionResult<ApiResponse<PagedSystemActivityLogResultDto>>> Search(
        [FromQuery] string? searchText,
        [FromQuery] int? userId,
        [FromQuery] string? moduleKey,
        [FromQuery] string? actionKey,
        [FromQuery] string? result,
        [FromQuery] string? entityType,
        [FromQuery] string? entityId,
        [FromQuery] DateTime? dateFrom,
        [FromQuery] DateTime? dateTo,
        [FromQuery] string? sortDirection,
        [FromQuery] int page = 1,
        [FromQuery] int pageSize = 25,
        CancellationToken cancellationToken = default)
    {
        var request = new SystemActivityQueryRequest
        {
            SearchText = searchText,
            UserId = userId,
            ModuleKey = moduleKey,
            ActionKey = actionKey,
            Result = result,
            EntityType = entityType,
            EntityId = entityId,
            DateFrom = dateFrom,
            DateTo = dateTo,
            SortDirection = sortDirection,
            Page = page,
            PageSize = pageSize
        };

        var data = await activityService.SearchAsync(request, cancellationToken);
        return Ok(ApiResponse<PagedSystemActivityLogResultDto>.Ok(data));
    }

    [RequireParkingPermission("history", "view")]
    [HttpGet("{systemActivityLogId:long}")]
    public async Task<ActionResult<ApiResponse<SystemActivityLogDto>>> GetById(long systemActivityLogId, CancellationToken cancellationToken)
    {
        try
        {
            var data = await activityService.GetByIdAsync(systemActivityLogId, cancellationToken);
            return Ok(ApiResponse<SystemActivityLogDto>.Ok(data));
        }
        catch (InvalidOperationException ex)
        {
            return NotFound(ApiResponse<SystemActivityLogDto>.Fail(ex.Message));
        }
    }
}

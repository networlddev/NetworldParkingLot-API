using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using NetworldParkingLot.Api.Common;
using NetworldParkingLot.Api.Features.Dashboard.Dtos;
using NetworldParkingLot.Api.Features.Dashboard.Services;
using NetworldParkingLot.Api.Features.UserAccess.Filters;

namespace NetworldParkingLot.Api.Features.Dashboard.Controllers;

[Authorize]
[ApiController]
[Route("api/dashboard")]
public sealed class DashboardController(IDashboardService dashboardService) : ControllerBase
{
    [RequireParkingPermission("dashboard", "view")]
    [HttpGet("summary")]
    public async Task<ActionResult<ApiResponse<DashboardSummaryDto>>> GetSummary(
        [FromQuery] string? range,
        [FromQuery] DateTime? dateFrom,
        [FromQuery] DateTime? dateTo,
        CancellationToken cancellationToken)
    {
        var data = await dashboardService.GetSummaryAsync(range, dateFrom, dateTo, cancellationToken);
        return Ok(ApiResponse<DashboardSummaryDto>.Ok(data));
    }
}

using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using NetworldParkingLot.Api.Common;
using NetworldParkingLot.Api.Features.Reports.Dtos;
using NetworldParkingLot.Api.Features.Reports.Services;
using NetworldParkingLot.Api.Features.UserAccess.Filters;

namespace NetworldParkingLot.Api.Features.Reports.Controllers;

[Authorize]
[ApiController]
[Route("api/reports")]
public sealed class ReportsController(IReportsService reportsService) : ControllerBase
{
    [RequireParkingPermission("reports", "view")]
    [HttpGet("catalog")]
    public ActionResult<ApiResponse<IReadOnlyList<ReportCatalogItemDto>>> GetCatalog()
    {
        return Ok(ApiResponse<IReadOnlyList<ReportCatalogItemDto>>.Ok(reportsService.GetCatalog()));
    }

    [RequireParkingPermission("reports", "view")]
    [HttpGet("{reportKey}/preview")]
    public async Task<ActionResult<ApiResponse<ReportResultDto>>> Preview(string reportKey, [FromQuery] ReportQueryDto query, CancellationToken cancellationToken)
    {
        var data = await reportsService.RunAsync(reportKey, query, cancellationToken);
        return Ok(ApiResponse<ReportResultDto>.Ok(data));
    }

    [RequireParkingPermission("reports", "export")]
    [HttpGet("{reportKey}/export")]
    public async Task<ActionResult<ApiResponse<ReportResultDto>>> Export(string reportKey, [FromQuery] ReportQueryDto query, CancellationToken cancellationToken)
    {
        query.PageNumber = 1;
        query.PageSize = Math.Max(query.PageSize, 500);
        var data = await reportsService.RunAsync(reportKey, query, cancellationToken);
        return Ok(ApiResponse<ReportResultDto>.Ok(data, "Report export data generated."));
    }
}

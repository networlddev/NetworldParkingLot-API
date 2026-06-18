using System.Security.Claims;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using NetworldParkingLot.Api.Common;
using NetworldParkingLot.Api.Common.Security;
using NetworldParkingLot.Api.Features.Settings.Dtos;
using NetworldParkingLot.Api.Features.Settings.Services;
using NetworldParkingLot.Api.Features.SystemActivity.Services;
using NetworldParkingLot.Api.Features.UserAccess.Filters;

namespace NetworldParkingLot.Api.Features.Settings.Controllers;

[Authorize]
[ApiController]
[Route("api/settings")]
public sealed class SettingsController(ISettingsService service, ISystemActivityService activityService) : ControllerBase
{
    [RequireParkingPermission("settings", "view")]
    [HttpGet]
    public async Task<ActionResult<ApiResponse<ParkingSettingsDto>>> GetSettings(CancellationToken cancellationToken)
    {
        var data = await service.GetSettingsAsync(cancellationToken);
        return Ok(ApiResponse<ParkingSettingsDto>.Ok(data, "Settings loaded."));
    }

    [RequireParkingPermission("settings", "edit")]
    [HttpPut]
    [HttpPost]
    public async Task<ActionResult<ApiResponse<ParkingSettingsDto>>> UpdateSettings([FromBody] UpdateParkingSettingsRequest request, CancellationToken cancellationToken)
    {
        try
        {
            var operatorId = GetCurrentUserId();
            var data = await service.UpdateSettingsAsync(request, operatorId, cancellationToken);
            return Ok(ApiResponse<ParkingSettingsDto>.Ok(data, "Settings saved successfully."));
        }
        catch (InvalidOperationException ex)
        {
            await activityService.RecordAsync(this.BuildActivity(
                "settings",
                "edit",
                "Failure",
                "SystemSettings",
                null,
                "Settings update failed",
                ex.Message), cancellationToken);
            return BadRequest(ApiResponse<ParkingSettingsDto>.Fail(ex.Message));
        }
    }

    private int GetCurrentUserId()
    {
        var id = User.FindFirstValue(ClaimTypes.NameIdentifier) ?? User.FindFirstValue("sub");
        return int.TryParse(id, out var parsed) ? parsed : 0;
    }
}

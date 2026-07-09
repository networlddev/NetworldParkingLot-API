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

    [RequireParkingPermission("settings", "view")]
    [HttpGet("rate-plans")]
    public async Task<ActionResult<ApiResponse<IReadOnlyList<ParkingRatePlanDto>>>> GetRatePlans([FromQuery] bool includeInactive, CancellationToken cancellationToken)
    {
        var data = await service.GetRatePlansAsync(includeInactive, cancellationToken);
        return Ok(ApiResponse<IReadOnlyList<ParkingRatePlanDto>>.Ok(data));
    }

    [RequireParkingPermission("settings", "edit")]
    [HttpPost("rate-plans")]
    public async Task<ActionResult<ApiResponse<ParkingRatePlanDto>>> CreateRatePlan([FromBody] SaveParkingRatePlanRequest request, CancellationToken cancellationToken) =>
        await SaveRatePlan(null, request, cancellationToken);

    [RequireParkingPermission("settings", "edit")]
    [HttpPut("rate-plans/{ratePlanId:int}")]
    public async Task<ActionResult<ApiResponse<ParkingRatePlanDto>>> UpdateRatePlan(int ratePlanId, [FromBody] SaveParkingRatePlanRequest request, CancellationToken cancellationToken) =>
        await SaveRatePlan(ratePlanId, request, cancellationToken);

    [RequireParkingPermission("settings", "view")]
    [HttpGet("vehicle-types")]
    public async Task<ActionResult<ApiResponse<IReadOnlyList<ParkingVehicleTypeDto>>>> GetVehicleTypes([FromQuery] bool includeInactive, CancellationToken cancellationToken)
    {
        var data = await service.GetVehicleTypesAsync(includeInactive, cancellationToken);
        return Ok(ApiResponse<IReadOnlyList<ParkingVehicleTypeDto>>.Ok(data));
    }

    [RequireParkingPermission("settings", "edit")]
    [HttpPost("vehicle-types")]
    public async Task<ActionResult<ApiResponse<ParkingVehicleTypeDto>>> CreateVehicleType([FromBody] SaveParkingVehicleTypeRequest request, CancellationToken cancellationToken) =>
        await SaveVehicleType(null, request, cancellationToken);

    [RequireParkingPermission("settings", "edit")]
    [HttpPut("vehicle-types/{vehicleTypeId:int}")]
    public async Task<ActionResult<ApiResponse<ParkingVehicleTypeDto>>> UpdateVehicleType(int vehicleTypeId, [FromBody] SaveParkingVehicleTypeRequest request, CancellationToken cancellationToken) =>
        await SaveVehicleType(vehicleTypeId, request, cancellationToken);

    [RequireParkingPermission("settings", "view")]
    [HttpGet("bank-accounts")]
    public async Task<ActionResult<ApiResponse<IReadOnlyList<ParkingBankAccountDto>>>> GetBankAccounts([FromQuery] bool includeInactive, CancellationToken cancellationToken)
    {
        var data = await service.GetBankAccountsAsync(includeInactive, cancellationToken);
        return Ok(ApiResponse<IReadOnlyList<ParkingBankAccountDto>>.Ok(data));
    }

    [RequireParkingPermission("settings", "edit")]
    [HttpPost("bank-accounts")]
    public async Task<ActionResult<ApiResponse<ParkingBankAccountDto>>> CreateBankAccount([FromBody] SaveParkingBankAccountRequest request, CancellationToken cancellationToken) =>
        await SaveBankAccount(null, request, cancellationToken);

    [RequireParkingPermission("settings", "edit")]
    [HttpPut("bank-accounts/{bankAccountId:int}")]
    public async Task<ActionResult<ApiResponse<ParkingBankAccountDto>>> UpdateBankAccount(int bankAccountId, [FromBody] SaveParkingBankAccountRequest request, CancellationToken cancellationToken) =>
        await SaveBankAccount(bankAccountId, request, cancellationToken);

    private int GetCurrentUserId()
    {
        var id = User.FindFirstValue(ClaimTypes.NameIdentifier) ?? User.FindFirstValue("sub");
        return int.TryParse(id, out var parsed) ? parsed : 0;
    }

    private async Task<ActionResult<ApiResponse<ParkingRatePlanDto>>> SaveRatePlan(int? ratePlanId, SaveParkingRatePlanRequest request, CancellationToken cancellationToken)
    {
        try
        {
            var data = await service.SaveRatePlanAsync(ratePlanId, request, GetCurrentUserId(), cancellationToken);
            return Ok(ApiResponse<ParkingRatePlanDto>.Ok(data, "Rate plan saved."));
        }
        catch (InvalidOperationException ex)
        {
            return BadRequest(ApiResponse<ParkingRatePlanDto>.Fail(ex.Message));
        }
    }

    private async Task<ActionResult<ApiResponse<ParkingVehicleTypeDto>>> SaveVehicleType(int? vehicleTypeId, SaveParkingVehicleTypeRequest request, CancellationToken cancellationToken)
    {
        try
        {
            var data = await service.SaveVehicleTypeAsync(vehicleTypeId, request, GetCurrentUserId(), cancellationToken);
            return Ok(ApiResponse<ParkingVehicleTypeDto>.Ok(data, "Vehicle type saved."));
        }
        catch (InvalidOperationException ex)
        {
            return BadRequest(ApiResponse<ParkingVehicleTypeDto>.Fail(ex.Message));
        }
    }

    private async Task<ActionResult<ApiResponse<ParkingBankAccountDto>>> SaveBankAccount(int? bankAccountId, SaveParkingBankAccountRequest request, CancellationToken cancellationToken)
    {
        try
        {
            var data = await service.SaveBankAccountAsync(bankAccountId, request, GetCurrentUserId(), cancellationToken);
            return Ok(ApiResponse<ParkingBankAccountDto>.Ok(data, "Bank account saved."));
        }
        catch (InvalidOperationException ex)
        {
            return BadRequest(ApiResponse<ParkingBankAccountDto>.Fail(ex.Message));
        }
    }
}

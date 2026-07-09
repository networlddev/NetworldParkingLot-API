using NetworldParkingLot.Api.Features.Settings.Dtos;

namespace NetworldParkingLot.Api.Features.Settings.Services;

public interface ISettingsService
{
    Task<ParkingSettingsDto> GetSettingsAsync(CancellationToken cancellationToken = default);
    Task<ParkingSettingsDto> UpdateSettingsAsync(UpdateParkingSettingsRequest request, int operatorId, CancellationToken cancellationToken = default);
    Task<IReadOnlyList<ParkingRatePlanDto>> GetRatePlansAsync(bool includeInactive = false, CancellationToken cancellationToken = default);
    Task<ParkingRatePlanDto> SaveRatePlanAsync(int? ratePlanId, SaveParkingRatePlanRequest request, int operatorId, CancellationToken cancellationToken = default);
    Task<IReadOnlyList<ParkingVehicleTypeDto>> GetVehicleTypesAsync(bool includeInactive = false, CancellationToken cancellationToken = default);
    Task<ParkingVehicleTypeDto> SaveVehicleTypeAsync(int? vehicleTypeId, SaveParkingVehicleTypeRequest request, int operatorId, CancellationToken cancellationToken = default);
    Task<IReadOnlyList<ParkingBankAccountDto>> GetBankAccountsAsync(bool includeInactive = false, CancellationToken cancellationToken = default);
    Task<ParkingBankAccountDto> SaveBankAccountAsync(int? bankAccountId, SaveParkingBankAccountRequest request, int operatorId, CancellationToken cancellationToken = default);
    Task<IReadOnlyList<ParkingRateVehicleTypeMappingDto>> GetRateVehicleTypeMappingsAsync(bool includeInactive = false, CancellationToken cancellationToken = default);
    Task<ParkingRateVehicleTypeMappingDto> SaveRateVehicleTypeMappingAsync(int? mappingId, SaveParkingRateVehicleTypeMappingRequest request, int operatorId, CancellationToken cancellationToken = default);
}

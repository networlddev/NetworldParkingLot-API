using NetworldParkingLot.Api.Features.Settings.Dtos;

namespace NetworldParkingLot.Api.Features.Settings.Services;

public interface ISettingsService
{
    Task<ParkingSettingsDto> GetSettingsAsync(CancellationToken cancellationToken = default);
    Task<ParkingSettingsDto> UpdateSettingsAsync(UpdateParkingSettingsRequest request, int operatorId, CancellationToken cancellationToken = default);
}

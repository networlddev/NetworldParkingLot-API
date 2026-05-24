using NetworldParkingLot.Api.Domain.Entities;

namespace NetworldParkingLot.Api.Features.GateOperations.Repositories;

public interface IGateRepository
{
    Task<int> SaveChangesAsync(CancellationToken cancellationToken = default);
    Task<ParkingCompany?> GetCompanyAsync(int companyId, CancellationToken cancellationToken = default);
    Task<ParkingSession?> GetSessionAsync(int sessionId, CancellationToken cancellationToken = default);
    Task<ParkingSession?> GetSessionByBarcodeAsync(string barcodeNo, CancellationToken cancellationToken = default);
    Task<ParkingSubscription?> GetBestActiveSubscriptionAsync(int companyId, CancellationToken cancellationToken = default);
    Task<int> GetActiveSlotCountAsync(int companyId, CancellationToken cancellationToken = default);
    Task<int> GetInsideVehicleCountAsync(int companyId, CancellationToken cancellationToken = default);
    Task<decimal> GetPendingAmountAsync(int companyId, CancellationToken cancellationToken = default);
    Task<Dictionary<string, string>> GetSettingsAsync(CancellationToken cancellationToken = default);
    Task AddActivityAsync(GateActivityLog log, CancellationToken cancellationToken = default);
    Task AddOutsideDisplayEventAsync(OutsideDisplayEvent displayEvent, CancellationToken cancellationToken = default);
}

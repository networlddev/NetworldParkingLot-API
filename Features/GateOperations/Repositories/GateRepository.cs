using Microsoft.EntityFrameworkCore;
using NetworldParkingLot.Api.Data;
using NetworldParkingLot.Api.Domain.Constants;
using NetworldParkingLot.Api.Domain.Entities;

namespace NetworldParkingLot.Api.Features.GateOperations.Repositories;

public sealed class GateRepository(NetworldParkingDbContext db) : IGateRepository
{
    public Task<int> SaveChangesAsync(CancellationToken cancellationToken = default) => db.SaveChangesAsync(cancellationToken);

    public Task<ParkingCompany?> GetCompanyAsync(int companyId, CancellationToken cancellationToken = default) =>
        db.ParkingCompanies.FirstOrDefaultAsync(x => x.CompanyId == companyId, cancellationToken);

    public Task<ParkingSession?> GetSessionAsync(int sessionId, CancellationToken cancellationToken = default) =>
        db.ParkingSessions
            .Include(x => x.Company)
            .Include(x => x.Subscription)
            .FirstOrDefaultAsync(x => x.SessionId == sessionId, cancellationToken);

    public Task<ParkingSession?> GetSessionByBarcodeAsync(string barcodeNo, CancellationToken cancellationToken = default) =>
        db.ParkingSessions
            .Include(x => x.Company)
            .Include(x => x.Subscription)
            .FirstOrDefaultAsync(x => x.BarcodeNo == barcodeNo, cancellationToken);

    public Task<ParkingSubscription?> GetBestActiveSubscriptionAsync(int companyId, CancellationToken cancellationToken = default)
    {
        var today = DateTime.Today;
        return db.ParkingSubscriptions
            .Where(x => x.CompanyId == companyId &&
                        x.Status == ParkingConstants.SubscriptionStatus.Active &&
                        x.StartDate.Date <= today &&
                        x.EndDate.Date >= today)
            .OrderBy(x => x.EndDate)
            .FirstOrDefaultAsync(cancellationToken);
    }

    public Task<int> GetActiveSlotCountAsync(int companyId, CancellationToken cancellationToken = default)
    {
        var today = DateTime.Today;
        return db.ParkingSubscriptions
            .Where(x => x.CompanyId == companyId &&
                        x.Status == ParkingConstants.SubscriptionStatus.Active &&
                        x.StartDate.Date <= today &&
                        x.EndDate.Date >= today)
            .SumAsync(x => (int?)x.SlotsPurchased, cancellationToken)
            .ContinueWith(x => x.Result ?? 0, cancellationToken);
    }

    public Task<int> GetInsideVehicleCountAsync(int companyId, CancellationToken cancellationToken = default) =>
        db.ParkingSessions.CountAsync(x => x.CompanyId == companyId && x.Status == ParkingConstants.SessionStatus.Inside, cancellationToken);

    public Task<decimal> GetPendingAmountAsync(int companyId, CancellationToken cancellationToken = default) =>
        db.ParkingInvoices
            .Where(x => x.CompanyId == companyId && x.BalanceAmount > 0)
            .SumAsync(x => (decimal?)x.BalanceAmount, cancellationToken)
            .ContinueWith(x => x.Result ?? 0m, cancellationToken);

    public async Task<Dictionary<string, string>> GetSettingsAsync(CancellationToken cancellationToken = default)
    {
        return await db.SystemSettings
            .AsNoTracking()
            .ToDictionaryAsync(x => x.SettingKey, x => x.SettingValue, cancellationToken);
    }

    public async Task AddActivityAsync(GateActivityLog log, CancellationToken cancellationToken = default)
    {
        await db.GateActivityLogs.AddAsync(log, cancellationToken);
    }

    public async Task AddOutsideDisplayEventAsync(OutsideDisplayEvent displayEvent, CancellationToken cancellationToken = default)
    {
        await db.OutsideDisplayEvents.AddAsync(displayEvent, cancellationToken);
    }
}

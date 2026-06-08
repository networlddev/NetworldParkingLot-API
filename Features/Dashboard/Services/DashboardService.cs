using Microsoft.EntityFrameworkCore;
using NetworldParkingLot.Api.Data;
using NetworldParkingLot.Api.Domain.Constants;
using NetworldParkingLot.Api.Features.Dashboard.Dtos;

namespace NetworldParkingLot.Api.Features.Dashboard.Services;

public sealed class DashboardService(NetworldParkingDbContext db) : IDashboardService
{
    public async Task<DashboardSummaryDto> GetSummaryAsync(string? range, DateTime? dateFrom, DateTime? dateTo, CancellationToken cancellationToken = default)
    {
        var (from, to) = ResolveRange(range, dateFrom, dateTo);
        var toExclusive = to.Date.AddDays(1);
        var selectedDays = Math.Max((toExclusive - from.Date).Days, 1);
        var previousFrom = from.Date.AddDays(-selectedDays);
        var previousTo = from.Date.AddDays(-1);
        var previousToExclusive = from.Date;
        var today = DateTime.Today;
        var expiringTo = today.AddDays(30);

        var capacity = await GetTotalCapacityAsync(cancellationToken);
        var vehiclesInside = await db.ParkingSessions.CountAsync(x => x.Status == ParkingConstants.SessionStatus.Inside, cancellationToken);
        var overstayVehicles = await CountCurrentOverstayVehiclesAsync(today, cancellationToken);
        var collectedAmount = await db.ParkingPayments.Where(x => x.PaymentDate >= from && x.PaymentDate < toExclusive).SumAsync(x => (decimal?)x.Amount, cancellationToken) ?? 0;
        var newCompanies = await db.ParkingCompanies.CountAsync(x => x.CreatedDate >= from && x.CreatedDate < toExclusive, cancellationToken);

        var dto = new DashboardSummaryDto
        {
            DateFrom = from,
            DateTo = to,
            PreviousDateFrom = previousFrom,
            PreviousDateTo = previousTo,
            TotalCapacity = capacity,
            VehiclesInside = vehiclesInside,
            AvailableSpaces = Math.Max(capacity - vehiclesInside, 0),
            TotalCompanies = await db.ParkingCompanies.CountAsync(cancellationToken),
            ActiveCompanies = await db.ParkingCompanies.CountAsync(x => x.Status == ParkingConstants.CompanyStatus.Active, cancellationToken),
            ActiveSubscriptions = await db.ParkingSubscriptions.CountAsync(x => x.Status == ParkingConstants.SubscriptionStatus.Active && x.EndDate.Date >= today, cancellationToken),
            ExpiringSubscriptions = await db.ParkingSubscriptions.CountAsync(x => x.Status == ParkingConstants.SubscriptionStatus.Active && x.EndDate.Date >= today && x.EndDate.Date <= expiringTo, cancellationToken),
            PendingInvoices = await db.ParkingInvoices.CountAsync(x => x.BalanceAmount > 0 && x.Status != "Cancelled", cancellationToken),
            PendingAmount = await db.ParkingInvoices.Where(x => x.BalanceAmount > 0 && x.Status != "Cancelled").SumAsync(x => (decimal?)x.BalanceAmount, cancellationToken) ?? 0,
            CollectedAmount = collectedAmount,
            NewCompanies = newCompanies,
            OverstayVehicles = overstayVehicles
        };

        dto.ComparisonMetrics = await BuildComparisonMetricsAsync(from, toExclusive, previousFrom, previousToExclusive, collectedAmount, newCompanies, cancellationToken);
        dto.CompanyRegistrationTrend = await BuildCompanyRegistrationTrendAsync(from, toExclusive, cancellationToken);
        dto.EntryExitTrend = await BuildEntryExitTrendAsync(from, toExclusive, cancellationToken);
        dto.RevenueTrend = await BuildRevenueTrendAsync(from, toExclusive, cancellationToken);
        dto.InvoiceStatus = await BuildStatusChartAsync(db.ParkingInvoices.AsNoTracking().Where(x => x.InvoiceDate >= from && x.InvoiceDate < toExclusive).Select(x => x.Status), cancellationToken);
        dto.SubscriptionStatus = await BuildStatusChartAsync(db.ParkingSubscriptions.AsNoTracking().Where(x => x.StartDate < toExclusive && x.EndDate >= from).Select(x => x.Status), cancellationToken);
        dto.PaymentModes = await BuildPaymentModeChartAsync(from, toExclusive, cancellationToken);
        dto.TopUsageCompanies = await BuildTopUsageCompaniesAsync(from, toExclusive, cancellationToken);
        dto.TopPendingCompanies = await BuildTopPendingCompaniesAsync(cancellationToken);

        return dto;
    }

    private async Task<int> CountCurrentOverstayVehiclesAsync(DateTime today, CancellationToken cancellationToken)
    {
        return await db.ParkingSessions.AsNoTracking()
            .CountAsync(x =>
                x.Status == ParkingConstants.SessionStatus.Inside &&
                (
                    x.OverstayDays > 0 ||
                    x.OverstayAmount > 0 ||
                    (
                        x.Subscription != null &&
                        x.Subscription.EndDate.Date < today.Date
                    )
                ),
                cancellationToken);
    }

    private async Task<List<DashboardComparisonMetricDto>> BuildComparisonMetricsAsync(
        DateTime from,
        DateTime toExclusive,
        DateTime previousFrom,
        DateTime previousToExclusive,
        decimal currentCollectedAmount,
        int currentNewCompanies,
        CancellationToken cancellationToken)
    {
        var currentEntries = await db.ParkingSessions.CountAsync(x => x.EntryTime != null && x.EntryTime >= from && x.EntryTime < toExclusive, cancellationToken);
        var currentExits = await db.ParkingSessions.CountAsync(x => x.ExitTime != null && x.ExitTime >= from && x.ExitTime < toExclusive, cancellationToken);
        var currentOverstay = await db.ParkingSessions.CountAsync(x => x.EntryTime != null && x.EntryTime >= from && x.EntryTime < toExclusive && (x.OverstayDays > 0 || x.OverstayAmount > 0), cancellationToken);

        var previousCollectedAmount = await db.ParkingPayments.Where(x => x.PaymentDate >= previousFrom && x.PaymentDate < previousToExclusive).SumAsync(x => (decimal?)x.Amount, cancellationToken) ?? 0;
        var previousNewCompanies = await db.ParkingCompanies.CountAsync(x => x.CreatedDate >= previousFrom && x.CreatedDate < previousToExclusive, cancellationToken);
        var previousEntries = await db.ParkingSessions.CountAsync(x => x.EntryTime != null && x.EntryTime >= previousFrom && x.EntryTime < previousToExclusive, cancellationToken);
        var previousExits = await db.ParkingSessions.CountAsync(x => x.ExitTime != null && x.ExitTime >= previousFrom && x.ExitTime < previousToExclusive, cancellationToken);
        var previousOverstay = await db.ParkingSessions.CountAsync(x => x.EntryTime != null && x.EntryTime >= previousFrom && x.EntryTime < previousToExclusive && (x.OverstayDays > 0 || x.OverstayAmount > 0), cancellationToken);

        return
        [
            BuildMetric("revenue", "Revenue", currentCollectedAmount, previousCollectedAmount, "money"),
            BuildMetric("new_companies", "New Companies", currentNewCompanies, previousNewCompanies, "number"),
            BuildMetric("entries", "Entries", currentEntries, previousEntries, "number"),
            BuildMetric("exits", "Exits", currentExits, previousExits, "number"),
            BuildMetric("overstay", "Overstay", currentOverstay, previousOverstay, "number")
        ];
    }

    private async Task<int> GetTotalCapacityAsync(CancellationToken cancellationToken)
    {
        var value = await db.SystemSettings.AsNoTracking()
            .Where(x => x.SettingKey == "TotalParkingCapacity")
            .Select(x => x.SettingValue)
            .FirstOrDefaultAsync(cancellationToken);
        return int.TryParse(value, out var capacity) ? capacity : 0;
    }

    private async Task<List<DashboardChartPointDto>> BuildEntryExitTrendAsync(DateTime from, DateTime toExclusive, CancellationToken cancellationToken)
    {
        var entries = await db.ParkingSessions.AsNoTracking()
            .Where(x => x.EntryTime != null && x.EntryTime >= from && x.EntryTime < toExclusive)
            .GroupBy(x => x.EntryTime!.Value.Date)
            .Select(x => new { Date = x.Key, Count = x.Count() })
            .ToListAsync(cancellationToken);
        var exits = await db.ParkingSessions.AsNoTracking()
            .Where(x => x.ExitTime != null && x.ExitTime >= from && x.ExitTime < toExclusive)
            .GroupBy(x => x.ExitTime!.Value.Date)
            .Select(x => new { Date = x.Key, Count = x.Count() })
            .ToListAsync(cancellationToken);

        return Days(from, toExclusive)
            .Select(day => new DashboardChartPointDto
            {
                Label = day.ToString("dd MMM"),
                Value = entries.FirstOrDefault(x => x.Date == day)?.Count ?? 0,
                SecondaryValue = exits.FirstOrDefault(x => x.Date == day)?.Count ?? 0
            })
            .ToList();
    }

    private async Task<List<DashboardChartPointDto>> BuildRevenueTrendAsync(DateTime from, DateTime toExclusive, CancellationToken cancellationToken)
    {
        var rows = await db.ParkingPayments.AsNoTracking()
            .Where(x => x.PaymentDate >= from && x.PaymentDate < toExclusive)
            .GroupBy(x => x.PaymentDate.Date)
            .Select(x => new { Date = x.Key, Amount = x.Sum(p => p.Amount) })
            .ToListAsync(cancellationToken);

        return Days(from, toExclusive)
            .Select(day => new DashboardChartPointDto { Label = day.ToString("dd MMM"), Value = rows.FirstOrDefault(x => x.Date == day)?.Amount ?? 0 })
            .ToList();
    }

    private async Task<List<DashboardChartPointDto>> BuildCompanyRegistrationTrendAsync(DateTime from, DateTime toExclusive, CancellationToken cancellationToken)
    {
        var rows = await db.ParkingCompanies.AsNoTracking()
            .Where(x => x.CreatedDate >= from && x.CreatedDate < toExclusive)
            .GroupBy(x => x.CreatedDate.Date)
            .Select(x => new { Date = x.Key, Count = x.Count() })
            .ToListAsync(cancellationToken);

        return Days(from, toExclusive)
            .Select(day => new DashboardChartPointDto { Label = day.ToString("dd MMM"), Value = rows.FirstOrDefault(x => x.Date == day)?.Count ?? 0 })
            .ToList();
    }

    private async Task<List<DashboardChartPointDto>> BuildStatusChartAsync(IQueryable<string> query, CancellationToken cancellationToken)
    {
        return await query
            .GroupBy(x => string.IsNullOrWhiteSpace(x) ? "Unknown" : x)
            .Select(x => new DashboardChartPointDto { Label = x.Key, Value = x.Count() })
            .OrderByDescending(x => x.Value)
            .ToListAsync(cancellationToken);
    }

    private async Task<List<DashboardChartPointDto>> BuildPaymentModeChartAsync(DateTime from, DateTime toExclusive, CancellationToken cancellationToken)
    {
        return await db.ParkingPayments.AsNoTracking()
            .Where(x => x.PaymentDate >= from && x.PaymentDate < toExclusive)
            .GroupBy(x => string.IsNullOrWhiteSpace(x.PaymentMode) ? "Unknown" : x.PaymentMode)
            .Select(x => new DashboardChartPointDto { Label = x.Key, Value = x.Sum(p => p.Amount) })
            .OrderByDescending(x => x.Value)
            .ToListAsync(cancellationToken);
    }

    private async Task<List<DashboardCompanyMetricDto>> BuildTopUsageCompaniesAsync(DateTime from, DateTime toExclusive, CancellationToken cancellationToken)
    {
        return await db.ParkingSessions.AsNoTracking()
            .Where(x => x.EntryTime != null && x.EntryTime >= from && x.EntryTime < toExclusive)
            .GroupBy(x => new { x.CompanyId, x.Company.CompanyCode, x.Company.CompanyName, x.Company.Status })
            .Select(x => new DashboardCompanyMetricDto
            {
                CompanyId = x.Key.CompanyId,
                CompanyCode = x.Key.CompanyCode,
                CompanyName = x.Key.CompanyName,
                Status = x.Key.Status,
                Value = x.Count(),
                SecondaryValue = x.Count(s => s.Status == ParkingConstants.SessionStatus.Inside)
            })
            .OrderByDescending(x => x.Value)
            .Take(10)
            .ToListAsync(cancellationToken);
    }

    private async Task<List<DashboardCompanyMetricDto>> BuildTopPendingCompaniesAsync(CancellationToken cancellationToken)
    {
        return await db.ParkingInvoices.AsNoTracking()
            .Where(x => x.BalanceAmount > 0 && x.Status != "Cancelled")
            .GroupBy(x => new { x.CompanyId, x.Company.CompanyCode, x.Company.CompanyName, x.Company.Status })
            .Select(x => new DashboardCompanyMetricDto
            {
                CompanyId = x.Key.CompanyId,
                CompanyCode = x.Key.CompanyCode,
                CompanyName = x.Key.CompanyName,
                Status = x.Key.Status,
                Value = x.Sum(i => i.BalanceAmount),
                SecondaryValue = x.Count()
            })
            .OrderByDescending(x => x.Value)
            .Take(10)
            .ToListAsync(cancellationToken);
    }

    private static IEnumerable<DateTime> Days(DateTime from, DateTime toExclusive)
    {
        for (var day = from.Date; day < toExclusive.Date; day = day.AddDays(1))
            yield return day;
    }

    private static (DateTime From, DateTime To) ResolveRange(string? range, DateTime? dateFrom, DateTime? dateTo)
    {
        if (dateFrom.HasValue || dateTo.HasValue)
        {
            var from = (dateFrom ?? DateTime.Today).Date;
            var to = (dateTo ?? from).Date;
            return from <= to ? (from, to) : (to, from);
        }

        var today = DateTime.Today;
        return (range ?? string.Empty).Trim().ToLowerInvariant() switch
        {
            "today" => (today, today),
            "last7days" or "week" => (today.AddDays(-6), today),
            "last30days" => (today.AddDays(-29), today),
            "last6months" or "sixmonths" => (today.AddMonths(-6).AddDays(1), today),
            "thisyear" or "year" => (new DateTime(today.Year, 1, 1), today),
            _ => (new DateTime(today.Year, today.Month, 1), today)
        };
    }

    private static DashboardComparisonMetricDto BuildMetric(string key, string name, decimal currentValue, decimal previousValue, string valueType)
    {
        var changePercent = previousValue == 0
            ? currentValue == 0 ? 0 : 100
            : ((currentValue - previousValue) / previousValue) * 100;
        return new DashboardComparisonMetricDto
        {
            MetricKey = key,
            MetricName = name,
            CurrentValue = currentValue,
            PreviousValue = previousValue,
            ChangePercent = Math.Round(changePercent, 2),
            ValueType = valueType
        };
    }
}

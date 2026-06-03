using NetworldParkingLot.Api.Features.Dashboard.Dtos;

namespace NetworldParkingLot.Api.Features.Dashboard.Services;

public interface IDashboardService
{
    Task<DashboardSummaryDto> GetSummaryAsync(string? range, DateTime? dateFrom, DateTime? dateTo, CancellationToken cancellationToken = default);
}

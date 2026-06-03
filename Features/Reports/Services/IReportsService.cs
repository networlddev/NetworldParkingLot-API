using NetworldParkingLot.Api.Features.Reports.Dtos;

namespace NetworldParkingLot.Api.Features.Reports.Services;

public interface IReportsService
{
    IReadOnlyList<ReportCatalogItemDto> GetCatalog();
    Task<ReportResultDto> RunAsync(string reportKey, ReportQueryDto query, CancellationToken cancellationToken = default);
}

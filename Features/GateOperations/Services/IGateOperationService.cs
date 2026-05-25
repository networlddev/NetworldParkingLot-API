using NetworldParkingLot.Api.Features.GateOperations.Dtos;

namespace NetworldParkingLot.Api.Features.GateOperations.Services;

public interface IGateOperationService
{
    Task<GateSummaryDto> GetSummaryAsync(CancellationToken cancellationToken = default);
    Task<IReadOnlyList<CompanySearchDto>> SearchCompaniesAsync(string searchText, CancellationToken cancellationToken = default);
    Task<CompanyGateStatusDto> CheckCompanyAsync(CheckCompanyRequest request, CancellationToken cancellationToken = default);
    Task<GenerateBarcodeResponseDto> GenerateBarcodeAsync(GenerateBarcodeRequest request, CancellationToken cancellationToken = default);
    Task<EntryResultDto> AllowEntryAsync(AllowEntryRequest request, CancellationToken cancellationToken = default);
    Task<EntryResultDto> RejectEntryAsync(RejectEntryRequest request, CancellationToken cancellationToken = default);
    Task<ExitScanResultDto> ScanExitBarcodeAsync(ScanExitRequest request, CancellationToken cancellationToken = default);
    Task<ExitResultDto> AllowExitAsync(AllowExitRequest request, CancellationToken cancellationToken = default);
    Task<PaymentResultDto> CollectPaymentAsync(CollectPaymentRequest request, CancellationToken cancellationToken = default);
    Task<IReadOnlyList<ExtraSlotRateDto>> GetExtraSlotRatesAsync(CancellationToken cancellationToken = default);
    Task<ExtraSlotInvoiceResultDto> CreateExtraSlotInvoiceAsync(CreateExtraSlotInvoiceRequest request, CancellationToken cancellationToken = default);
    Task<IReadOnlyList<CompanyInvoiceDto>> GetCompanyInvoicesAsync(int companyId, CancellationToken cancellationToken = default);
    Task<IReadOnlyList<RecentActivityDto>> GetRecentActivityAsync(int take = 25, CancellationToken cancellationToken = default);
    Task<IReadOnlyList<LiveParkingDto>> GetLiveParkingAsync(string? searchText = null, CancellationToken cancellationToken = default);
    Task<OutsideDisplayDto?> GetLatestOutsideDisplayAsync(CancellationToken cancellationToken = default);

    Task<IReadOnlyList<OutsideDisplayDto>> GetRecentOutsideDisplayScansAsync(int take = 30, CancellationToken cancellationToken = default);
}

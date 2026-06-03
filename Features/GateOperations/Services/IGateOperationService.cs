using NetworldParkingLot.Api.Features.GateOperations.Dtos;

namespace NetworldParkingLot.Api.Features.GateOperations.Services;

public interface IGateOperationService
{
    Task<GateSummaryDto> GetSummaryAsync(CancellationToken cancellationToken = default);
    Task<IReadOnlyList<CompanySearchDto>> SearchCompaniesAsync(string searchText, CancellationToken cancellationToken = default);
    Task<PagedResultDto<CompanyListItemDto>> GetCompaniesAsync(CompanyListQueryRequest request, CancellationToken cancellationToken = default);
    Task<IReadOnlyList<CompanyListItemDto>> ExportCompaniesAsync(CompanyListQueryRequest request, CancellationToken cancellationToken = default);
    Task<CompanyListItemDto> CreateCompanyWithSubscriptionAsync(CreateCompanyWithSubscriptionRequest request, CancellationToken cancellationToken = default);
    Task<CompanyListItemDto> UpdateCompanyAsync(int companyId, UpdateCompanyRequest request, CancellationToken cancellationToken = default);
    Task<PagedSubscriptionResultDto> GetSubscriptionsAsync(SubscriptionListQueryRequest request, CancellationToken cancellationToken = default);
    Task<IReadOnlyList<SubscriptionListItemDto>> ExportSubscriptionsAsync(SubscriptionListQueryRequest request, CancellationToken cancellationToken = default);
    Task<SubscriptionListItemDto> GetSubscriptionByIdAsync(int subscriptionId, CancellationToken cancellationToken = default);
    Task<SubscriptionListItemDto> CreateSubscriptionAsync(CreateSubscriptionRequest request, CancellationToken cancellationToken = default);
    Task<SubscriptionListItemDto> UpdateSubscriptionAsync(int subscriptionId, UpdateSubscriptionRequest request, CancellationToken cancellationToken = default);
    Task<SubscriptionListItemDto> CancelSubscriptionAsync(int subscriptionId, CancelSubscriptionRequest request, CancellationToken cancellationToken = default);
    Task<SubscriptionListItemDto> RenewSubscriptionAsync(int subscriptionId, RenewSubscriptionRequest request, CancellationToken cancellationToken = default);
    Task<CompanyGateStatusDto> CheckCompanyAsync(CheckCompanyRequest request, CancellationToken cancellationToken = default);
    Task<GenerateBarcodeResponseDto> GenerateBarcodeAsync(GenerateBarcodeRequest request, CancellationToken cancellationToken = default);
    Task<EntryResultDto> AllowEntryAsync(AllowEntryRequest request, CancellationToken cancellationToken = default);
    Task<EntryResultDto> RejectEntryAsync(RejectEntryRequest request, CancellationToken cancellationToken = default);
    Task<ExitScanResultDto> ScanExitBarcodeAsync(ScanExitRequest request, CancellationToken cancellationToken = default);
    Task<ExitResultDto> AllowExitAsync(AllowExitRequest request, CancellationToken cancellationToken = default);
    Task<PaymentResultDto> CollectPaymentAsync(CollectPaymentRequest request, CancellationToken cancellationToken = default);
    Task<IReadOnlyList<ExtraSlotRateDto>> GetExtraSlotRatesAsync(CancellationToken cancellationToken = default);
    Task<ExtraSlotInvoiceResultDto> CreateExtraSlotInvoiceAsync(CreateExtraSlotInvoiceRequest request, CancellationToken cancellationToken = default);
    Task<PagedInvoiceResultDto> GetInvoicesAsync(InvoiceListQueryRequest request, CancellationToken cancellationToken = default);
    Task<IReadOnlyList<InvoiceListItemDto>> ExportInvoicesAsync(InvoiceListQueryRequest request, CancellationToken cancellationToken = default);
    Task<InvoicePaymentReconciliationResultDto> GetInvoicePaymentReconciliationAsync(InvoicePaymentReconciliationQueryRequest request, CancellationToken cancellationToken = default);
    Task<InvoiceListItemDto> GetInvoiceByIdAsync(int invoiceId, CancellationToken cancellationToken = default);
    Task<IReadOnlyList<InvoicePaymentDto>> GetInvoicePaymentsAsync(int invoiceId, CancellationToken cancellationToken = default);
    Task<InvoiceListItemDto> CreateInvoiceAsync(CreateInvoiceRequest request, CancellationToken cancellationToken = default);
    Task<InvoiceListItemDto> UpdateInvoiceAsync(int invoiceId, UpdateInvoiceRequest request, CancellationToken cancellationToken = default);
    Task<InvoiceListItemDto> CancelInvoiceAsync(int invoiceId, CancelInvoiceRequest request, CancellationToken cancellationToken = default);
    Task<IReadOnlyList<CompanyInvoiceDto>> GetCompanyInvoicesAsync(int companyId, CancellationToken cancellationToken = default);
    Task<IReadOnlyList<RecentActivityDto>> GetRecentActivityAsync(int take = 25, CancellationToken cancellationToken = default);
    Task<IReadOnlyList<LiveParkingDto>> GetLiveParkingAsync(string? searchText = null, CancellationToken cancellationToken = default);
    Task<PagedLiveParkingResultDto> GetLiveParkingPagedAsync(LiveParkingListQueryRequest request, CancellationToken cancellationToken = default);
    Task<IReadOnlyList<LiveParkingListItemDto>> ExportLiveParkingAsync(LiveParkingListQueryRequest request, CancellationToken cancellationToken = default);
    Task<VehicleBarcodeDetailDto> GetVehicleBarcodeDetailAsync(int sessionId, CancellationToken cancellationToken = default);
    Task<PagedPaymentResultDto> GetPaymentsAsync(PaymentListQueryRequest request, CancellationToken cancellationToken = default);
    Task<IReadOnlyList<PaymentListItemDto>> ExportPaymentsAsync(PaymentListQueryRequest request, CancellationToken cancellationToken = default);
    Task<PaymentListItemDto> GetPaymentByIdAsync(int paymentId, CancellationToken cancellationToken = default);
    Task<PagedVehicleBarcodeResultDto> GetVehicleBarcodesAsync(VehicleBarcodeListQueryRequest request, CancellationToken cancellationToken = default);
    Task<IReadOnlyList<VehicleBarcodeListItemDto>> ExportVehicleBarcodesAsync(VehicleBarcodeListQueryRequest request, CancellationToken cancellationToken = default);
    Task<VehicleBarcodeDetailDto> MarkVehicleBarcodeInvalidAsync(int sessionId, MarkBarcodeInvalidRequest request, CancellationToken cancellationToken = default);
    Task<OutsideDisplayDto?> GetLatestOutsideDisplayAsync(CancellationToken cancellationToken = default);

    Task<IReadOnlyList<OutsideDisplayDto>> GetRecentOutsideDisplayScansAsync(int take = 30, CancellationToken cancellationToken = default);
}

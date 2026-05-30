namespace NetworldParkingLot.Api.Features.GateOperations.Dtos;

public sealed record GateSummaryDto(
    int TotalCapacity,
    int VehiclesInside,
    int AvailableSpaces,
    int TodayEntries,
    int TodayExits,
    int PendingExitPayments,
    int OverstayVehicles);

public sealed record CompanySearchDto(
    int CompanyId,
    string CompanyCode,
    string CompanyName,
    string? ContactPerson,
    string? Mobile,
    string Status,
    int PurchasedSlots,
    int VehiclesInside,
    int AvailableSlots,
    decimal PendingAmount,
    string PaymentStatus,
    string SubscriptionStatus,
    DateTime? SubscriptionStartDate,
    DateTime? SubscriptionEndDate);

public sealed record CompanyGateStatusDto(
    int CompanyId,
    string CompanyCode,
    string CompanyName,
    string? ContactPerson,
    string? Mobile,
    string CompanyStatus,
    int PurchasedSlots,
    int VehiclesInside,
    int AvailableSlots,
    decimal PendingAmount,
    string PaymentStatus,
    string SubscriptionStatus,
    DateTime? SubscriptionStartDate,
    DateTime? SubscriptionEndDate,
    string EntryStatus,
    bool CanGenerateBarcode,
    string Message);

public sealed record GenerateBarcodeResponseDto(
    int SessionId,
    string BarcodeNo,
    string CompanyName,
    string? PlateNo,
    string VehicleType,
    DateTime GeneratedDate,
    string Status,
    string BarcodeStatus);

public sealed record EntryResultDto(
    int SessionId,
    string BarcodeNo,
    string? PlateNo,
    string CompanyName,
    DateTime EntryTime,
    string Status,
    string Message);

public sealed record ExitScanResultDto(
    bool IsValid,
    int? SessionId,
    int CompanyId,
    string? BarcodeNo,
    string? PlateNo,
    string? CompanyName,
    DateTime? EntryTime,
    DateTime CurrentTime,
    string? TotalStay,
    DateTime? SubscriptionValidUntil,
    string PaymentStatus,
    decimal PendingCompanyAmount,
    int OverstayDays,
    decimal OverstayAmount,
    decimal TotalPayable,
    string FinalStatus,
    string Message);

public sealed record ExitResultDto(
    int SessionId,
    string BarcodeNo,
    string? PlateNo,
    string CompanyName,
    DateTime ExitTime,
    string BarcodeStatus,
    string Status,
    string Message);

public sealed record PaymentResultDto(
    int PaymentId,
    string ReceiptNo,
    int CompanyId,
    decimal Amount,
    string PaymentMode,
    decimal RemainingPendingAmount,
    string Message);

public sealed record ExtraSlotInvoiceResultDto(
    int SubscriptionId,
    int InvoiceId,
    string InvoiceNo,
    int CompanyId,
    int AdditionalSlots,
    string PlanType,
    decimal TotalAmount,
    decimal BalanceAmount,
    string Status,
    string Message);

public sealed record ExtraSlotRateDto(
    string PlanType,
    decimal RatePerSlot,
    int DurationDays,
    string DisplayName);

public sealed record CompanyInvoiceDto(
    int InvoiceId,
    string InvoiceNo,
    int CompanyId,
    string CompanyName,
    string InvoiceType,
    DateTime InvoiceDate,
    DateTime? DueDate,
    string? PlanType,
    int Slots,
    decimal SubTotal,
    decimal DiscountAmount,
    decimal VatAmount,
    decimal TotalAmount,
    decimal PaidAmount,
    decimal BalanceAmount,
    string Status,
    string? Remarks);



public sealed record InvoiceListItemDto(
    int InvoiceId,
    string InvoiceNo,
    int CompanyId,
    string CompanyCode,
    string CompanyName,
    string InvoiceType,
    DateTime InvoiceDate,
    DateTime? DueDate,
    string? PlanType,
    int Slots,
    decimal SubTotal,
    decimal DiscountAmount,
    decimal VatAmount,
    decimal TotalAmount,
    decimal PaidAmount,
    decimal BalanceAmount,
    string Status,
    int? SubscriptionId,
    int? SessionId,
    DateTime CreatedDate,
    DateTime? ModifiedDate,
    string? Remarks,
    string? CancellationReason);

public sealed record PagedInvoiceResultDto(
    IReadOnlyList<InvoiceListItemDto> Items,
    int Page,
    int PageSize,
    int TotalCount,
    int TotalPages,
    decimal TotalAmount,
    decimal TotalPaidAmount,
    decimal TotalPendingAmount,
    int PaidCount,
    int PartialCount,
    int UnpaidCount,
    int OverdueCount,
    int CancelledCount);

public sealed record InvoicePaymentDto(
    int PaymentId,
    string ReceiptNo,
    int CompanyId,
    int? InvoiceId,
    string CompanyName,
    decimal Amount,
    string PaymentMode,
    string? ReferenceNo,
    DateTime PaymentDate,
    string? Remarks,
    int ReceivedBy);

public sealed record RecentActivityDto(
    DateTime Time,
    string Type,
    string? PlateNumber,
    string? CompanyName,
    string? BarcodeNumber,
    string Status,
    string? Message,
    int OperatorId);

public sealed record LiveParkingDto(
    int SessionId,
    string BarcodeNo,
    string? PlateNo,
    string CompanyName,
    DateTime EntryTime,
    string DurationInside,
    DateTime? ValidUntil,
    string Status,
    decimal PendingAmount);

public sealed record OutsideDisplayDto(
    long DisplayEventId,
    string? BarcodeNo,
    string? PlateNo,
    string? CompanyName,
    string DisplayStatus,
    string MainMessage,
    string? SubMessage,
    decimal AmountDue,
    int OverstayDays,
    DateTime CreatedDate);

public sealed record PagedResultDto<T>(
    IReadOnlyList<T> Items,
    int Page,
    int PageSize,
    int TotalCount,
    int TotalPages,
    decimal TotalPendingAmount,
    int TotalPurchasedSlots,
    int TotalVehiclesInside,
    int TotalAvailableSlots);

public sealed record CompanyListItemDto(
    int CompanyId,
    string CompanyCode,
    string CompanyName,
    string? ContactPerson,
    string? Mobile,
    string? Email,
    string? Address,
    string? TradeLicenseNo,
    string? Trn,
    string Status,
    decimal OpeningBalance,
    string? BillingName,
    string? PaymentTerms,
    decimal CreditLimit,
    string? Remarks,
    string? InternalNotes,
    int PurchasedSlots,
    int VehiclesInside,
    int AvailableSlots,
    decimal PendingAmount,
    string PaymentStatus,
    string SubscriptionStatus,
    string? SubscriptionType,
    DateTime? SubscriptionStartDate,
    DateTime? SubscriptionEndDate,
    DateTime? LastInvoiceDate,
    DateTime CreatedDate);


public sealed record SubscriptionListItemDto(
    int SubscriptionId,
    int CompanyId,
    string CompanyCode,
    string CompanyName,
    string PlanType,
    int SlotsPurchased,
    decimal RatePerSlot,
    DateTime StartDate,
    DateTime EndDate,
    int DurationDays,
    decimal DiscountAmount,
    decimal VatAmount,
    decimal TotalAmount,
    decimal PaidAmount,
    decimal BalanceAmount,
    string PaymentStatus,
    string Status,
    bool IsExtraSlot,
    int? InvoiceId,
    string? InvoiceNo,
    string? InvoiceStatus,
    int VehiclesInside,
    DateTime CreatedDate,
    string? Remarks,
    string? CancellationReason);

public sealed record PagedSubscriptionResultDto(
    IReadOnlyList<SubscriptionListItemDto> Items,
    int Page,
    int PageSize,
    int TotalCount,
    int TotalPages,
    decimal TotalAmount,
    decimal TotalPendingAmount,
    int TotalSlots,
    int ActiveSlots,
    int ExpiredCount,
    int ExpiringSoonCount);

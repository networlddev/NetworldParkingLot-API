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

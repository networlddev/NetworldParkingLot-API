using System.ComponentModel.DataAnnotations;

namespace NetworldParkingLot.Api.Features.GateOperations.Dtos;

public sealed class CheckCompanyRequest
{
    [Required]
    public string SearchText { get; set; } = string.Empty;

    public int OperatorId { get; set; }
}

public sealed class GenerateBarcodeRequest
{
    [Range(1, int.MaxValue)]
    public int CompanyId { get; set; }

    // Vehicle plate is optional because many vehicles may not have plates.
    [MaxLength(30)]
    public string? PlateNo { get; set; }

    [MaxLength(30)]
    public string VehicleType { get; set; } = "Car";

    [MaxLength(100)]
    public string? DriverName { get; set; }

    [MaxLength(30)]
    public string? DriverMobile { get; set; }

    [MaxLength(500)]
    public string? Remarks { get; set; }

    public bool AllowPaymentDueWarning { get; set; }

    public int OperatorId { get; set; }
}

public sealed class UpdateGeneratedBarcodeDetailsRequest
{
    [Range(1, int.MaxValue)]
    public int SessionId { get; set; }

    [MaxLength(30)]
    public string? PlateNo { get; set; }

    [MaxLength(30)]
    public string VehicleType { get; set; } = "Car";

    [MaxLength(100)]
    public string? DriverName { get; set; }

    [MaxLength(30)]
    public string? DriverMobile { get; set; }

    [MaxLength(500)]
    public string? Remarks { get; set; }

    public int OperatorId { get; set; }
}

public sealed class AllowEntryRequest
{
    [Range(1, int.MaxValue)]
    public int SessionId { get; set; }

    public bool AllowPaymentDueWarning { get; set; }

    public int OperatorId { get; set; }
}

public sealed class RejectEntryRequest
{
    [Range(1, int.MaxValue)]
    public int SessionId { get; set; }

    public int OperatorId { get; set; }

    public string? Reason { get; set; }
}

public sealed class ScanExitRequest
{
    [Required]
    public string BarcodeNo { get; set; } = string.Empty;

    public int OperatorId { get; set; }

    public bool SaveDisplayEvent { get; set; } = true;
}

public sealed class AllowExitRequest
{
    public int? SessionId { get; set; }
    public string? BarcodeNo { get; set; }
    public bool ForceAllow { get; set; }
    public string? ForceReason { get; set; }

    public int OperatorId { get; set; }
}

public sealed class CollectPaymentRequest
{
    [Range(1, int.MaxValue)]
    public int CompanyId { get; set; }

    public int? InvoiceId { get; set; }
    public int? SessionId { get; set; }

    [Range(0.01, double.MaxValue)]
    public decimal Amount { get; set; }

    [Required]
    public string PaymentMode { get; set; } = "Cash";

    public int? BankAccountId { get; set; }
    public string? ReferenceNo { get; set; }
    public string? Remarks { get; set; }
    public bool SavePaymentAndAllowExit { get; set; }

    public int OperatorId { get; set; }
}

public sealed class CreateExtraSlotInvoiceRequest
{
    [Range(1, int.MaxValue)]
    public int CompanyId { get; set; }

    [Range(1, 10000)]
    public int AdditionalSlots { get; set; }

    [Required]
    public string PlanType { get; set; } = "Daily";

    public int? RatePlanId { get; set; }
    public int? VehicleTypeId { get; set; }

    [Range(0, double.MaxValue)]
    public decimal RatePerSlot { get; set; }

    public DateTime StartDate { get; set; } = DateTime.Today;
    public DateTime EndDate { get; set; } = DateTime.Today.AddDays(1);
    public decimal DiscountAmount { get; set; }
    public decimal VatAmount { get; set; }
    public decimal? VatPercent { get; set; }
    public string? VatMode { get; set; }
    public decimal PaidAmount { get; set; }
    public string PaymentMode { get; set; } = "Cash";
    public int? BankAccountId { get; set; }
    public string? ReferenceNo { get; set; }
    public string? Remarks { get; set; }

    public int OperatorId { get; set; }
}

public sealed class CompanyListQueryRequest
{
    public string? SearchText { get; set; }
    public string? Tab { get; set; } = "All";
    public string? Status { get; set; }
    public string? PaymentStatus { get; set; }
    public string? SubscriptionType { get; set; }
    public DateTime? CreatedFrom { get; set; }
    public DateTime? CreatedTo { get; set; }
    public string? SortBy { get; set; } = "CompanyName";
    public string? SortDirection { get; set; } = "Asc";
    public int Page { get; set; } = 1;
    public int PageSize { get; set; } = 25;
}

public sealed class CreateCompanyWithSubscriptionRequest
{
    public string? CompanyCode { get; set; }

    [Required]
    [MaxLength(250)]
    public string CompanyName { get; set; } = string.Empty;

    [MaxLength(150)]
    public string? ContactPerson { get; set; }

    [MaxLength(50)]
    public string? Mobile { get; set; }

    [MaxLength(150)]
    public string? Email { get; set; }

    [MaxLength(500)]
    public string? Address { get; set; }

    [MaxLength(100)]
    public string? TradeLicenseNo { get; set; }

    [MaxLength(100)]
    public string? Trn { get; set; }

    public string Status { get; set; } = "Active";
    public bool AutoRenewSubscriptions { get; set; } = true;
    public decimal OpeningBalance { get; set; }
    public string? BillingName { get; set; }
    public string? PaymentTerms { get; set; }
    public decimal CreditLimit { get; set; }
    public string? Remarks { get; set; }
    public string? InternalNotes { get; set; }

    [Required]
    public string PlanType { get; set; } = "Monthly";

    public int? RatePlanId { get; set; }
    public int? VehicleTypeId { get; set; }
    public List<SubscriptionVehicleTypeAllocationRequest> VehicleTypeAllocations { get; set; } = [];

    [Range(1, 100000)]
    public int SlotsPurchased { get; set; }

    [Range(0, double.MaxValue)]
    public decimal RatePerSlot { get; set; }

    public bool AutoRenew { get; set; } = true;
    public DateTime StartDate { get; set; } = DateTime.Today;
    public decimal DiscountAmount { get; set; }
    public decimal VatAmount { get; set; }
    public decimal? VatPercent { get; set; }
    public string? VatMode { get; set; }
    public decimal PaidAmount { get; set; }
    public string PaymentMode { get; set; } = "Cash";
    public int? BankAccountId { get; set; }
    public string? ReferenceNo { get; set; }

    public int OperatorId { get; set; }
}


public sealed class UpdateCompanyRequest
{
    [Required]
    [MaxLength(250)]
    public string CompanyName { get; set; } = string.Empty;

    [MaxLength(150)]
    public string? ContactPerson { get; set; }

    [MaxLength(50)]
    public string? Mobile { get; set; }

    [MaxLength(150)]
    public string? Email { get; set; }

    [MaxLength(500)]
    public string? Address { get; set; }

    [MaxLength(100)]
    public string? TradeLicenseNo { get; set; }

    [MaxLength(100)]
    public string? Trn { get; set; }

    public string Status { get; set; } = "Active";
    public bool AutoRenewSubscriptions { get; set; } = true;
    public decimal OpeningBalance { get; set; }
    public string? BillingName { get; set; }
    public string? PaymentTerms { get; set; }
    public decimal CreditLimit { get; set; }
    public string? Remarks { get; set; }
    public string? InternalNotes { get; set; }

    public int OperatorId { get; set; }
}


public sealed class SubscriptionListQueryRequest
{
    public string? SearchText { get; set; }
    public string? Tab { get; set; } = "All";
    public int? CompanyId { get; set; }
    public string? PlanType { get; set; }
    public string? Status { get; set; }
    public string? PaymentStatus { get; set; }
    public bool? IsExtraSlot { get; set; }
    public DateTime? StartFrom { get; set; }
    public DateTime? StartTo { get; set; }
    public DateTime? EndFrom { get; set; }
    public DateTime? EndTo { get; set; }
    public string? SortBy { get; set; } = "EndDate";
    public string? SortDirection { get; set; } = "Desc";
    public int Page { get; set; } = 1;
    public int PageSize { get; set; } = 25;
}

public sealed class CreateSubscriptionRequest
{
    [Range(1, int.MaxValue)]
    public int CompanyId { get; set; }

    [Required]
    public string PlanType { get; set; } = "Monthly";

    public int? RatePlanId { get; set; }
    public int? VehicleTypeId { get; set; }
    public List<SubscriptionVehicleTypeAllocationRequest> VehicleTypeAllocations { get; set; } = [];

    [Range(1, 100000)]
    public int SlotsPurchased { get; set; }

    public bool IsExtraSlot { get; set; }
    public DateTime StartDate { get; set; } = DateTime.Today;
    public DateTime? EndDate { get; set; }
    public decimal RatePerSlot { get; set; }
    public bool AutoRenew { get; set; } = true;
    public decimal DiscountAmount { get; set; }
    public decimal VatAmount { get; set; }
    public decimal? VatPercent { get; set; }
    public string? VatMode { get; set; }
    public decimal PaidAmount { get; set; }
    public string PaymentMode { get; set; } = "Cash";
    public int? BankAccountId { get; set; }
    public string? ReferenceNo { get; set; }
    public string? Remarks { get; set; }

    public int OperatorId { get; set; }
}

public sealed class UpdateSubscriptionRequest
{
    [Required]
    public string PlanType { get; set; } = "Monthly";

    public int? RatePlanId { get; set; }
    public int? VehicleTypeId { get; set; }
    public List<SubscriptionVehicleTypeAllocationRequest> VehicleTypeAllocations { get; set; } = [];

    [Range(1, 100000)]
    public int SlotsPurchased { get; set; }

    public bool IsExtraSlot { get; set; }
    public DateTime StartDate { get; set; } = DateTime.Today;
    public DateTime? EndDate { get; set; }
    public decimal RatePerSlot { get; set; }
    public bool AutoRenew { get; set; } = true;
    public decimal DiscountAmount { get; set; }
    public decimal VatAmount { get; set; }
    public decimal? VatPercent { get; set; }
    public string? VatMode { get; set; }
    public string Status { get; set; } = "Active";
    public string? Remarks { get; set; }

    public int OperatorId { get; set; }
}

public sealed class CancelSubscriptionRequest
{
    public string? Reason { get; set; }
    public bool ClearPendingInvoiceBalance { get; set; }
    public bool StopAutoRenew { get; set; }

    public int OperatorId { get; set; }
}

public sealed class SubscriptionVehicleTypeAllocationRequest
{
    [Range(1, int.MaxValue)]
    public int VehicleTypeId { get; set; }

    [Range(1, 100000)]
    public int SlotsPurchased { get; set; }

    [Range(0, double.MaxValue)]
    public decimal RatePerSlot { get; set; }
}

public sealed class ClearCancelledSubscriptionBalanceRequest
{
    [Required]
    public string Reason { get; set; } = string.Empty;

    public int OperatorId { get; set; }
}

public sealed class RenewSubscriptionRequest
{
    public string? PlanType { get; set; }
    public int? RatePlanId { get; set; }
    public int? VehicleTypeId { get; set; }
    public List<SubscriptionVehicleTypeAllocationRequest> VehicleTypeAllocations { get; set; } = [];
    public int? SlotsPurchased { get; set; }
    public DateTime? StartDate { get; set; }
    public DateTime? EndDate { get; set; }
    public decimal? RatePerSlot { get; set; }
    public bool? AutoRenew { get; set; }
    public decimal DiscountAmount { get; set; }
    public decimal VatAmount { get; set; }
    public decimal? VatPercent { get; set; }
    public string? VatMode { get; set; }
    public decimal PaidAmount { get; set; }
    public string PaymentMode { get; set; } = "Cash";
    public int? BankAccountId { get; set; }
    public string? ReferenceNo { get; set; }
    public string? Remarks { get; set; }

    public int OperatorId { get; set; }
}

public sealed class ReduceSubscriptionSlotsRequest
{
    [Range(1, 100000)]
    public int NewSlotsPurchased { get; set; }

    public DateTime? EffectiveDate { get; set; }
    public string? Reason { get; set; }
    public int OperatorId { get; set; }
}




public sealed class LiveParkingListQueryRequest
{
    public string? SearchText { get; set; }
    public string? Tab { get; set; } = "Inside";
    public int? CompanyId { get; set; }
    public string? Status { get; set; }
    public string? PaymentStatus { get; set; }
    public bool? OverstayOnly { get; set; }
    public DateTime? EntryFrom { get; set; }
    public DateTime? EntryTo { get; set; }
    public string? SortBy { get; set; } = "EntryTime";
    public string? SortDirection { get; set; } = "Desc";
    public int Page { get; set; } = 1;
    public int PageSize { get; set; } = 25;
}

public sealed class PaymentListQueryRequest
{
    public string? SearchText { get; set; }
    public string? Tab { get; set; } = "All";
    public int? CompanyId { get; set; }
    public int? PaymentId { get; set; }
    public int? InvoiceId { get; set; }
    public int? SessionId { get; set; }
    public string? PaymentMode { get; set; }
    public string? PaymentType { get; set; }
    public DateTime? DateFrom { get; set; }
    public DateTime? DateTo { get; set; }
    public string? SortBy { get; set; } = "PaymentDate";
    public string? SortDirection { get; set; } = "Desc";
    public int Page { get; set; } = 1;
    public int PageSize { get; set; } = 25;
}

public sealed class InvoicePaymentReconciliationQueryRequest
{
    public string? SearchText { get; set; }
    public int? CompanyId { get; set; }
    public string? InvoiceType { get; set; }
    public bool MismatchOnly { get; set; } = true;
    public bool IncludeCancelled { get; set; }
    public DateTime? InvoiceFrom { get; set; }
    public DateTime? InvoiceTo { get; set; }
    public int Page { get; set; } = 1;
    public int PageSize { get; set; } = 25;
}

public sealed class VehicleBarcodeListQueryRequest
{
    public string? SearchText { get; set; }
    public string? Tab { get; set; } = "All";
    public int? CompanyId { get; set; }
    public string? Status { get; set; }
    public string? BarcodeStatus { get; set; }
    public string? VehicleType { get; set; }
    public DateTime? EntryFrom { get; set; }
    public DateTime? EntryTo { get; set; }
    public DateTime? ExitFrom { get; set; }
    public DateTime? ExitTo { get; set; }
    public DateTime? CreatedFrom { get; set; }
    public DateTime? CreatedTo { get; set; }
    public string? SortBy { get; set; } = "CreatedDate";
    public string? SortDirection { get; set; } = "Desc";
    public int Page { get; set; } = 1;
    public int PageSize { get; set; } = 25;
}

public sealed class MarkBarcodeInvalidRequest
{
    [Required]
    [MaxLength(500)]
    public string Reason { get; set; } = string.Empty;

    public int OperatorId { get; set; }
}

public sealed class InvoiceListQueryRequest
{
    public string? SearchText { get; set; }
    public string? Tab { get; set; } = "All";
    public int? CompanyId { get; set; }
    public string? InvoiceType { get; set; }
    public string? Status { get; set; }
    public DateTime? InvoiceFrom { get; set; }
    public DateTime? InvoiceTo { get; set; }
    public DateTime? DueFrom { get; set; }
    public DateTime? DueTo { get; set; }
    public string? SortBy { get; set; } = "InvoiceDate";
    public string? SortDirection { get; set; } = "Desc";
    public int Page { get; set; } = 1;
    public int PageSize { get; set; } = 25;
}

public sealed class CreateInvoiceRequest
{
    [Range(1, int.MaxValue)]
    public int CompanyId { get; set; }

    [MaxLength(50)]
    public string? InvoiceNo { get; set; }

    [Required]
    public string InvoiceType { get; set; } = "Manual";

    public DateTime InvoiceDate { get; set; } = DateTime.Now;
    public DateTime? DueDate { get; set; }

    [MaxLength(30)]
    public string? PlanType { get; set; }

    [Range(0, 100000)]
    public int Slots { get; set; }

    [Range(0, double.MaxValue)]
    public decimal SubTotal { get; set; }

    [Range(0, double.MaxValue)]
    public decimal DiscountAmount { get; set; }

    [Range(0, double.MaxValue)]
    public decimal VatAmount { get; set; }

    public decimal? VatPercent { get; set; }
    public string? VatMode { get; set; }

    [Range(0, double.MaxValue)]
    public decimal TotalAmount { get; set; }

    [Range(0, double.MaxValue)]
    public decimal PaidAmount { get; set; }

    public string PaymentMode { get; set; } = "Cash";
    public int? BankAccountId { get; set; }
    public string? ReferenceNo { get; set; }
    public string? Remarks { get; set; }

    public int OperatorId { get; set; }
}

public sealed class UpdateInvoiceRequest
{
    [Required]
    public string InvoiceType { get; set; } = "Manual";

    public DateTime InvoiceDate { get; set; } = DateTime.Now;
    public DateTime? DueDate { get; set; }
    public string? PlanType { get; set; }

    [Range(0, 100000)]
    public int Slots { get; set; }

    [Range(0, double.MaxValue)]
    public decimal SubTotal { get; set; }

    [Range(0, double.MaxValue)]
    public decimal DiscountAmount { get; set; }

    [Range(0, double.MaxValue)]
    public decimal VatAmount { get; set; }

    [Range(0, double.MaxValue)]
    public decimal TotalAmount { get; set; }

    public string? Remarks { get; set; }

    public int OperatorId { get; set; }
}

public sealed class CancelInvoiceRequest
{
    public string? Reason { get; set; }
    public bool ClearPendingBalance { get; set; } = true;

    public int OperatorId { get; set; }
}

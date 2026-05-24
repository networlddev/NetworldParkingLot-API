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

    [Range(1, int.MaxValue)]
    public int OperatorId { get; set; }
}

public sealed class AllowEntryRequest
{
    [Range(1, int.MaxValue)]
    public int SessionId { get; set; }

    public bool AllowPaymentDueWarning { get; set; }

    [Range(1, int.MaxValue)]
    public int OperatorId { get; set; }
}

public sealed class RejectEntryRequest
{
    [Range(1, int.MaxValue)]
    public int SessionId { get; set; }

    [Range(1, int.MaxValue)]
    public int OperatorId { get; set; }

    public string? Reason { get; set; }
}

public sealed class ScanExitRequest
{
    [Required]
    public string BarcodeNo { get; set; } = string.Empty;

    [Range(1, int.MaxValue)]
    public int OperatorId { get; set; }
}

public sealed class AllowExitRequest
{
    public int? SessionId { get; set; }
    public string? BarcodeNo { get; set; }
    public bool ForceAllow { get; set; }
    public string? ForceReason { get; set; }

    [Range(1, int.MaxValue)]
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

    public string? ReferenceNo { get; set; }
    public string? Remarks { get; set; }
    public bool SavePaymentAndAllowExit { get; set; }

    [Range(1, int.MaxValue)]
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

    [Range(0, double.MaxValue)]
    public decimal RatePerSlot { get; set; }

    public DateTime StartDate { get; set; } = DateTime.Today;
    public DateTime EndDate { get; set; } = DateTime.Today.AddDays(1);
    public decimal DiscountAmount { get; set; }
    public decimal VatAmount { get; set; }
    public decimal PaidAmount { get; set; }
    public string PaymentMode { get; set; } = "Cash";
    public string? ReferenceNo { get; set; }
    public string? Remarks { get; set; }

    [Range(1, int.MaxValue)]
    public int OperatorId { get; set; }
}

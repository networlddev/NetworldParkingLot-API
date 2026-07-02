namespace NetworldParkingLot.Api.Domain.Entities;

public sealed class ParkingSubscription
{
    public int SubscriptionId { get; set; }
    public int CompanyId { get; set; }
    public string PlanType { get; set; } = "Monthly";
    public int SlotsPurchased { get; set; }
    public decimal RatePerSlot { get; set; }
    public DateTime StartDate { get; set; }
    public DateTime EndDate { get; set; }
    public decimal DiscountAmount { get; set; }
    public decimal VatAmount { get; set; }
    public decimal TotalAmount { get; set; }
    public decimal PaidAmount { get; set; }
    public decimal BalanceAmount { get; set; }
    public string Status { get; set; } = "Active";
    public bool IsExtraSlot { get; set; }
    public bool AutoRenew { get; set; } = true;
    public int? SourceInvoiceId { get; set; }
    public string? Remarks { get; set; }
    public DateTime CreatedDate { get; set; } = DateTime.Now;
    public int? CreatedBy { get; set; }
    public DateTime? ModifiedDate { get; set; }
    public int? ModifiedBy { get; set; }
    public DateTime? CancelledDate { get; set; }
    public int? CancelledBy { get; set; }
    public string? CancellationReason { get; set; }
    public DateTime? StoppedDate { get; set; }
    public int? StoppedBy { get; set; }
    public string? StopReason { get; set; }

    public ParkingCompany Company { get; set; } = default!;
}

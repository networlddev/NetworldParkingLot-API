namespace NetworldParkingLot.Api.Domain.Entities;

public sealed class ParkingInvoice
{
    public int InvoiceId { get; set; }
    public string InvoiceNo { get; set; } = string.Empty;
    public int CompanyId { get; set; }
    public int? SubscriptionId { get; set; }
    public int? SessionId { get; set; }
    public string InvoiceType { get; set; } = "Subscription";
    public DateTime InvoiceDate { get; set; } = DateTime.Now;
    public DateTime? DueDate { get; set; }
    public string? PlanType { get; set; }
    public int Slots { get; set; }
    public decimal SubTotal { get; set; }
    public decimal DiscountAmount { get; set; }
    public decimal VatAmount { get; set; }
    public decimal TotalAmount { get; set; }
    public decimal PaidAmount { get; set; }
    public decimal BalanceAmount { get; set; }
    public string Status { get; set; } = "Unpaid";
    public string? Remarks { get; set; }
    public DateTime CreatedDate { get; set; } = DateTime.Now;
    public int? CreatedBy { get; set; }
    public DateTime? ModifiedDate { get; set; }
    public int? ModifiedBy { get; set; }
    public DateTime? CancelledDate { get; set; }
    public int? CancelledBy { get; set; }
    public string? CancellationReason { get; set; }

    public ParkingCompany Company { get; set; } = default!;
}

namespace NetworldParkingLot.Api.Domain.Entities;

public sealed class ParkingPayment
{
    public int PaymentId { get; set; }
    public string ReceiptNo { get; set; } = string.Empty;
    public int CompanyId { get; set; }
    public int? InvoiceId { get; set; }
    public int? SessionId { get; set; }
    public string PaymentType { get; set; } = "Invoice";
    public decimal Amount { get; set; }
    public string PaymentMode { get; set; } = "Cash";
    public string? ReferenceNo { get; set; }
    public int ReceivedBy { get; set; }
    public DateTime PaymentDate { get; set; } = DateTime.Now;
    public string? Remarks { get; set; }

    public ParkingCompany Company { get; set; } = default!;
}

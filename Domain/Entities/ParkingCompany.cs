namespace NetworldParkingLot.Api.Domain.Entities;

public sealed class ParkingCompany
{
    public int CompanyId { get; set; }
    public string CompanyCode { get; set; } = string.Empty;
    public string CompanyName { get; set; } = string.Empty;
    public string? ContactPerson { get; set; }
    public string? Mobile { get; set; }
    public string? TradeLicenseNo { get; set; }
    public string? Trn { get; set; }
    public string? Email { get; set; }
    public string? Address { get; set; }
    public string Status { get; set; } = "Active";
    public decimal OpeningBalance { get; set; }
    public bool AutoRenewSubscriptions { get; set; } = true;
    public string? BillingName { get; set; }
    public string? PaymentTerms { get; set; }
    public decimal CreditLimit { get; set; }
    public string? Remarks { get; set; }
    public string? InternalNotes { get; set; }
    public DateTime CreatedDate { get; set; } = DateTime.Now;
    public int? CreatedBy { get; set; }
    public DateTime? ModifiedDate { get; set; }
    public int? ModifiedBy { get; set; }

    public ICollection<ParkingSubscription> Subscriptions { get; set; } = [];
    public ICollection<ParkingInvoice> Invoices { get; set; } = [];
    public ICollection<ParkingSession> ParkingSessions { get; set; } = [];
    public ICollection<ParkingCompanyBalanceAdjustment> BalanceAdjustments { get; set; } = [];
}

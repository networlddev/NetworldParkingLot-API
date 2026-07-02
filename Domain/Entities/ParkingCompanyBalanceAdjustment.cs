namespace NetworldParkingLot.Api.Domain.Entities;

public sealed class ParkingCompanyBalanceAdjustment
{
    public int BalanceAdjustmentId { get; set; }
    public int CompanyId { get; set; }
    public int? SubscriptionId { get; set; }
    public int? InvoiceId { get; set; }
    public string AdjustmentType { get; set; } = "Credit";
    public decimal Amount { get; set; }
    public decimal AppliedAmount { get; set; }
    public decimal RemainingAmount { get; set; }
    public string Reason { get; set; } = string.Empty;
    public int? OldSlots { get; set; }
    public int? NewSlots { get; set; }
    public DateTime? EffectiveDate { get; set; }
    public DateTime CreatedDate { get; set; } = DateTime.Now;
    public int? CreatedBy { get; set; }

    public ParkingCompany Company { get; set; } = default!;
}

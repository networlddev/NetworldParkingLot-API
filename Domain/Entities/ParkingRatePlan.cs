namespace NetworldParkingLot.Api.Domain.Entities;

public sealed class ParkingRatePlan
{
    public int RatePlanId { get; set; }
    public string PlanName { get; set; } = string.Empty;
    public string PeriodUnit { get; set; } = "Days";
    public int PeriodValue { get; set; }
    public decimal RatePerSlot { get; set; }
    public bool IsSystemDefault { get; set; }
    public bool IsActive { get; set; } = true;
    public int SortOrder { get; set; }
    public string? Remarks { get; set; }
    public DateTime CreatedDate { get; set; } = DateTime.Now;
    public int? CreatedBy { get; set; }
    public DateTime? ModifiedDate { get; set; }
    public int? ModifiedBy { get; set; }
}

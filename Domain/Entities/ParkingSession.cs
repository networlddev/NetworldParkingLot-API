namespace NetworldParkingLot.Api.Domain.Entities;

public sealed class ParkingSession
{
    public int SessionId { get; set; }
    public string BarcodeNo { get; set; } = string.Empty;
    public int CompanyId { get; set; }
    public int? SubscriptionId { get; set; }
    public string? PlateNo { get; set; }
    public string VehicleType { get; set; } = "Car";
    public string? DriverName { get; set; }
    public string? DriverMobile { get; set; }
    public DateTime? EntryTime { get; set; }
    public DateTime? ExitTime { get; set; }
    public string Status { get; set; } = "BarcodeGenerated";
    public string BarcodeStatus { get; set; } = "Generated";
    public int? EntryOperatorId { get; set; }
    public int? ExitOperatorId { get; set; }
    public int OverstayDays { get; set; }
    public decimal OverstayAmount { get; set; }
    public bool ForceExit { get; set; }
    public string? ForceExitReason { get; set; }
    public string? Remarks { get; set; }
    public DateTime CreatedDate { get; set; } = DateTime.Now;
    public int CreatedBy { get; set; }

    public ParkingCompany Company { get; set; } = default!;
    public ParkingSubscription? Subscription { get; set; }
}

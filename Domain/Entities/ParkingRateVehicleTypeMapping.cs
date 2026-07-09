namespace NetworldParkingLot.Api.Domain.Entities;

public sealed class ParkingRateVehicleTypeMapping
{
    public int RateVehicleTypeMappingId { get; set; }
    public int RatePlanId { get; set; }
    public int VehicleTypeId { get; set; }
    public decimal? RatePerSlotOverride { get; set; }
    public bool IsActive { get; set; } = true;
    public DateTime CreatedDate { get; set; } = DateTime.Now;
    public int? CreatedBy { get; set; }
    public DateTime? ModifiedDate { get; set; }
    public int? ModifiedBy { get; set; }

    public ParkingRatePlan? RatePlan { get; set; }
    public ParkingVehicleType? VehicleType { get; set; }
}

namespace NetworldParkingLot.Api.Domain.Entities;

public sealed class ParkingSubscriptionVehicleAllocation
{
    public int SubscriptionVehicleAllocationId { get; set; }
    public int SubscriptionId { get; set; }
    public int VehicleTypeId { get; set; }
    public int SlotsPurchased { get; set; }
    public decimal RatePerSlot { get; set; }
    public decimal LineTotal { get; set; }
    public DateTime CreatedDate { get; set; } = DateTime.Now;
    public int? CreatedBy { get; set; }

    public ParkingSubscription? Subscription { get; set; }
    public ParkingVehicleType? VehicleType { get; set; }
}

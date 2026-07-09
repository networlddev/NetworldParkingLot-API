namespace NetworldParkingLot.Api.Domain.Entities;

public sealed class ParkingVehicleType
{
    public int VehicleTypeId { get; set; }
    public string VehicleTypeName { get; set; } = string.Empty;
    public string? Description { get; set; }
    public bool IsActive { get; set; } = true;
    public int SortOrder { get; set; }
    public DateTime CreatedDate { get; set; } = DateTime.Now;
    public int? CreatedBy { get; set; }
    public DateTime? ModifiedDate { get; set; }
    public int? ModifiedBy { get; set; }
}

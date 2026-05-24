namespace NetworldParkingLot.Api.Domain.Entities;

public sealed class SystemCounter
{
    public int SystemCounterId { get; set; }
    public string CounterName { get; set; } = string.Empty;
    public int LastNumber { get; set; }
    public DateTime UpdatedDate { get; set; } = DateTime.Now;
}

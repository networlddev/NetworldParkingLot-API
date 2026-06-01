namespace NetworldParkingLot.Api.Domain.Entities;

public sealed class AppUserRole
{
    public int UserId { get; set; }
    public int RoleId { get; set; }
    public DateTime CreatedDate { get; set; } = DateTime.Now;

    public AppUser? User { get; set; }
    public AppRole? Role { get; set; }
}

namespace NetworldParkingLot.Api.Domain.Entities;

public sealed class AppRole
{
    public int RoleId { get; set; }
    public string RoleKey { get; set; } = string.Empty;
    public string RoleName { get; set; } = string.Empty;
    public bool Active { get; set; } = true;
    public bool IsDefault { get; set; }
    public DateTime CreatedDate { get; set; } = DateTime.Now;

    public ICollection<AppUserRole> UserRoles { get; set; } = new List<AppUserRole>();
    public ICollection<AppRolePermission> Permissions { get; set; } = new List<AppRolePermission>();
}

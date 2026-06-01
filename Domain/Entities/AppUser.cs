namespace NetworldParkingLot.Api.Domain.Entities;

public sealed class AppUser
{
    public int UserId { get; set; }
    public string Username { get; set; } = string.Empty;
    public string PasswordHash { get; set; } = string.Empty;
    public string FullName { get; set; } = string.Empty;
    public string? Email { get; set; }
    public string? Phone { get; set; }
    public string Role { get; set; } = "Operator";
    public string Status { get; set; } = "Active";
    public bool Active { get; set; } = true;
    public DateTime CreatedDate { get; set; } = DateTime.Now;
    public DateTime? ModifiedDate { get; set; }
    public int? ModifiedBy { get; set; }

    public ICollection<AppUserRole> UserRoles { get; set; } = new List<AppUserRole>();
    public ICollection<AppUserPermission> UserPermissions { get; set; } = new List<AppUserPermission>();
}

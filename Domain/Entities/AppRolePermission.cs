namespace NetworldParkingLot.Api.Domain.Entities;

public sealed class AppRolePermission
{
    public int RoleId { get; set; }
    public int ModuleId { get; set; }
    public string ActionKey { get; set; } = string.Empty;
    public bool IsAllowed { get; set; } = true;

    public AppRole? Role { get; set; }
    public AppModule? Module { get; set; }
}

namespace NetworldParkingLot.Api.Domain.Entities;

public sealed class AppUserPermission
{
    public int UserId { get; set; }
    public int ModuleId { get; set; }
    public int ActionId { get; set; }
    public bool IsAllowed { get; set; } = true;

    public AppUser? User { get; set; }
    public AppModule? Module { get; set; }
    public AppModuleAction? Action { get; set; }
}

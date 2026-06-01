namespace NetworldParkingLot.Api.Domain.Entities;

public sealed class AppModuleAction
{
    public int ActionId { get; set; }
    public int ModuleId { get; set; }
    public string ActionKey { get; set; } = string.Empty;
    public string ActionName { get; set; } = string.Empty;
    public int SortOrder { get; set; }
    public bool Active { get; set; } = true;

    public AppModule? Module { get; set; }
}

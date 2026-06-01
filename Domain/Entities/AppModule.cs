namespace NetworldParkingLot.Api.Domain.Entities;

public sealed class AppModule
{
    public int ModuleId { get; set; }
    public string ModuleKey { get; set; } = string.Empty;
    public string ModuleName { get; set; } = string.Empty;
    public int SortOrder { get; set; }
    public bool Active { get; set; } = true;

    public ICollection<AppModuleAction> Actions { get; set; } = new List<AppModuleAction>();
}

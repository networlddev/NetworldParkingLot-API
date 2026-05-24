namespace NetworldParkingLot.Api.Domain.Entities;

public sealed class SystemSetting
{
    public int SettingId { get; set; }
    public string SettingKey { get; set; } = string.Empty;
    public string SettingValue { get; set; } = string.Empty;
    public string? Remarks { get; set; }
}

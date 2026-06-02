namespace NetworldParkingLot.Api.Domain.Entities;

public sealed class SystemActivityLogDetail
{
    public long SystemActivityLogDetailId { get; set; }
    public long SystemActivityLogId { get; set; }
    public string FieldName { get; set; } = string.Empty;
    public string? OldValue { get; set; }
    public string? NewValue { get; set; }
    public SystemActivityLog? ActivityLog { get; set; }
}

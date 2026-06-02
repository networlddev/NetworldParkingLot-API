namespace NetworldParkingLot.Api.Domain.Entities;

public sealed class SystemActivityLog
{
    public long SystemActivityLogId { get; set; }
    public DateTime ActivityDate { get; set; } = DateTime.Now;
    public int? UserId { get; set; }
    public string Username { get; set; } = string.Empty;
    public string ModuleKey { get; set; } = string.Empty;
    public string ActionKey { get; set; } = string.Empty;
    public string Result { get; set; } = "Success";
    public string EntityType { get; set; } = string.Empty;
    public string? EntityId { get; set; }
    public string Title { get; set; } = string.Empty;
    public string? Message { get; set; }
    public string? IpAddress { get; set; }
    public string? UserAgent { get; set; }
    public string? RequestPath { get; set; }
    public ICollection<SystemActivityLogDetail> Details { get; set; } = [];
}

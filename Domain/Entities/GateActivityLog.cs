namespace NetworldParkingLot.Api.Domain.Entities;

public sealed class GateActivityLog
{
    public long ActivityLogId { get; set; }
    public string ActionType { get; set; } = string.Empty;
    public int? CompanyId { get; set; }
    public int? SessionId { get; set; }
    public string? BarcodeNo { get; set; }
    public string? PlateNo { get; set; }
    public string Status { get; set; } = string.Empty;
    public string? Message { get; set; }
    public int OperatorId { get; set; }
    public DateTime ActionDate { get; set; } = DateTime.Now;
}

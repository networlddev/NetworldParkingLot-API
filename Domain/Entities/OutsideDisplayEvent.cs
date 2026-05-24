namespace NetworldParkingLot.Api.Domain.Entities;

public sealed class OutsideDisplayEvent
{
    public long DisplayEventId { get; set; }
    public int? SessionId { get; set; }
    public string? BarcodeNo { get; set; }
    public string? PlateNo { get; set; }
    public string? CompanyName { get; set; }
    public string DisplayStatus { get; set; } = string.Empty;
    public string MainMessage { get; set; } = string.Empty;
    public string? SubMessage { get; set; }
    public decimal AmountDue { get; set; }
    public int OverstayDays { get; set; }
    public DateTime CreatedDate { get; set; } = DateTime.Now;
}

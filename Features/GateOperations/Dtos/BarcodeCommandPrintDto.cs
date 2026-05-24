namespace NetworldParkingLot.Api.Features.GateOperations.Dtos;

public sealed class BarcodeCommandPrintDto
{
    public string BarcodeNo { get; set; } = string.Empty;
    public string PrinterLanguage { get; set; } = "TSPL";
    public string PrinterName { get; set; } = string.Empty;
    public string Command { get; set; } = string.Empty;
}

using System.ComponentModel.DataAnnotations;

namespace NetworldParkingLot.Api.Features.GateOperations.Dtos;

public sealed class PrintBarcodeRequest
{
    public int? SessionId { get; set; }
    public string? BarcodeNo { get; set; }

    [Required]
    public string PrinterName { get; set; } = string.Empty;

    // TSPL is common for TSC/Xprinter label printers. Use ZPL for Zebra.
    public string PrinterLanguage { get; set; } = "TSPL";
    public int Copies { get; set; } = 1;
}

public sealed class PrintInvoiceRequest
{
    public int? InvoiceId { get; set; }
    public string? InvoiceNo { get; set; }

    [Required]
    public string PrinterName { get; set; } = string.Empty;

    public int Copies { get; set; } = 1;
}

public sealed record PrintJobResultDto(
    bool Printed,
    string PrinterName,
    string DocumentName,
    string Message);

public sealed class BarcodeImagePrintDto
{
    public string BarcodeNo { get; set; } = string.Empty;
    public int WidthMm { get; set; }
    public int HeightMm { get; set; }
    public int Dpi { get; set; }
    public bool Rotated { get; set; }
    public string ImageMimeType { get; set; } = "image/png";
    public string ImageBase64 { get; set; } = string.Empty;
}

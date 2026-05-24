using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using NetworldParkingLot.Api.Common;
using NetworldParkingLot.Api.Common.Printing;
using NetworldParkingLot.Api.Data;
using NetworldParkingLot.Api.Features.GateOperations.Dtos;
using NetworldParkingLot.Api.Infrastructure.Printing;

namespace NetworldParkingLot.Api.Features.GateOperations.Controllers;

[ApiController]
[Route("api/gate-operation/print")]
public sealed class GatePrintController(NetworldParkingDbContext db, IWindowsRawPrinterService printer) : ControllerBase
{
    [HttpGet("barcode-image/{barcodeNo}")]
    public async Task<ActionResult<ApiResponse<BarcodeImagePrintDto>>> GetBarcodeImage(
        string barcodeNo,
        [FromQuery] bool? rotate90,
        [FromQuery] int? widthMm,
        [FromQuery] int? heightMm,
        [FromQuery] int? dpi,
        CancellationToken cancellationToken)
    {
        var cleanBarcode = barcodeNo.Trim().ToUpperInvariant();
        if (string.IsNullOrWhiteSpace(cleanBarcode))
            return BadRequest(ApiResponse<BarcodeImagePrintDto>.Fail("Barcode number is required."));

        var session = await db.ParkingSessions
            .AsNoTracking()
            .Include(x => x.Company)
            .Include(x => x.Subscription)
            .FirstOrDefaultAsync(x => x.BarcodeNo == cleanBarcode, cancellationToken);

        if (session == null)
            return NotFound(ApiResponse<BarcodeImagePrintDto>.Fail("Barcode record not found."));

        var settings = await db.SystemSettings
            .AsNoTracking()
            .ToDictionaryAsync(x => x.SettingKey, x => x.SettingValue, cancellationToken);

        var labelWidthMm = ClampInt(widthMm ?? ReadInt(settings, "BarcodeLabelWidthMm", 60), 25, 120);
        var labelHeightMm = ClampInt(heightMm ?? ReadInt(settings, "BarcodeLabelHeightMm", 35), 15, 80);
        var labelDpi = ClampInt(dpi ?? ReadInt(settings, "BarcodePrinterDpi", 203), 150, 600);
        var shouldRotate = rotate90 ?? ReadBool(settings, "BarcodeRotate90", false);

        var validUntil = session.Subscription?.EndDate.ToString("dd-MMM-yyyy") ?? "-";
        var vehicleReference = string.IsNullOrWhiteSpace(session.PlateNo)
            ? "NO PLATE / TEMP VEHICLE"
            : session.PlateNo.Trim().ToUpperInvariant();

        var pngBytes = BarcodeLabelImageGenerator.GenerateLabelPng(
            projectName: "NETWORLD PARKING LOT",
            companyName: session.Company.CompanyName,
            vehicleReference: vehicleReference,
            barcodeNo: session.BarcodeNo,
            validUntil: validUntil,
            note: "Valid for one parking session only",
            widthMm: labelWidthMm,
            heightMm: labelHeightMm,
            dpi: labelDpi,
            rotate90: shouldRotate);

        var responseWidthMm = shouldRotate ? labelHeightMm : labelWidthMm;
        var responseHeightMm = shouldRotate ? labelWidthMm : labelHeightMm;

        var dto = new BarcodeImagePrintDto
        {
            BarcodeNo = session.BarcodeNo,
            WidthMm = responseWidthMm,
            HeightMm = responseHeightMm,
            Dpi = labelDpi,
            Rotated = shouldRotate,
            ImageBase64 = Convert.ToBase64String(pngBytes)
        };

        return Ok(ApiResponse<BarcodeImagePrintDto>.Ok(dto, "Barcode image generated."));
    }

    // NEW: QZ Tray endpoint. Flutter Web will call this endpoint, receive the raw printer command,
    // and send it to QZ Tray on the gate PC. This avoids Chrome label preview/cropping issues.
    [HttpGet("barcode-command/{barcodeNo}")]
    public async Task<ActionResult<ApiResponse<BarcodeCommandPrintDto>>> GetBarcodeCommand(
        string barcodeNo,
        [FromQuery] string? printerName,
        [FromQuery] string? language,
        [FromQuery] int? copies,
        [FromQuery] int? direction,
        CancellationToken cancellationToken)
    {
        var cleanBarcode = barcodeNo.Trim().ToUpperInvariant();
        if (string.IsNullOrWhiteSpace(cleanBarcode))
            return BadRequest(ApiResponse<BarcodeCommandPrintDto>.Fail("Barcode number is required."));

        var session = await db.ParkingSessions
            .AsNoTracking()
            .Include(x => x.Company)
            .Include(x => x.Subscription)
            .FirstOrDefaultAsync(x => x.BarcodeNo == cleanBarcode, cancellationToken);

        if (session == null)
            return NotFound(ApiResponse<BarcodeCommandPrintDto>.Fail("Barcode record not found."));

        var settings = await db.SystemSettings
            .AsNoTracking()
            .ToDictionaryAsync(x => x.SettingKey, x => x.SettingValue, cancellationToken);

        var selectedLanguage = (language ?? ReadString(settings, "BarcodePrinterLanguage", "TSPL"))
            .Trim()
            .ToUpperInvariant();

        if (selectedLanguage != "TSPL" && selectedLanguage != "ZPL")
            selectedLanguage = "TSPL";

        var selectedPrinter = string.IsNullOrWhiteSpace(printerName)
            ? ReadString(settings, "BarcodePrinterName", string.Empty)
            : printerName.Trim();

        var selectedCopies = Math.Clamp(copies ?? 1, 1, 5);
        var selectedDirection = Math.Clamp(direction ?? ReadInt(settings, "BarcodePrinterDirection", 1), 0, 1);

        var command = selectedLanguage == "ZPL"
            ? BuildZplBarcodeLabel(session, selectedCopies)
            : BuildTsplBarcodeLabel(session, selectedCopies);

        var dto = new BarcodeCommandPrintDto
        {
            BarcodeNo = session.BarcodeNo,
            PrinterLanguage = selectedLanguage,
            PrinterName = selectedPrinter,
            Command = command
        };

        return Ok(ApiResponse<BarcodeCommandPrintDto>.Ok(dto, "Barcode print command generated."));
    }

    [HttpGet("defaults")]
    public async Task<ActionResult<ApiResponse<PrinterListDto>>> GetPrintDefaults(CancellationToken cancellationToken)
    {
        var settings = await db.SystemSettings
            .AsNoTracking()
            .ToDictionaryAsync(x => x.SettingKey, x => x.SettingValue, cancellationToken);

        var dto = new PrinterListDto
        {
            DefaultPrinterName = ReadString(settings, "BarcodePrinterName", string.Empty),
            PrinterLanguage = ReadString(settings, "BarcodePrinterLanguage", "TSPL")
        };

        return Ok(ApiResponse<PrinterListDto>.Ok(dto, "Printer defaults loaded."));
    }

    // Existing server-side Windows printing endpoint. Keep it only if your API is running on the same PC as the printer.
    // For hosted IIS + client-side printer, use barcode-command + QZ Tray instead.
    [HttpPost("barcode")]
    public async Task<ActionResult<ApiResponse<PrintJobResultDto>>> PrintBarcode([FromBody] PrintBarcodeRequest request, CancellationToken cancellationToken)
    {
        try
        {
            var session = await db.ParkingSessions
                .Include(x => x.Company)
                .Include(x => x.Subscription)
                .FirstOrDefaultAsync(x =>
                    (request.SessionId.HasValue && x.SessionId == request.SessionId.Value) ||
                    (!string.IsNullOrWhiteSpace(request.BarcodeNo) && x.BarcodeNo == request.BarcodeNo), cancellationToken);

            if (session == null)
                return BadRequest(ApiResponse<PrintJobResultDto>.Fail("Barcode record not found."));

            var copies = Math.Clamp(request.Copies, 1, 5);
            var language = string.IsNullOrWhiteSpace(request.PrinterLanguage) ? "TSPL" : request.PrinterLanguage.Trim().ToUpperInvariant();
            var command = language == "ZPL"
                ? BuildZplBarcodeLabel(session, copies)
                : BuildTsplBarcodeLabel(session, copies);

            await printer.SendRawCommandAsync(request.PrinterName, $"Barcode-{session.BarcodeNo}", command, cancellationToken);

            var result = new PrintJobResultDto(true, request.PrinterName, $"Barcode-{session.BarcodeNo}", "Barcode sent to printer.");
            return Ok(ApiResponse<PrintJobResultDto>.Ok(result, result.Message));
        }
        catch (InvalidOperationException ex)
        {
            return BadRequest(ApiResponse<PrintJobResultDto>.Fail(ex.Message));
        }
    }

    [HttpGet("invoice-command")]
    public async Task<ActionResult<ApiResponse<InvoiceCommandPrintDto>>> GetInvoiceCommand(
        [FromQuery] int? invoiceId,
        [FromQuery] string? invoiceNo,
        [FromQuery] int? copies,
        [FromQuery] string? printerName,
        CancellationToken cancellationToken)
    {
        if (!invoiceId.HasValue && string.IsNullOrWhiteSpace(invoiceNo))
            return BadRequest(ApiResponse<InvoiceCommandPrintDto>.Fail("InvoiceId or InvoiceNo is required."));

        var cleanInvoiceNo = string.IsNullOrWhiteSpace(invoiceNo) ? string.Empty : invoiceNo.Trim();

        var invoice = await db.ParkingInvoices
            .AsNoTracking()
            .Include(x => x.Company)
            .FirstOrDefaultAsync(x =>
                (invoiceId.HasValue && x.InvoiceId == invoiceId.Value) ||
                (!string.IsNullOrWhiteSpace(cleanInvoiceNo) && x.InvoiceNo == cleanInvoiceNo), cancellationToken);

        if (invoice == null)
            return NotFound(ApiResponse<InvoiceCommandPrintDto>.Fail("Invoice not found."));

        var settings = await db.SystemSettings
            .AsNoTracking()
            .ToDictionaryAsync(x => x.SettingKey, x => x.SettingValue, cancellationToken);

        var selectedPrinter = string.IsNullOrWhiteSpace(printerName)
            ? ReadString(settings, "InvoicePrinterName", string.Empty)
            : printerName.Trim();

        var command = BuildReceiptInvoice(invoice, Math.Clamp(copies ?? 1, 1, 5));
        var dto = new InvoiceCommandPrintDto
        {
            InvoiceId = invoice.InvoiceId,
            InvoiceNo = invoice.InvoiceNo,
            PrinterName = selectedPrinter,
            Command = command
        };

        return Ok(ApiResponse<InvoiceCommandPrintDto>.Ok(dto, "Invoice print command generated."));
    }

    [HttpPost("invoice")]
    public async Task<ActionResult<ApiResponse<PrintJobResultDto>>> PrintInvoice([FromBody] PrintInvoiceRequest request, CancellationToken cancellationToken)
    {
        try
        {
            var invoice = await db.ParkingInvoices
                .Include(x => x.Company)
                .FirstOrDefaultAsync(x =>
                    (request.InvoiceId.HasValue && x.InvoiceId == request.InvoiceId.Value) ||
                    (!string.IsNullOrWhiteSpace(request.InvoiceNo) && x.InvoiceNo == request.InvoiceNo), cancellationToken);

            if (invoice == null)
                return BadRequest(ApiResponse<PrintJobResultDto>.Fail("Invoice not found."));

            var command = BuildReceiptInvoice(invoice, Math.Clamp(request.Copies, 1, 5));
            await printer.SendRawCommandAsync(request.PrinterName, $"Invoice-{invoice.InvoiceNo}", command, cancellationToken);

            var result = new PrintJobResultDto(true, request.PrinterName, $"Invoice-{invoice.InvoiceNo}", "Invoice sent to printer.");
            return Ok(ApiResponse<PrintJobResultDto>.Ok(result, result.Message));
        }
        catch (InvalidOperationException ex)
        {
            return BadRequest(ApiResponse<PrintJobResultDto>.Fail(ex.Message));
        }
    }

    private static string Clean(string? value, int maxLength = 32)
    {
        value = string.IsNullOrWhiteSpace(value) ? "-" : value.Trim();
        value = value.Replace("\"", "'").Replace("\r", " ").Replace("\n", " ");
        return value.Length <= maxLength ? value : value[..maxLength];
    }

    private static int ReadInt(Dictionary<string, string> settings, string key, int fallback) =>
        settings.TryGetValue(key, out var value) && int.TryParse(value, out var parsed) ? parsed : fallback;

    private static bool ReadBool(Dictionary<string, string> settings, string key, bool fallback) =>
        settings.TryGetValue(key, out var value) && bool.TryParse(value, out var parsed) ? parsed : fallback;

    private static string ReadString(Dictionary<string, string> settings, string key, string fallback) =>
        settings.TryGetValue(key, out var value) && !string.IsNullOrWhiteSpace(value) ? value.Trim() : fallback;

    private static int ClampInt(int value, int min, int max) => Math.Min(Math.Max(value, min), max);

    private static string BuildTsplBarcodeLabel(Domain.Entities.ParkingSession session, int copies)
    {
        var company = Clean(session.Company.CompanyName, 30);
        var vehicleRef = string.IsNullOrWhiteSpace(session.PlateNo)
            ? "NO PLATE / TEMP VEHICLE"
            : Clean(session.PlateNo, 26);

        var barcode = Clean(session.BarcodeNo, 28);
        var validUntil = session.Subscription?.EndDate.ToString("dd-MMM-yyyy") ?? "-";

        return $"""
SIZE 60 mm,35 mm
GAP 3 mm,0 mm
DENSITY 8
DIRECTION 1
REFERENCE 0,0
CLS
TEXT 42,12,"2",0,1,1,"NETWORLD PARKING LOT"
TEXT 42,38,"1",0,1,1,"{company}"
TEXT 42,60,"2",0,1,1,"{vehicleRef}"
BARCODE 42,92,"128",78,1,0,1,2,"{barcode}"
TEXT 42,196,"1",0,1,1,"{barcode}"
TEXT 42,218,"1",0,1,1,"Valid Until: {validUntil}"
TEXT 42,240,"1",0,1,1,"One parking session only"
PRINT {copies},1
""";
    }

    private static string BuildZplBarcodeLabel(Domain.Entities.ParkingSession session, int copies)
    {
        var company = Clean(session.Company.CompanyName, 30);
        var vehicleRef = string.IsNullOrWhiteSpace(session.PlateNo)
            ? "NO PLATE / TEMP VEHICLE"
            : Clean(session.PlateNo, 26);

        var barcode = Clean(session.BarcodeNo, 28);
        var validUntil = session.Subscription?.EndDate.ToString("dd-MMM-yyyy") ?? "-";

        return $"""
^XA
^PW480
^LL280
^LH0,0
^FO42,12^A0N,22,22^FDNETWORLD PARKING LOT^FS
^FO42,40^A0N,17,17^FD{company}^FS
^FO42,64^A0N,21,21^FD{vehicleRef}^FS
^BY1,2,78
^FO42,94^BCN,78,Y,N,N
^FD{barcode}^FS
^FO42,206^A0N,15,15^FDValid Until: {validUntil}^FS
^FO42,226^A0N,15,15^FDOne parking session only^FS
^PQ{copies}
^XZ
""";
    }

    private static string BuildReceiptInvoice(Domain.Entities.ParkingInvoice invoice, int copies)
    {
        var sb = new System.Text.StringBuilder();
        for (var i = 0; i < copies; i++)
        {
            sb.AppendLine("NETWORLD SMART PARKING");
            sb.AppendLine("PARKING INVOICE");
            sb.AppendLine("--------------------------------");
            sb.AppendLine($"Invoice No : {invoice.InvoiceNo}");
            sb.AppendLine($"Date       : {invoice.InvoiceDate:dd-MMM-yyyy HH:mm}");
            sb.AppendLine($"Company    : {Clean(invoice.Company.CompanyName, 28)}");
            sb.AppendLine($"Type       : {invoice.InvoiceType}");
            sb.AppendLine($"Plan       : {invoice.PlanType ?? "-"}");
            sb.AppendLine($"Slots      : {invoice.Slots}");
            sb.AppendLine("--------------------------------");
            sb.AppendLine($"Sub Total  : AED {invoice.SubTotal:n2}");
            sb.AppendLine($"Discount   : AED {invoice.DiscountAmount:n2}");
            sb.AppendLine($"VAT        : AED {invoice.VatAmount:n2}");
            sb.AppendLine($"Total      : AED {invoice.TotalAmount:n2}");
            sb.AppendLine($"Paid       : AED {invoice.PaidAmount:n2}");
            sb.AppendLine($"Balance    : AED {invoice.BalanceAmount:n2}");
            sb.AppendLine("--------------------------------");
            var printStatus = invoice.BalanceAmount <= 0 ? "Paid" : invoice.PaidAmount > 0 ? "Partial" : "Unpaid";
            sb.AppendLine($"Status     : {printStatus}");
            sb.AppendLine("Thank you");
            sb.AppendLine();
            sb.AppendLine();
            sb.AppendLine();
            sb.Append("\x1B\x64\x06");      // Feed 6 lines.
            sb.Append("\x1D\x56\x42\x00");  // Feed and cut for ESC/POS printers.
        }
        return sb.ToString();
    }
}

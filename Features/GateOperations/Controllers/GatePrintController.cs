using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using NetworldParkingLot.Api.Common;
using NetworldParkingLot.Api.Common.Printing;
using NetworldParkingLot.Api.Data;
using NetworldParkingLot.Api.Features.GateOperations.Dtos;
using NetworldParkingLot.Api.Features.UserAccess.Filters;
using NetworldParkingLot.Api.Infrastructure.Printing;
using SkiaSharp;
using ZXing;
using ZXing.Common;
using ZXing.SkiaSharp;

namespace NetworldParkingLot.Api.Features.GateOperations.Controllers;

[Authorize]
[ApiController]
[Route("api/gate-operation/print")]
public sealed class GatePrintController(NetworldParkingDbContext db, IWindowsRawPrinterService printer) : ControllerBase
{
    [RequireParkingPermission("gate_operation", "print")]
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
        var marginLeftMm = ClampInt(ReadInt(settings, "BarcodeMarginLeftMm", 5), 0, 30);
        var marginTopMm = ClampInt(ReadInt(settings, "BarcodeMarginTopMm", 2), 0, 30);
        var marginRightMm = ClampInt(ReadInt(settings, "BarcodeMarginRightMm", 5), 0, 30);
        var marginBottomMm = ClampInt(ReadInt(settings, "BarcodeMarginBottomMm", 2), 0, 30);
        var shouldRotate = rotate90 ?? ReadBool(settings, "BarcodeRotate90", false);

        var validUntil = session.Subscription?.EndDate.ToString("dd-MMM-yyyy") ?? "-";
        var vehicleReference = string.IsNullOrWhiteSpace(session.PlateNo)
            ? "NO PLATE / TEMP VEHICLE"
            : session.PlateNo.Trim().ToUpperInvariant();

        var pngBytes = BarcodeLabelImageGenerator.GenerateLabelPng(
            projectName: ReadString(settings, "BarcodeLabelTitle", "NETWORLD PARKING LOT"),
            companyName: session.Company.CompanyName,
            vehicleReference: vehicleReference,
            barcodeNo: session.BarcodeNo,
            validUntil: validUntil,
            note: ReadString(settings, "BarcodeLabelNote", "One parking session only"),
            widthMm: labelWidthMm,
            heightMm: labelHeightMm,
            dpi: labelDpi,
            marginLeftMm: marginLeftMm,
            marginTopMm: marginTopMm,
            marginRightMm: marginRightMm,
            marginBottomMm: marginBottomMm,
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
    [RequireParkingPermission("gate_operation", "print")]
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

        var selectedLanguage = NormalizePrinterLanguage(language ?? ReadString(settings, "BarcodePrinterLanguage", "TSPL"));

        var selectedPrinter = string.IsNullOrWhiteSpace(printerName)
            ? ReadString(settings, "BarcodePrinterName", string.Empty)
            : printerName.Trim();

        var selectedCopies = Math.Clamp(copies ?? ReadInt(settings, "BarcodePrintCopies", 1), 1, 5);
        var selectedDirection = Math.Clamp(direction ?? ReadInt(settings, "BarcodePrinterDirection", 1), 0, 1);

        var command = selectedLanguage == "ZPL"
            ? BuildZplBarcodeLabel(session, selectedCopies, settings)
            : BuildTsplBarcodeLabel(session, selectedCopies, settings, selectedDirection);

        var dto = new BarcodeCommandPrintDto
        {
            BarcodeNo = session.BarcodeNo,
            PrinterLanguage = selectedLanguage,
            PrinterName = selectedPrinter,
            Command = command
        };

        return Ok(ApiResponse<BarcodeCommandPrintDto>.Ok(dto, "Barcode print command generated."));
    }

    [RequireParkingPermission("gate_operation", "print")]
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
    [RequireParkingPermission("gate_operation", "print")]
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

            var settings = await db.SystemSettings
                .AsNoTracking()
                .ToDictionaryAsync(x => x.SettingKey, x => x.SettingValue, cancellationToken);

            var copies = Math.Clamp(request.Copies > 0 ? request.Copies : ReadInt(settings, "BarcodePrintCopies", 1), 1, 5);
            var language = NormalizePrinterLanguage(request.PrinterLanguage ?? ReadString(settings, "BarcodePrinterLanguage", "TSPL"));
            var selectedPrinter = string.IsNullOrWhiteSpace(request.PrinterName)
                ? ReadString(settings, "BarcodePrinterName", string.Empty)
                : request.PrinterName.Trim();
            var direction = Math.Clamp(ReadInt(settings, "BarcodePrinterDirection", 1), 0, 1);
            var command = language == "ZPL"
                ? BuildZplBarcodeLabel(session, copies, settings)
                : BuildTsplBarcodeLabel(session, copies, settings, direction);

            await printer.SendRawCommandAsync(selectedPrinter, $"Barcode-{session.BarcodeNo}", command, cancellationToken);

            var result = new PrintJobResultDto(true, selectedPrinter, $"Barcode-{session.BarcodeNo}", "Barcode sent to printer.");
            return Ok(ApiResponse<PrintJobResultDto>.Ok(result, result.Message));
        }
        catch (InvalidOperationException ex)
        {
            return BadRequest(ApiResponse<PrintJobResultDto>.Fail(ex.Message));
        }
    }

    [RequireParkingPermission("invoices", "print")]
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

        var selectedCopies = Math.Clamp(copies ?? ReadInt(settings, "InvoicePrintCopies", 1), 1, 5);
        var command = BuildReceiptInvoice(invoice, selectedCopies, settings);
        var dto = new InvoiceCommandPrintDto
        {
            InvoiceId = invoice.InvoiceId,
            InvoiceNo = invoice.InvoiceNo,
            PrinterName = selectedPrinter,
            Command = command
        };

        return Ok(ApiResponse<InvoiceCommandPrintDto>.Ok(dto, "Invoice print command generated."));
    }

    [RequireParkingPermission("invoices", "print")]
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

            var settings = await db.SystemSettings
                .AsNoTracking()
                .ToDictionaryAsync(x => x.SettingKey, x => x.SettingValue, cancellationToken);
            var selectedPrinter = string.IsNullOrWhiteSpace(request.PrinterName)
                ? ReadString(settings, "InvoicePrinterName", string.Empty)
                : request.PrinterName.Trim();
            var copies = Math.Clamp(request.Copies > 0 ? request.Copies : ReadInt(settings, "InvoicePrintCopies", 1), 1, 5);
            var command = BuildReceiptInvoice(invoice, copies, settings);
            await printer.SendRawCommandAsync(selectedPrinter, $"Invoice-{invoice.InvoiceNo}", command, cancellationToken);

            var result = new PrintJobResultDto(true, selectedPrinter, $"Invoice-{invoice.InvoiceNo}", "Invoice sent to printer.");
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
    private static int MmToDots(int mm, int dpi) => Math.Max(0, (int)Math.Round(mm / 25.4d * dpi));
    private static PrinterBitmap BuildBarcodeBitmap(string barcode, string symbology, int widthDots, int heightDots)
    {
        var targetWidth = Math.Max(24, widthDots);
        var targetHeight = Math.Max(24, heightDots);
        var writer = new BarcodeWriter
        {
            Format = BarcodeFormatFromSymbology(symbology),
            Options = new EncodingOptions
            {
                Width = Math.Max(240, targetWidth * 3),
                Height = Math.Max(80, targetHeight),
                Margin = 0,
                PureBarcode = true
            }
        };

        using var source = writer.Write(barcode);
        var bounds = FindBlackBounds(source);
        var bytesPerRow = (targetWidth + 7) / 8;
        var bytes = new byte[bytesPerRow * targetHeight];

        for (var y = 0; y < targetHeight; y++)
        {
            var sourceY = bounds.Top + ((long)y * bounds.Height / targetHeight);
            for (var x = 0; x < targetWidth; x++)
            {
                var sourceX = bounds.Left + ((long)x * bounds.Width / targetWidth);
                var color = source.GetPixel((int)sourceX, (int)sourceY);
                var isBlack = color.Red < 128 && color.Green < 128 && color.Blue < 128 && color.Alpha > 0;
                if (!isBlack) continue;

                var index = (y * bytesPerRow) + (x / 8);
                bytes[index] |= (byte)(0x80 >> (x % 8));
            }
        }

        return new(targetWidth, targetHeight, bytesPerRow, Convert.ToHexString(bytes));
    }

    private static SKRectI FindBlackBounds(SKBitmap bitmap)
    {
        var left = bitmap.Width;
        var top = bitmap.Height;
        var right = -1;
        var bottom = -1;

        for (var y = 0; y < bitmap.Height; y++)
        {
            for (var x = 0; x < bitmap.Width; x++)
            {
                var color = bitmap.GetPixel(x, y);
                var isBlack = color.Red < 128 && color.Green < 128 && color.Blue < 128 && color.Alpha > 0;
                if (!isBlack) continue;

                if (x < left) left = x;
                if (x > right) right = x;
                if (y < top) top = y;
                if (y > bottom) bottom = y;
            }
        }

        if (right < left || bottom < top)
            return new SKRectI(0, 0, bitmap.Width, bitmap.Height);

        return new SKRectI(left, top, right + 1, bottom + 1);
    }

    private static int CenteredTsplTextX(int left, int boxWidth, string text, int fontWidthDots = 8)
    {
        var textWidth = Math.Max(0, text.Length * fontWidthDots);
        return Math.Max(0, left + ((boxWidth - textWidth) / 2));
    }

    private static BarcodeFormat BarcodeFormatFromSymbology(string symbology) => symbology switch
    {
        "39" or "CODE39" => BarcodeFormat.CODE_39,
        "EAN13" => BarcodeFormat.EAN_13,
        "EAN8" => BarcodeFormat.EAN_8,
        "CODABAR" => BarcodeFormat.CODABAR,
        "ITF" => BarcodeFormat.ITF,
        _ => BarcodeFormat.CODE_128
    };

    private sealed record PrinterBitmap(int WidthDots, int HeightDots, int BytesPerRow, string HexData);

    private static string BuildTsplBarcodeLabel(Domain.Entities.ParkingSession session, int copies, Dictionary<string, string> settings, int direction)
    {
        var company = Clean(session.Company.CompanyName, 30);
        var vehicleRef = string.IsNullOrWhiteSpace(session.PlateNo)
            ? "NO PLATE / TEMP VEHICLE"
            : Clean(session.PlateNo, 26);

        var barcode = Clean(session.BarcodeNo, 28);
        var validUntil = session.Subscription?.EndDate.ToString("dd-MMM-yyyy") ?? "-";
        var title = Clean(ReadString(settings, "BarcodeLabelTitle", "NETWORLD PARKING LOT"), 34);
        var note = Clean(ReadString(settings, "BarcodeLabelNote", "One parking session only"), 38);
        var symbology = NormalizeBarcodeSymbology(ReadString(settings, "BarcodeSymbology", "128"));
        var humanReadable = ReadBool(settings, "BarcodeShowHumanReadable", true) ? 1 : 0;
        var widthMm = ClampInt(ReadInt(settings, "BarcodeLabelWidthMm", 60), 25, 120);
        var heightMm = ClampInt(ReadInt(settings, "BarcodeLabelHeightMm", 35), 15, 80);
        var dpi = ClampInt(ReadInt(settings, "BarcodePrinterDpi", 203), 150, 600);
        var symbolWidthMm = ClampInt(ReadInt(settings, "BarcodeSymbolWidthMm", 48), 10, 110);
        var symbolHeightMm = ClampInt(ReadInt(settings, "BarcodeSymbolHeightMm", 12), 5, 50);
        var marginLeft = MmToDots(ClampInt(ReadInt(settings, "BarcodeMarginLeftMm", 5), 0, 30), dpi);
        var marginRight = MmToDots(ClampInt(ReadInt(settings, "BarcodeMarginRightMm", 5), 0, 30), dpi);
        var marginTop = MmToDots(ClampInt(ReadInt(settings, "BarcodeMarginTopMm", 2), 0, 30), dpi);
        var marginBottom = MmToDots(ClampInt(ReadInt(settings, "BarcodeMarginBottomMm", 2), 0, 30), dpi);
        var labelWidthDots = MmToDots(widthMm, dpi);
        var labelHeightDots = MmToDots(heightMm, dpi);
        var density = ClampInt(ReadInt(settings, "BarcodePrintDensity", 8), 1, 15);
        var safeDirection = Math.Clamp(direction, 0, 1);
        var safeCopies = Math.Clamp(copies, 1, 5);
        var contentX = marginLeft;
        var titleY = marginTop;
        var companyY = titleY + 26;
        var vehicleY = companyY + 22;
        var barcodeY = vehicleY + 32;
        var maxSymbolWidth = Math.Max(80, labelWidthDots - marginLeft - marginRight);
        var maxSymbolHeight = Math.Max(24, labelHeightDots - marginBottom - barcodeY - (humanReadable == 1 ? 70 : 40));
        var symbolWidthDots = Math.Min(MmToDots(symbolWidthMm, dpi), maxSymbolWidth);
        var symbolHeightDots = Math.Min(MmToDots(symbolHeightMm, dpi), maxSymbolHeight);
        var barcodeImage = BuildBarcodeBitmap(barcode, symbology, symbolWidthDots, symbolHeightDots);
        var barcodeTextX = CenteredTsplTextX(contentX, barcodeImage.WidthDots, barcode);
        var footerY = barcodeY + barcodeImage.HeightDots + (humanReadable == 1 ? 24 : 14);
        var noteY = footerY + 22;

        var sb = new System.Text.StringBuilder();
        sb.AppendLine($"SIZE {widthMm} mm,{heightMm} mm");
        sb.AppendLine("GAP 3 mm,0 mm");
        sb.AppendLine($"DENSITY {density}");
        sb.AppendLine($"DIRECTION {safeDirection}");
        sb.AppendLine("REFERENCE 0,0");
        sb.AppendLine("CLS");
        sb.AppendLine($"TEXT {contentX},{titleY},\"2\",0,1,1,\"{title}\"");
        sb.AppendLine($"TEXT {contentX},{companyY},\"1\",0,1,1,\"{company}\"");
        sb.AppendLine($"TEXT {contentX},{vehicleY},\"2\",0,1,1,\"{vehicleRef}\"");
        sb.AppendLine($"BITMAP {contentX},{barcodeY},{barcodeImage.BytesPerRow},{barcodeImage.HeightDots},0,{barcodeImage.HexData}");
        if (humanReadable == 1)
            sb.AppendLine($"TEXT {barcodeTextX},{barcodeY + barcodeImage.HeightDots + 4},\"1\",0,1,1,\"{barcode}\"");
        sb.AppendLine($"TEXT {contentX},{footerY},\"1\",0,1,1,\"Valid Until: {validUntil}\"");
        sb.AppendLine($"TEXT {contentX},{noteY},\"1\",0,1,1,\"{note}\"");
        sb.AppendLine($"PRINT {safeCopies},1");
        return sb.ToString();
    }


    private static string BuildZplBarcodeLabel(Domain.Entities.ParkingSession session, int copies, Dictionary<string, string> settings)
    {
        var company = Clean(session.Company.CompanyName, 30);
        var vehicleRef = string.IsNullOrWhiteSpace(session.PlateNo)
            ? "NO PLATE / TEMP VEHICLE"
            : Clean(session.PlateNo, 26);

        var barcode = Clean(session.BarcodeNo, 28);
        var validUntil = session.Subscription?.EndDate.ToString("dd-MMM-yyyy") ?? "-";
        var title = Clean(ReadString(settings, "BarcodeLabelTitle", "NETWORLD PARKING LOT"), 34);
        var note = Clean(ReadString(settings, "BarcodeLabelNote", "One parking session only"), 38);
        var symbology = NormalizeBarcodeSymbology(ReadString(settings, "BarcodeSymbology", "128"));
        var humanReadable = ReadBool(settings, "BarcodeShowHumanReadable", true) ? "Y" : "N";
        var widthMm = ClampInt(ReadInt(settings, "BarcodeLabelWidthMm", 60), 25, 120);
        var heightMm = ClampInt(ReadInt(settings, "BarcodeLabelHeightMm", 35), 15, 80);
        var dpi = ClampInt(ReadInt(settings, "BarcodePrinterDpi", 203), 150, 600);
        var symbolWidthMm = ClampInt(ReadInt(settings, "BarcodeSymbolWidthMm", 48), 10, 110);
        var symbolHeightMm = ClampInt(ReadInt(settings, "BarcodeSymbolHeightMm", 12), 5, 50);
        var marginLeft = MmToDots(ClampInt(ReadInt(settings, "BarcodeMarginLeftMm", 5), 0, 30), dpi);
        var marginRight = MmToDots(ClampInt(ReadInt(settings, "BarcodeMarginRightMm", 5), 0, 30), dpi);
        var marginTop = MmToDots(ClampInt(ReadInt(settings, "BarcodeMarginTopMm", 2), 0, 30), dpi);
        var marginBottom = MmToDots(ClampInt(ReadInt(settings, "BarcodeMarginBottomMm", 2), 0, 30), dpi);
        var dotsPerMm = dpi / 25.4m;
        var printWidth = Math.Max(280, (int)Math.Round(widthMm * dotsPerMm));
        var labelLength = Math.Max(180, (int)Math.Round(heightMm * dotsPerMm));
        var titleY = marginTop;
        var companyY = titleY + 28;
        var vehicleY = companyY + 24;
        var barcodeY = vehicleY + 34;
        var maxSymbolWidth = Math.Max(80, printWidth - marginLeft - marginRight);
        var maxSymbolHeight = Math.Max(24, labelLength - marginBottom - barcodeY - (humanReadable == "Y" ? 54 : 32));
        var symbolWidthDots = Math.Min(MmToDots(symbolWidthMm, dpi), maxSymbolWidth);
        var symbolHeightDots = Math.Min(MmToDots(symbolHeightMm, dpi), maxSymbolHeight);
        var barcodeImage = BuildBarcodeBitmap(barcode, symbology, symbolWidthDots, symbolHeightDots);
        var safeCopies = Math.Clamp(copies, 1, 5);
        var footerY = barcodeY + barcodeImage.HeightDots + (humanReadable == "Y" ? 28 : 16);
        var noteY = footerY + 20;

        var totalBytes = barcodeImage.BytesPerRow * barcodeImage.HeightDots;
        var sb = new System.Text.StringBuilder();
        sb.AppendLine("^XA");
        sb.AppendLine($"^PW{printWidth}");
        sb.AppendLine($"^LL{labelLength}");
        sb.AppendLine("^LH0,0");
        sb.AppendLine($"^FO{marginLeft},{titleY}^A0N,22,22^FD{title}^FS");
        sb.AppendLine($"^FO{marginLeft},{companyY}^A0N,17,17^FD{company}^FS");
        sb.AppendLine($"^FO{marginLeft},{vehicleY}^A0N,21,21^FD{vehicleRef}^FS");
        sb.AppendLine($"^FO{marginLeft},{barcodeY}^GFA,{totalBytes},{totalBytes},{barcodeImage.BytesPerRow},{barcodeImage.HexData}^FS");
        if (humanReadable == "Y")
            sb.AppendLine($"^FO{marginLeft},{barcodeY + barcodeImage.HeightDots + 4}^FB{barcodeImage.WidthDots},1,0,C^A0N,15,15^FD{barcode}^FS");
        sb.AppendLine($"^FO{marginLeft},{footerY}^A0N,15,15^FDValid Until: {validUntil}^FS");
        sb.AppendLine($"^FO{marginLeft},{noteY}^A0N,15,15^FD{note}^FS");
        sb.AppendLine($"^PQ{safeCopies}");
        sb.AppendLine("^XZ");
        return sb.ToString();
    }


    private static string BuildReceiptInvoice(Domain.Entities.ParkingInvoice invoice, int copies, Dictionary<string, string> settings)
    {
        var sb = new System.Text.StringBuilder();
        var companyName = Clean(ReadString(settings, "InvoiceCompanyName", "NETWORLD SMART PARKING"), 32);
        var title = Clean(ReadString(settings, "InvoiceTitle", "PARKING INVOICE"), 32);
        var address = Clean(ReadString(settings, "InvoiceAddress", string.Empty), 40);
        var trn = Clean(ReadString(settings, "InvoiceTrn", string.Empty), 30);
        var currency = Clean(ReadString(settings, "InvoiceCurrency", "AED"), 8);
        var footer = Clean(ReadString(settings, "InvoiceFooterText", "Thank you"), 38);
        var showVatLine = ReadBool(settings, "InvoiceShowVatLine", true);
        var safeCopies = Math.Clamp(copies, 1, 5);

        for (var i = 0; i < safeCopies; i++)
        {
            sb.AppendLine(companyName);
            if (!string.IsNullOrWhiteSpace(address) && address != "-") sb.AppendLine(address);
            if (!string.IsNullOrWhiteSpace(trn) && trn != "-") sb.AppendLine($"TRN: {trn}");
            sb.AppendLine(title);
            sb.AppendLine("--------------------------------");
            sb.AppendLine($"Invoice No : {invoice.InvoiceNo}");
            sb.AppendLine($"Date       : {invoice.InvoiceDate:dd-MMM-yyyy HH:mm}");
            sb.AppendLine($"Company    : {Clean(invoice.Company.CompanyName, 28)}");
            sb.AppendLine($"Type       : {invoice.InvoiceType}");
            sb.AppendLine($"Plan       : {invoice.PlanType ?? "-"}");
            sb.AppendLine($"Slots      : {invoice.Slots}");
            sb.AppendLine("--------------------------------");
            sb.AppendLine($"Sub Total  : {currency} {invoice.SubTotal:n2}");
            sb.AppendLine($"Discount   : {currency} {invoice.DiscountAmount:n2}");
            if (showVatLine) sb.AppendLine($"VAT        : {currency} {invoice.VatAmount:n2}");
            sb.AppendLine($"Total      : {currency} {invoice.TotalAmount:n2}");
            sb.AppendLine($"Paid       : {currency} {invoice.PaidAmount:n2}");
            sb.AppendLine($"Balance    : {currency} {invoice.BalanceAmount:n2}");
            sb.AppendLine("--------------------------------");
            var printStatus = invoice.BalanceAmount <= 0 ? "Paid" : invoice.PaidAmount > 0 ? "Partial" : "Unpaid";
            sb.AppendLine($"Status     : {printStatus}");
            sb.AppendLine(footer);
            sb.AppendLine();
            sb.AppendLine();
            sb.AppendLine();
            sb.Append("\x1B\x64\x06");      // Feed 6 lines.
            sb.Append("\x1D\x56\x42\x00");  // Feed and cut for ESC/POS printers.
        }
        return sb.ToString();
    }

    private static string BuildZplBarcodeCommand(string symbology, string barcode, string humanReadable, int barHeight)
    {
        return symbology switch
        {
            "39" or "CODE39" => $"^B3N,N,{barHeight},{humanReadable},N^FD{barcode}^FS",
            "EAN13" => $"^BEN,{barHeight},{humanReadable},N^FD{barcode}^FS",
            "EAN8" => $"^B8N,{barHeight},{humanReadable},N^FD{barcode}^FS",
            _ => $"^BCN,{barHeight},{humanReadable},N,N^FD{barcode}^FS"
        };
    }

    private static string NormalizePrinterLanguage(string? value)
    {
        var clean = string.IsNullOrWhiteSpace(value) ? "TSPL" : value.Trim().ToUpperInvariant();
        return clean == "ZPL" ? "ZPL" : "TSPL";
    }

    private static string NormalizeBarcodeSymbology(string? value)
    {
        var clean = string.IsNullOrWhiteSpace(value) ? "128" : value.Trim().ToUpperInvariant();
        clean = clean.Replace("CODE", string.Empty).Replace("-", string.Empty).Replace(" ", string.Empty);
        return clean switch
        {
            "39" => "39",
            "93" => "93",
            "EAN13" or "EAN-13" => "EAN13",
            "EAN8" or "EAN-8" => "EAN8",
            "CODABAR" => "CODABAR",
            "ITF" or "I25" or "INTERLEAVED2OF5" => "ITF",
            _ => "128"
        };
    }

}

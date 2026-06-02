using System.Globalization;
using Microsoft.EntityFrameworkCore;
using NetworldParkingLot.Api.Data;
using NetworldParkingLot.Api.Domain.Entities;
using NetworldParkingLot.Api.Features.Settings.Dtos;

namespace NetworldParkingLot.Api.Features.Settings.Services;

public sealed class SettingsService(NetworldParkingDbContext db) : ISettingsService
{
    private static readonly IReadOnlyList<SettingsOptionDto> BarcodeSymbologyOptions =
    [
        new("128", "Code 128", "Recommended default for parking barcode labels"),
        new("39", "Code 39", "Simple alphanumeric barcode"),
        new("93", "Code 93", "Compact alphanumeric barcode"),
        new("EAN13", "EAN-13", "Retail-style 13 digit barcode"),
        new("EAN8", "EAN-8", "Retail-style 8 digit barcode"),
        new("CODABAR", "Codabar", "Legacy barcode type"),
        new("ITF", "Interleaved 2 of 5", "Numeric barcode type")
    ];

    private static readonly IReadOnlyList<SettingsOptionDto> BarcodePrinterLanguageOptions =
    [
        new("TSPL", "TSPL", "TSC / XPrinter / many thermal label printers"),
        new("ZPL", "ZPL", "Zebra label printers")
    ];

    private static readonly IReadOnlyList<SettingsOptionDto> PrinterTypeOptions =
    [
        new("ThermalLabel", "Thermal label printer", "Barcode sticker printer"),
        new("ESC/POS", "ESC/POS receipt printer", "80mm receipt/invoice printer"),
        new("Windows", "Windows printer driver", "Normal Windows installed printer")
    ];

    private static readonly IReadOnlyList<SettingsOptionDto> BarcodePrintModeOptions =
    [
        new("Command", "Raw command", "Recommended for Flutter Windows / QZ tray"),
        new("Image", "Image label", "Use generated PNG label if command mode is not suitable")
    ];

    public async Task<ParkingSettingsDto> GetSettingsAsync(CancellationToken cancellationToken = default)
    {
        var settings = await LoadSettingsDictionaryAsync(cancellationToken);
        return BuildDto(settings);
    }

    public async Task<ParkingSettingsDto> UpdateSettingsAsync(UpdateParkingSettingsRequest request, int operatorId, CancellationToken cancellationToken = default)
    {
        Validate(request);

        var values = ToSettingValues(request);
        var keys = values.Keys.ToList();
        var existingRows = await db.SystemSettings
            .Where(x => keys.Contains(x.SettingKey))
            .ToListAsync(cancellationToken);
        var existing = existingRows.ToDictionary(x => x.SettingKey, StringComparer.OrdinalIgnoreCase);

        foreach (var pair in values)
        {
            if (existing.TryGetValue(pair.Key, out var row))
            {
                row.SettingValue = pair.Value.Value;
                row.Remarks = pair.Value.Remarks;
            }
            else
            {
                await db.SystemSettings.AddAsync(new SystemSetting
                {
                    SettingKey = pair.Key,
                    SettingValue = pair.Value.Value,
                    Remarks = pair.Value.Remarks
                }, cancellationToken);
            }
        }

        await db.SaveChangesAsync(cancellationToken);
        return await GetSettingsAsync(cancellationToken);
    }

    private async Task<Dictionary<string, string>> LoadSettingsDictionaryAsync(CancellationToken cancellationToken)
    {
        var rows = await db.SystemSettings
            .AsNoTracking()
            .ToListAsync(cancellationToken);
        var dbSettings = rows.ToDictionary(x => x.SettingKey, x => x.SettingValue, StringComparer.OrdinalIgnoreCase);

        foreach (var pair in DefaultValues())
        {
            if (!dbSettings.ContainsKey(pair.Key))
                dbSettings[pair.Key] = pair.Value.Value;
        }

        return dbSettings;
    }

    private static ParkingSettingsDto BuildDto(Dictionary<string, string> settings) => new()
    {
        ParkingCapacity = new ParkingCapacitySettingsDto
        {
            TotalParkingCapacity = ReadInt(settings, "TotalParkingCapacity", 500),
            WarningLevelPercent = ReadInt(settings, "ParkingCapacityWarningLevelPercent", 80),
            CriticalLevelPercent = ReadInt(settings, "ParkingCapacityCriticalLevelPercent", 95),
            BlockEntryWhenParkingFull = ReadBool(settings, "BlockEntryWhenParkingFull", true)
        },
        Rates = new RateSettingsDto
        {
            DailyRatePerSlot = ReadDecimal(settings, "DailyRatePerSlot", 50m),
            WeeklyRatePerSlot = ReadDecimal(settings, "WeeklyRatePerSlot", 150m),
            MonthlyRatePerSlot = ReadDecimal(settings, "MonthlyRatePerSlot", 500m),
            OverstayDailyCharge = ReadDecimal(settings, "OverstayDailyCharge", 50m),
            DefaultVatPercent = ReadDecimal(settings, "DefaultVatPercent", 5m),
            BlockEntryIfPaymentDue = ReadBool(settings, "BlockEntryIfPaymentDue", false)
        },
        Barcode = new BarcodeSettingsDto
        {
            BarcodePrefix = ReadString(settings, "BarcodePrefix", "KP"),
            BarcodeSymbology = ReadString(settings, "BarcodeSymbology", "128"),
            BarcodePrinterName = ReadString(settings, "BarcodePrinterName", string.Empty),
            BarcodePrinterType = ReadString(settings, "BarcodePrinterType", "ThermalLabel"),
            BarcodePrinterLanguage = ReadString(settings, "BarcodePrinterLanguage", "TSPL"),
            BarcodePrintMode = ReadString(settings, "BarcodePrintMode", "Command"),
            BarcodeLabelWidthMm = ReadInt(settings, "BarcodeLabelWidthMm", 60),
            BarcodeLabelHeightMm = ReadInt(settings, "BarcodeLabelHeightMm", 35),
            BarcodeBarWidth = ReadInt(settings, "BarcodeBarWidth", 2),
            BarcodeBarHeight = ReadInt(settings, "BarcodeBarHeight", 78),
            BarcodeSymbolWidthMm = ReadInt(settings, "BarcodeSymbolWidthMm", 48),
            BarcodeSymbolHeightMm = ReadInt(settings, "BarcodeSymbolHeightMm", 12),
            BarcodeMarginLeftMm = ReadInt(settings, "BarcodeMarginLeftMm", 5),
            BarcodeMarginTopMm = ReadInt(settings, "BarcodeMarginTopMm", 2),
            BarcodeMarginRightMm = ReadInt(settings, "BarcodeMarginRightMm", 5),
            BarcodeMarginBottomMm = ReadInt(settings, "BarcodeMarginBottomMm", 2),
            BarcodePrinterDpi = ReadInt(settings, "BarcodePrinterDpi", 203),
            BarcodePrinterDirection = ReadInt(settings, "BarcodePrinterDirection", 1),
            BarcodePrintDensity = ReadInt(settings, "BarcodePrintDensity", 8),
            BarcodePrintCopies = ReadInt(settings, "BarcodePrintCopies", 1),
            BarcodeRotate90 = ReadBool(settings, "BarcodeRotate90", false),
            AutoPrintBarcodeAfterEntry = ReadBool(settings, "AutoPrintBarcodeAfterEntry", true),
            AllowBarcodeReprint = ReadBool(settings, "AllowBarcodeReprint", true),
            BarcodeShowHumanReadable = ReadBool(settings, "BarcodeShowHumanReadable", true),
            BarcodeLabelTitle = ReadString(settings, "BarcodeLabelTitle", "NETWORLD PARKING LOT"),
            BarcodeLabelNote = ReadString(settings, "BarcodeLabelNote", "One parking session only")
        },
        Invoice = new InvoiceSettingsDto
        {
            InvoicePrefix = ReadString(settings, "InvoicePrefix", "INV"),
            ReceiptPrefix = ReadString(settings, "ReceiptPrefix", "RCT"),
            InvoicePrinterName = ReadString(settings, "InvoicePrinterName", string.Empty),
            InvoicePrinterType = ReadString(settings, "InvoicePrinterType", "ESC/POS"),
            InvoicePrintCopies = ReadInt(settings, "InvoicePrintCopies", 1),
            InvoiceCompanyName = ReadString(settings, "InvoiceCompanyName", "NETWORLD SMART PARKING"),
            InvoiceTitle = ReadString(settings, "InvoiceTitle", "PARKING INVOICE"),
            InvoiceAddress = ReadString(settings, "InvoiceAddress", string.Empty),
            InvoiceTrn = ReadString(settings, "InvoiceTrn", string.Empty),
            InvoiceCurrency = ReadString(settings, "InvoiceCurrency", "AED"),
            InvoiceShowVatLine = ReadBool(settings, "InvoiceShowVatLine", true),
            InvoiceFooterText = ReadString(settings, "InvoiceFooterText", "Thank you"),
            InvoiceLogoPath = ReadString(settings, "InvoiceLogoPath", string.Empty)
        },
        OutsideDisplay = new OutsideDisplaySettingsDto
        {
            OutsideDisplayEnabled = ReadBool(settings, "OutsideDisplayEnabled", true),
            OutsideDisplayRefreshIntervalSeconds = ReadInt(settings, "OutsideDisplayRefreshIntervalSeconds", 1),
            OutsideDisplayAutoClearSeconds = ReadInt(settings, "OutsideDisplayAutoClearSeconds", 10),
            OutsideDisplayFullScreenMode = ReadBool(settings, "OutsideDisplayFullScreenMode", true),
            OutsideDisplayShowAmountDue = ReadBool(settings, "OutsideDisplayShowAmountDue", true),
            OutsideDisplayShowCompanyName = ReadBool(settings, "OutsideDisplayShowCompanyName", true),
            OutsideDisplayShowPlateNo = ReadBool(settings, "OutsideDisplayShowPlateNo", true),
            OutsideDisplayShowBarcodeNo = ReadBool(settings, "OutsideDisplayShowBarcodeNo", true),
            OutsideDisplayShowOverstayDays = ReadBool(settings, "OutsideDisplayShowOverstayDays", true),
            OutsideDisplayScreenTitle = ReadString(settings, "OutsideDisplayScreenTitle", "NETWORLD PARKING"),
            OutsideDisplayWaitingMessage = ReadString(settings, "OutsideDisplayWaitingMessage", "Scan a vehicle barcode to show the result."),
            OutsideDisplayEntryAllowedMainMessage = ReadString(settings, "OutsideDisplayEntryAllowedMainMessage", "ENTRY ALLOWED"),
            OutsideDisplayEntryAllowedSubMessage = ReadString(settings, "OutsideDisplayEntryAllowedSubMessage", "Please proceed inside."),
            OutsideDisplayClearToExitMainMessage = ReadString(settings, "OutsideDisplayClearToExitMainMessage", "CLEAR TO EXIT"),
            OutsideDisplayClearToExitSubMessage = ReadString(settings, "OutsideDisplayClearToExitSubMessage", "Please proceed."),
            OutsideDisplayPaymentRequiredMainMessage = ReadString(settings, "OutsideDisplayPaymentRequiredMainMessage", "PAYMENT REQUIRED"),
            OutsideDisplayPaymentRequiredSubMessage = ReadString(settings, "OutsideDisplayPaymentRequiredSubMessage", "Please park aside and clear pending amount."),
            OutsideDisplayOverstayMainMessage = ReadString(settings, "OutsideDisplayOverstayMainMessage", "OVERSTAY DETECTED"),
            OutsideDisplayOverstaySubMessage = ReadString(settings, "OutsideDisplayOverstaySubMessage", "Please park aside and clear payment."),
            OutsideDisplayInvalidMainMessage = ReadString(settings, "OutsideDisplayInvalidMainMessage", "INVALID BARCODE"),
            OutsideDisplayInvalidSubMessage = ReadString(settings, "OutsideDisplayInvalidSubMessage", "Please contact staff.")
        },
        BarcodeSymbologyOptions = BarcodeSymbologyOptions,
        BarcodePrinterLanguageOptions = BarcodePrinterLanguageOptions,
        PrinterTypeOptions = PrinterTypeOptions,
        BarcodePrintModeOptions = BarcodePrintModeOptions
    };

    private static void Validate(UpdateParkingSettingsRequest request)
    {
        if (request.ParkingCapacity.TotalParkingCapacity <= 0)
            throw new InvalidOperationException("Total parking capacity must be greater than zero.");
        if (request.ParkingCapacity.WarningLevelPercent is < 1 or > 100)
            throw new InvalidOperationException("Warning level must be between 1 and 100 percent.");
        if (request.ParkingCapacity.CriticalLevelPercent is < 1 or > 100)
            throw new InvalidOperationException("Critical level must be between 1 and 100 percent.");
        if (request.ParkingCapacity.CriticalLevelPercent < request.ParkingCapacity.WarningLevelPercent)
            throw new InvalidOperationException("Critical level must be greater than or equal to warning level.");

        if (request.Rates.DailyRatePerSlot < 0 || request.Rates.WeeklyRatePerSlot < 0 || request.Rates.MonthlyRatePerSlot < 0)
            throw new InvalidOperationException("Slot rates cannot be negative.");
        if (request.Rates.OverstayDailyCharge < 0)
            throw new InvalidOperationException("Overstay daily charge cannot be negative.");
        if (request.Rates.DefaultVatPercent is < 0 or > 100)
            throw new InvalidOperationException("Default VAT percentage must be between 0 and 100.");

        if (string.IsNullOrWhiteSpace(request.Barcode.BarcodePrefix))
            throw new InvalidOperationException("Barcode prefix is required.");
        if (string.IsNullOrWhiteSpace(request.Barcode.BarcodeSymbology))
            throw new InvalidOperationException("Barcode type/symbology is required.");
        if (request.Barcode.BarcodeLabelWidthMm is < 25 or > 120)
            throw new InvalidOperationException("Barcode label width must be between 25 and 120 mm.");
        if (request.Barcode.BarcodeLabelHeightMm is < 15 or > 80)
            throw new InvalidOperationException("Barcode label height must be between 15 and 80 mm.");
        if (request.Barcode.BarcodeBarWidth is < 1 or > 4)
            throw new InvalidOperationException("Barcode width must be between 1 and 4.");
        if (request.Barcode.BarcodeBarHeight is < 40 or > 220)
            throw new InvalidOperationException("Barcode height must be between 40 and 220 dots.");
        if (request.Barcode.BarcodeSymbolWidthMm is < 10 or > 110)
            throw new InvalidOperationException("Barcode width must be between 10 and 110 mm.");
        if (request.Barcode.BarcodeSymbolHeightMm is < 5 or > 50)
            throw new InvalidOperationException("Barcode height must be between 5 and 50 mm.");
        if (request.Barcode.BarcodeMarginLeftMm is < 0 or > 30 ||
            request.Barcode.BarcodeMarginTopMm is < 0 or > 30 ||
            request.Barcode.BarcodeMarginRightMm is < 0 or > 30 ||
            request.Barcode.BarcodeMarginBottomMm is < 0 or > 30)
            throw new InvalidOperationException("Barcode margins must be between 0 and 30 mm.");
        if (request.Barcode.BarcodeMarginLeftMm + request.Barcode.BarcodeMarginRightMm >= request.Barcode.BarcodeLabelWidthMm)
            throw new InvalidOperationException("Barcode left and right margins must be smaller than the label width.");
        if (request.Barcode.BarcodeMarginTopMm + request.Barcode.BarcodeMarginBottomMm >= request.Barcode.BarcodeLabelHeightMm)
            throw new InvalidOperationException("Barcode top and bottom margins must be smaller than the label height.");
        if (request.Barcode.BarcodeMarginLeftMm + request.Barcode.BarcodeMarginRightMm + request.Barcode.BarcodeSymbolWidthMm > request.Barcode.BarcodeLabelWidthMm)
            throw new InvalidOperationException("Barcode width plus left/right margins cannot be larger than the label width.");
        if (request.Barcode.BarcodeMarginTopMm + request.Barcode.BarcodeMarginBottomMm + request.Barcode.BarcodeSymbolHeightMm > request.Barcode.BarcodeLabelHeightMm)
            throw new InvalidOperationException("Barcode height plus top/bottom margins cannot be larger than the label height.");
        if (request.Barcode.BarcodePrinterDpi is < 150 or > 600)
            throw new InvalidOperationException("Barcode printer DPI must be between 150 and 600.");
        if (request.Barcode.BarcodePrinterDirection is < 0 or > 1)
            throw new InvalidOperationException("Barcode printer direction must be 0 or 1.");
        if (request.Barcode.BarcodePrintDensity is < 1 or > 15)
            throw new InvalidOperationException("Barcode print density must be between 1 and 15.");
        if (request.Barcode.BarcodePrintCopies is < 1 or > 5)
            throw new InvalidOperationException("Barcode print copies must be between 1 and 5.");

        if (string.IsNullOrWhiteSpace(request.Invoice.InvoicePrefix))
            throw new InvalidOperationException("Invoice prefix is required.");
        if (string.IsNullOrWhiteSpace(request.Invoice.ReceiptPrefix))
            throw new InvalidOperationException("Receipt prefix is required.");
        if (request.Invoice.InvoicePrintCopies is < 1 or > 5)
            throw new InvalidOperationException("Invoice print copies must be between 1 and 5.");

        if (request.OutsideDisplay.OutsideDisplayRefreshIntervalSeconds is < 1 or > 60)
            throw new InvalidOperationException("Outside display refresh interval must be between 1 and 60 seconds.");
        if (request.OutsideDisplay.OutsideDisplayAutoClearSeconds is < 3 or > 120)
            throw new InvalidOperationException("Outside display auto clear seconds must be between 3 and 120 seconds.");
    }

    private static Dictionary<string, SettingWriteValue> ToSettingValues(UpdateParkingSettingsRequest request)
    {
        var p = request.ParkingCapacity;
        var r = request.Rates;
        var b = request.Barcode;
        var i = request.Invoice;
        var d = request.OutsideDisplay;

        return new(StringComparer.OrdinalIgnoreCase)
        {
            ["TotalParkingCapacity"] = Value(p.TotalParkingCapacity, "Total physical parking capacity used by dashboard and entry validation"),
            ["ParkingCapacityWarningLevelPercent"] = Value(p.WarningLevelPercent, "Dashboard warning threshold for occupied spaces"),
            ["ParkingCapacityCriticalLevelPercent"] = Value(p.CriticalLevelPercent, "Dashboard critical threshold for occupied spaces"),
            ["BlockEntryWhenParkingFull"] = Value(p.BlockEntryWhenParkingFull, "Blocks new barcode/entry when physical parking capacity is full"),

            ["DailyRatePerSlot"] = Value(r.DailyRatePerSlot, "Default daily company/extra slot rate per slot"),
            ["WeeklyRatePerSlot"] = Value(r.WeeklyRatePerSlot, "Default weekly company/extra slot rate per slot"),
            ["MonthlyRatePerSlot"] = Value(r.MonthlyRatePerSlot, "Default monthly company/extra slot rate per slot"),
            ["OverstayDailyCharge"] = Value(r.OverstayDailyCharge, "Charge per overstay day"),
            ["DefaultVatPercent"] = Value(r.DefaultVatPercent, "Default VAT percentage for invoice forms"),
            ["BlockEntryIfPaymentDue"] = Value(r.BlockEntryIfPaymentDue, "Blocks barcode generation when a company has pending payment"),

            ["BarcodePrefix"] = Value(CleanPrefix(b.BarcodePrefix, "KP"), "Barcode number prefix"),
            ["BarcodeSymbology"] = Value(CleanText(b.BarcodeSymbology, 30, "128").ToUpperInvariant(), "Barcode type/symbology sent to the printer, for example 128, 39 or EAN13"),
            ["BarcodePrinterName"] = Value(CleanText(b.BarcodePrinterName, 200, string.Empty), "Installed Windows printer name for barcode labels"),
            ["BarcodePrinterType"] = Value(CleanText(b.BarcodePrinterType, 50, "ThermalLabel"), "Barcode printer type"),
            ["BarcodePrinterLanguage"] = Value(NormalizePrinterLanguage(b.BarcodePrinterLanguage), "Barcode printer command language: TSPL or ZPL"),
            ["BarcodePrintMode"] = Value(CleanText(b.BarcodePrintMode, 50, "Command"), "Barcode printing mode: Command or Image"),
            ["BarcodeLabelWidthMm"] = Value(b.BarcodeLabelWidthMm, "Barcode label width in millimeters"),
            ["BarcodeLabelHeightMm"] = Value(b.BarcodeLabelHeightMm, "Barcode label height in millimeters"),
            ["BarcodeBarWidth"] = Value(b.BarcodeBarWidth, "Barcode bar width/module size used in raw printer commands"),
            ["BarcodeBarHeight"] = Value(b.BarcodeBarHeight, "Barcode bar height in printer dots used in raw printer commands"),
            ["BarcodeSymbolWidthMm"] = Value(b.BarcodeSymbolWidthMm, "Barcode symbol width in millimeters"),
            ["BarcodeSymbolHeightMm"] = Value(b.BarcodeSymbolHeightMm, "Barcode symbol height in millimeters"),
            ["BarcodeMarginLeftMm"] = Value(b.BarcodeMarginLeftMm, "Barcode label left margin in millimeters"),
            ["BarcodeMarginTopMm"] = Value(b.BarcodeMarginTopMm, "Barcode label top margin in millimeters"),
            ["BarcodeMarginRightMm"] = Value(b.BarcodeMarginRightMm, "Barcode label right margin in millimeters"),
            ["BarcodeMarginBottomMm"] = Value(b.BarcodeMarginBottomMm, "Barcode label bottom margin in millimeters"),
            ["BarcodePrinterDpi"] = Value(b.BarcodePrinterDpi, "Barcode label printer DPI"),
            ["BarcodePrinterDirection"] = Value(b.BarcodePrinterDirection, "TSPL print direction 0 or 1"),
            ["BarcodePrintDensity"] = Value(b.BarcodePrintDensity, "Thermal barcode print density"),
            ["BarcodePrintCopies"] = Value(b.BarcodePrintCopies, "Default barcode print copies"),
            ["BarcodeRotate90"] = Value(b.BarcodeRotate90, "Rotate barcode image label by 90 degrees"),
            ["AutoPrintBarcodeAfterEntry"] = Value(b.AutoPrintBarcodeAfterEntry, "Automatically print barcode after generation in supported screens"),
            ["AllowBarcodeReprint"] = Value(b.AllowBarcodeReprint, "Allow reprinting barcode labels"),
            ["BarcodeShowHumanReadable"] = Value(b.BarcodeShowHumanReadable, "Show barcode number below barcode"),
            ["BarcodeLabelTitle"] = Value(CleanText(b.BarcodeLabelTitle, 80, "NETWORLD PARKING LOT"), "Barcode label title"),
            ["BarcodeLabelNote"] = Value(CleanText(b.BarcodeLabelNote, 120, "One parking session only"), "Barcode label footer note"),

            ["InvoicePrefix"] = Value(CleanPrefix(i.InvoicePrefix, "INV"), "Invoice number prefix"),
            ["ReceiptPrefix"] = Value(CleanPrefix(i.ReceiptPrefix, "RCT"), "Receipt number prefix"),
            ["InvoicePrinterName"] = Value(CleanText(i.InvoicePrinterName, 200, string.Empty), "Installed Windows printer name for invoice printing"),
            ["InvoicePrinterType"] = Value(CleanText(i.InvoicePrinterType, 50, "ESC/POS"), "Invoice printer type"),
            ["InvoicePrintCopies"] = Value(i.InvoicePrintCopies, "Default invoice print copies"),
            ["InvoiceCompanyName"] = Value(CleanText(i.InvoiceCompanyName, 100, "NETWORLD SMART PARKING"), "Invoice header company name"),
            ["InvoiceTitle"] = Value(CleanText(i.InvoiceTitle, 80, "PARKING INVOICE"), "Invoice title"),
            ["InvoiceAddress"] = Value(CleanText(i.InvoiceAddress, 250, string.Empty), "Invoice address line"),
            ["InvoiceTrn"] = Value(CleanText(i.InvoiceTrn, 100, string.Empty), "Invoice TRN/tax registration number"),
            ["InvoiceCurrency"] = Value(CleanText(i.InvoiceCurrency, 10, "AED").ToUpperInvariant(), "Invoice currency text"),
            ["InvoiceShowVatLine"] = Value(i.InvoiceShowVatLine, "Show VAT line on printed invoice"),
            ["InvoiceFooterText"] = Value(CleanText(i.InvoiceFooterText, 160, "Thank you"), "Invoice footer text"),
            ["InvoiceLogoPath"] = Value(CleanText(i.InvoiceLogoPath, 300, string.Empty), "Optional invoice logo path or URL"),

            ["OutsideDisplayEnabled"] = Value(d.OutsideDisplayEnabled, "Enable or disable outside display events"),
            ["OutsideDisplayRefreshIntervalSeconds"] = Value(d.OutsideDisplayRefreshIntervalSeconds, "Outside display polling interval in seconds"),
            ["OutsideDisplayAutoClearSeconds"] = Value(d.OutsideDisplayAutoClearSeconds, "Seconds before outside display returns to waiting screen"),
            ["OutsideDisplayFullScreenMode"] = Value(d.OutsideDisplayFullScreenMode, "Preferred full screen mode for outside display"),
            ["OutsideDisplayShowAmountDue"] = Value(d.OutsideDisplayShowAmountDue, "Show payable amount on outside display"),
            ["OutsideDisplayShowCompanyName"] = Value(d.OutsideDisplayShowCompanyName, "Show company name on outside display"),
            ["OutsideDisplayShowPlateNo"] = Value(d.OutsideDisplayShowPlateNo, "Show vehicle plate/reference on outside display"),
            ["OutsideDisplayShowBarcodeNo"] = Value(d.OutsideDisplayShowBarcodeNo, "Show barcode number on outside display"),
            ["OutsideDisplayShowOverstayDays"] = Value(d.OutsideDisplayShowOverstayDays, "Show overstay days on outside display"),
            ["OutsideDisplayScreenTitle"] = Value(CleanText(d.OutsideDisplayScreenTitle, 80, "NETWORLD PARKING"), "Outside display screen title"),
            ["OutsideDisplayWaitingMessage"] = Value(CleanText(d.OutsideDisplayWaitingMessage, 160, "Scan a vehicle barcode to show the result."), "Outside display waiting message"),
            ["OutsideDisplayEntryAllowedMainMessage"] = Value(CleanText(d.OutsideDisplayEntryAllowedMainMessage, 80, "ENTRY ALLOWED"), "Outside display entry allowed main message"),
            ["OutsideDisplayEntryAllowedSubMessage"] = Value(CleanText(d.OutsideDisplayEntryAllowedSubMessage, 160, "Please proceed inside."), "Outside display entry allowed sub message"),
            ["OutsideDisplayClearToExitMainMessage"] = Value(CleanText(d.OutsideDisplayClearToExitMainMessage, 80, "CLEAR TO EXIT"), "Outside display clear to exit main message"),
            ["OutsideDisplayClearToExitSubMessage"] = Value(CleanText(d.OutsideDisplayClearToExitSubMessage, 160, "Please proceed."), "Outside display clear to exit sub message"),
            ["OutsideDisplayPaymentRequiredMainMessage"] = Value(CleanText(d.OutsideDisplayPaymentRequiredMainMessage, 80, "PAYMENT REQUIRED"), "Outside display payment required main message"),
            ["OutsideDisplayPaymentRequiredSubMessage"] = Value(CleanText(d.OutsideDisplayPaymentRequiredSubMessage, 160, "Please park aside and clear pending amount."), "Outside display payment required sub message"),
            ["OutsideDisplayOverstayMainMessage"] = Value(CleanText(d.OutsideDisplayOverstayMainMessage, 80, "OVERSTAY DETECTED"), "Outside display overstay main message"),
            ["OutsideDisplayOverstaySubMessage"] = Value(CleanText(d.OutsideDisplayOverstaySubMessage, 160, "Please park aside and clear payment."), "Outside display overstay sub message"),
            ["OutsideDisplayInvalidMainMessage"] = Value(CleanText(d.OutsideDisplayInvalidMainMessage, 80, "INVALID BARCODE"), "Outside display invalid barcode main message"),
            ["OutsideDisplayInvalidSubMessage"] = Value(CleanText(d.OutsideDisplayInvalidSubMessage, 160, "Please contact staff."), "Outside display invalid barcode sub message")
        };
    }

    private static Dictionary<string, SettingWriteValue> DefaultValues()
    {
        var defaults = ToSettingValues(new UpdateParkingSettingsRequest());
        return defaults;
    }

    private static SettingWriteValue Value(int value, string remarks) => new(value.ToString(CultureInfo.InvariantCulture), remarks);
    private static SettingWriteValue Value(decimal value, string remarks) => new(value.ToString("0.##", CultureInfo.InvariantCulture), remarks);
    private static SettingWriteValue Value(bool value, string remarks) => new(value ? "true" : "false", remarks);
    private static SettingWriteValue Value(string value, string remarks) => new(value, remarks);

    private static string ReadString(Dictionary<string, string> settings, string key, string fallback) =>
        settings.TryGetValue(key, out var value) && !string.IsNullOrWhiteSpace(value) ? value.Trim() : fallback;

    private static int ReadInt(Dictionary<string, string> settings, string key, int fallback) =>
        settings.TryGetValue(key, out var value) && int.TryParse(value, NumberStyles.Integer, CultureInfo.InvariantCulture, out var parsed) ? parsed : fallback;

    private static decimal ReadDecimal(Dictionary<string, string> settings, string key, decimal fallback) =>
        settings.TryGetValue(key, out var value) && decimal.TryParse(value, NumberStyles.Number, CultureInfo.InvariantCulture, out var parsed) ? parsed : fallback;

    private static bool ReadBool(Dictionary<string, string> settings, string key, bool fallback)
    {
        if (!settings.TryGetValue(key, out var value) || string.IsNullOrWhiteSpace(value)) return fallback;
        if (bool.TryParse(value, out var parsed)) return parsed;
        return value.Trim() is "1" or "yes" or "YES" or "Yes";
    }

    private static string CleanText(string? value, int maxLength, string fallback)
    {
        var clean = (value ?? string.Empty).Trim();
        if (clean.Length == 0) clean = fallback;
        clean = clean.Replace("\r", " ").Replace("\n", " ");
        return clean.Length <= maxLength ? clean : clean[..maxLength];
    }

    private static string CleanPrefix(string? value, string fallback)
    {
        var clean = new string((value ?? string.Empty).Trim().ToUpperInvariant().Where(c => char.IsLetterOrDigit(c) || c == '-').ToArray());
        if (clean.Length == 0) clean = fallback;
        return clean.Length <= 12 ? clean : clean[..12];
    }

    private static string NormalizePrinterLanguage(string? value)
    {
        var clean = CleanText(value, 20, "TSPL").ToUpperInvariant();
        return clean == "ZPL" ? "ZPL" : "TSPL";
    }

    private sealed record SettingWriteValue(string Value, string Remarks);
}

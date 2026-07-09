namespace NetworldParkingLot.Api.Features.Settings.Dtos;

public sealed record SettingsOptionDto(string Value, string Text, string? Description = null);

public sealed class ParkingSettingsDto
{
    public ParkingCapacitySettingsDto ParkingCapacity { get; set; } = new();
    public RateSettingsDto Rates { get; set; } = new();
    public BarcodeSettingsDto Barcode { get; set; } = new();
    public InvoiceSettingsDto Invoice { get; set; } = new();
    public OutsideDisplaySettingsDto OutsideDisplay { get; set; } = new();
    public IReadOnlyList<SettingsOptionDto> BarcodeSymbologyOptions { get; set; } = [];
    public IReadOnlyList<SettingsOptionDto> BarcodePrinterLanguageOptions { get; set; } = [];
    public IReadOnlyList<SettingsOptionDto> PrinterTypeOptions { get; set; } = [];
    public IReadOnlyList<SettingsOptionDto> BarcodePrintModeOptions { get; set; } = [];
}

public sealed class UpdateParkingSettingsRequest
{
    public ParkingCapacitySettingsDto ParkingCapacity { get; set; } = new();
    public RateSettingsDto Rates { get; set; } = new();
    public BarcodeSettingsDto Barcode { get; set; } = new();
    public InvoiceSettingsDto Invoice { get; set; } = new();
    public OutsideDisplaySettingsDto OutsideDisplay { get; set; } = new();
}

public sealed class ParkingCapacitySettingsDto
{
    public int TotalParkingCapacity { get; set; } = 500;
    public int WarningLevelPercent { get; set; } = 80;
    public int CriticalLevelPercent { get; set; } = 95;
    public bool BlockEntryWhenParkingFull { get; set; } = true;
}

public sealed class RateSettingsDto
{
    public decimal DailyRatePerSlot { get; set; } = 50m;
    public decimal WeeklyRatePerSlot { get; set; } = 150m;
    public decimal MonthlyRatePerSlot { get; set; } = 500m;
    public decimal OverstayDailyCharge { get; set; } = 50m;
    public bool VatEnabled { get; set; } = true;
    public decimal DefaultVatPercent { get; set; } = 5m;
    public string DefaultVatMode { get; set; } = "Exclusive";
    public bool BlockEntryIfPaymentDue { get; set; }
}

public sealed class BarcodeSettingsDto
{
    public string BarcodePrefix { get; set; } = "KP";
    public string BarcodeSymbology { get; set; } = "128";
    public string BarcodePrinterName { get; set; } = string.Empty;
    public string BarcodePrinterType { get; set; } = "ThermalLabel";
    public string BarcodePrinterLanguage { get; set; } = "ZPL";
    public string BarcodePrintMode { get; set; } = "Command";
    public int BarcodeLabelWidthMm { get; set; } = 100;
    public int BarcodeLabelHeightMm { get; set; } = 110;
    public int BarcodeBarWidth { get; set; } = 2;
    public int BarcodeBarHeight { get; set; } = 78;
    public int BarcodeSymbolWidthMm { get; set; } = 90;
    public int BarcodeSymbolHeightMm { get; set; } = 35;
    public int BarcodeMarginLeftMm { get; set; } = 5;
    public int BarcodeMarginTopMm { get; set; } = 5;
    public int BarcodeMarginRightMm { get; set; } = 5;
    public int BarcodeMarginBottomMm { get; set; } = 5;
    public int BarcodePrinterDpi { get; set; } = 203;
    public int BarcodePrinterDirection { get; set; } = 1;
    public int BarcodePrintDensity { get; set; } = 8;
    public int BarcodePrintCopies { get; set; } = 1;
    public int BarcodeTextScalePercent { get; set; } = 100;
    public int BarcodeSymbolScalePercent { get; set; } = 100;
    public bool BarcodeRotate90 { get; set; }
    public bool AutoPrintBarcodeAfterEntry { get; set; } = true;
    public bool AllowBarcodeReprint { get; set; } = true;
    public bool BarcodeShowHumanReadable { get; set; } = true;
    public string BarcodeLabelTitle { get; set; } = "NETWORLD SMART PARKING";
    public string BarcodeLabelNote { get; set; } = "One parking session only";
}

public sealed class InvoiceSettingsDto
{
    public string InvoicePrefix { get; set; } = "INV";
    public string ReceiptPrefix { get; set; } = "RCT";
    public string InvoicePrinterName { get; set; } = string.Empty;
    public string InvoicePrinterType { get; set; } = "ESC/POS";
    public int InvoicePrintCopies { get; set; } = 1;
    public string InvoiceCompanyName { get; set; } = "NETWORLD SMART PARKING";
    public string InvoiceTitle { get; set; } = "PARKING INVOICE";
    public string InvoiceAddress { get; set; } = string.Empty;
    public string InvoiceTrn { get; set; } = string.Empty;
    public string InvoiceCurrency { get; set; } = "AED";
    public bool InvoiceShowVatLine { get; set; } = true;
    public string InvoiceFooterText { get; set; } = "Thank you";
    public string InvoiceLogoPath { get; set; } = string.Empty;
}

public sealed class OutsideDisplaySettingsDto
{
    public bool OutsideDisplayEnabled { get; set; } = true;
    public int OutsideDisplayRefreshIntervalSeconds { get; set; } = 1;
    public int OutsideDisplayAutoClearSeconds { get; set; } = 10;
    public bool OutsideDisplayFullScreenMode { get; set; } = true;
    public bool OutsideDisplayShowAmountDue { get; set; } = true;
    public bool OutsideDisplayShowCompanyName { get; set; } = true;
    public bool OutsideDisplayShowPlateNo { get; set; } = true;
    public bool OutsideDisplayShowBarcodeNo { get; set; } = true;
    public bool OutsideDisplayShowOverstayDays { get; set; } = true;
    public string OutsideDisplayScreenTitle { get; set; } = "NETWORLD PARKING";
    public string OutsideDisplayWaitingMessage { get; set; } = "Scan a vehicle barcode to show the result.";
    public string OutsideDisplayEntryAllowedMainMessage { get; set; } = "ENTRY ALLOWED";
    public string OutsideDisplayEntryAllowedSubMessage { get; set; } = "Please proceed inside.";
    public string OutsideDisplayClearToExitMainMessage { get; set; } = "CLEAR TO EXIT";
    public string OutsideDisplayClearToExitSubMessage { get; set; } = "Please proceed.";
    public string OutsideDisplayPaymentRequiredMainMessage { get; set; } = "PAYMENT REQUIRED";
    public string OutsideDisplayPaymentRequiredSubMessage { get; set; } = "Please park aside and clear pending amount.";
    public string OutsideDisplayOverstayMainMessage { get; set; } = "OVERSTAY DETECTED";
    public string OutsideDisplayOverstaySubMessage { get; set; } = "Please park aside and clear payment.";
    public string OutsideDisplayInvalidMainMessage { get; set; } = "INVALID BARCODE";
    public string OutsideDisplayInvalidSubMessage { get; set; } = "Please contact staff.";
}

public sealed record ParkingRatePlanDto(
    int RatePlanId,
    string PlanName,
    string PeriodUnit,
    int PeriodValue,
    decimal RatePerSlot,
    bool IsSystemDefault,
    bool IsActive,
    int SortOrder,
    string? Remarks);

public sealed class SaveParkingRatePlanRequest
{
    public string PlanName { get; set; } = string.Empty;
    public string PeriodUnit { get; set; } = "Days";
    public int PeriodValue { get; set; } = 1;
    public decimal RatePerSlot { get; set; }
    public bool IsActive { get; set; } = true;
    public int SortOrder { get; set; }
    public string? Remarks { get; set; }
}

public sealed record ParkingVehicleTypeDto(
    int VehicleTypeId,
    string VehicleTypeName,
    string? Description,
    bool IsActive,
    int SortOrder);

public sealed class SaveParkingVehicleTypeRequest
{
    public string VehicleTypeName { get; set; } = string.Empty;
    public string? Description { get; set; }
    public bool IsActive { get; set; } = true;
    public int SortOrder { get; set; }
}

public sealed record ParkingBankAccountDto(
    int BankAccountId,
    string BankName,
    string? AccountName,
    string? AccountNumber,
    string? Iban,
    string? BranchName,
    bool IsActive,
    int SortOrder,
    string? Remarks);

public sealed class SaveParkingBankAccountRequest
{
    public string BankName { get; set; } = string.Empty;
    public string? AccountName { get; set; }
    public string? AccountNumber { get; set; }
    public string? Iban { get; set; }
    public string? BranchName { get; set; }
    public bool IsActive { get; set; } = true;
    public int SortOrder { get; set; }
    public string? Remarks { get; set; }
}

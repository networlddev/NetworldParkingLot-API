/*
NETWORLD SMART PARKING - Full Settings Module
Run this script after the previous SQL patches.
It seeds editable system settings and adds Settings permissions.
*/

USE NetworldParkingLot;
GO

DECLARE @Settings TABLE
(
    SettingKey NVARCHAR(100) NOT NULL PRIMARY KEY,
    SettingValue NVARCHAR(500) NOT NULL,
    Remarks NVARCHAR(500) NULL
);

INSERT INTO @Settings (SettingKey, SettingValue, Remarks) VALUES
('TotalParkingCapacity', '500', 'Total physical parking capacity used by dashboard and entry validation'),
('ParkingCapacityWarningLevelPercent', '80', 'Dashboard warning threshold for occupied spaces'),
('ParkingCapacityCriticalLevelPercent', '95', 'Dashboard critical threshold for occupied spaces'),
('BlockEntryWhenParkingFull', 'true', 'Blocks new barcode/entry when physical parking capacity is full'),

('DailyRatePerSlot', '50', 'Default daily company/extra slot rate per slot'),
('WeeklyRatePerSlot', '150', 'Default weekly company/extra slot rate per slot'),
('MonthlyRatePerSlot', '500', 'Default monthly company/extra slot rate per slot'),
('OverstayDailyCharge', '50', 'Charge per overstay day'),
('DefaultVatPercent', '5', 'Default VAT percentage for invoice forms'),
('BlockEntryIfPaymentDue', 'false', 'Blocks barcode generation when a company has pending payment'),

('BarcodePrefix', 'KP', 'Barcode number prefix'),
('BarcodeSymbology', '128', 'Barcode type/symbology. Default is Code 128'),
('BarcodePrinterName', '', 'Installed Windows printer name for barcode labels'),
('BarcodePrinterType', 'ThermalLabel', 'Barcode printer type'),
('BarcodePrinterLanguage', 'ZPL', 'Barcode printer command language: TSPL or ZPL'),
('BarcodePrintMode', 'Command', 'Barcode printing mode: Command or Image'),
('BarcodeLabelWidthMm', '100', 'Barcode label width in millimeters'),
('BarcodeLabelHeightMm', '110', 'Barcode label height in millimeters'),
('BarcodePrinterDpi', '203', 'Barcode label printer DPI'),
('BarcodePrinterDirection', '1', 'TSPL print direction 0 or 1'),
('BarcodePrintDensity', '8', 'Thermal barcode print density'),
('BarcodePrintCopies', '1', 'Default barcode print copies'),
('BarcodeRotate90', 'false', 'Rotate barcode image label by 90 degrees'),
('AutoPrintBarcodeAfterEntry', 'true', 'Automatically print barcode after generation in supported screens'),
('AllowBarcodeReprint', 'true', 'Allow reprinting barcode labels'),
('BarcodeShowHumanReadable', 'true', 'Show barcode number below barcode'),
('BarcodeLabelTitle', 'NETWORLD SMART PARKING', 'Barcode label title'),
('BarcodeLabelNote', 'One parking session only', 'Barcode label footer note'),

('InvoicePrefix', 'INV', 'Invoice number prefix'),
('ReceiptPrefix', 'RCT', 'Receipt number prefix'),
('InvoicePrinterName', '', 'Installed Windows printer name for invoice printing'),
('InvoicePrinterType', 'ESC/POS', 'Invoice printer type'),
('InvoicePrintCopies', '1', 'Default invoice print copies'),
('InvoiceCompanyName', 'NETWORLD SMART PARKING', 'Invoice header company name'),
('InvoiceTitle', 'PARKING INVOICE', 'Invoice title'),
('InvoiceAddress', '', 'Invoice address line'),
('InvoiceTrn', '', 'Invoice TRN/tax registration number'),
('InvoiceCurrency', 'AED', 'Invoice currency text'),
('InvoiceShowVatLine', 'true', 'Show VAT line on printed invoice'),
('InvoiceFooterText', 'Thank you', 'Invoice footer text'),
('InvoiceLogoPath', '', 'Optional invoice logo path or URL'),

('OutsideDisplayEnabled', 'true', 'Enable or disable outside display events'),
('OutsideDisplayRefreshIntervalSeconds', '1', 'Outside display polling interval in seconds'),
('OutsideDisplayAutoClearSeconds', '10', 'Seconds before outside display returns to waiting screen'),
('OutsideDisplayFullScreenMode', 'true', 'Preferred full screen mode for outside display'),
('OutsideDisplayShowAmountDue', 'true', 'Show payable amount on outside display'),
('OutsideDisplayShowCompanyName', 'true', 'Show company name on outside display'),
('OutsideDisplayShowPlateNo', 'true', 'Show vehicle plate/reference on outside display'),
('OutsideDisplayShowBarcodeNo', 'true', 'Show barcode number on outside display'),
('OutsideDisplayShowOverstayDays', 'true', 'Show overstay days on outside display'),
('OutsideDisplayScreenTitle', 'NETWORLD PARKING', 'Outside display screen title'),
('OutsideDisplayWaitingMessage', 'Scan a vehicle barcode to show the result.', 'Outside display waiting message'),
('OutsideDisplayEntryAllowedMainMessage', 'ENTRY ALLOWED', 'Outside display entry allowed main message'),
('OutsideDisplayEntryAllowedSubMessage', 'Please proceed inside.', 'Outside display entry allowed sub message'),
('OutsideDisplayClearToExitMainMessage', 'CLEAR TO EXIT', 'Outside display clear to exit main message'),
('OutsideDisplayClearToExitSubMessage', 'Please proceed.', 'Outside display clear to exit sub message'),
('OutsideDisplayPaymentRequiredMainMessage', 'PAYMENT REQUIRED', 'Outside display payment required main message'),
('OutsideDisplayPaymentRequiredSubMessage', 'Please park aside and clear pending amount.', 'Outside display payment required sub message'),
('OutsideDisplayOverstayMainMessage', 'OVERSTAY DETECTED', 'Outside display overstay main message'),
('OutsideDisplayOverstaySubMessage', 'Please park aside and clear payment.', 'Outside display overstay sub message'),
('OutsideDisplayInvalidMainMessage', 'INVALID BARCODE', 'Outside display invalid barcode main message'),
('OutsideDisplayInvalidSubMessage', 'Please contact staff.', 'Outside display invalid barcode sub message');

MERGE dbo.SystemSettings AS target
USING @Settings AS source
    ON target.SettingKey = source.SettingKey
WHEN MATCHED THEN
    UPDATE SET Remarks = source.Remarks
WHEN NOT MATCHED THEN
    INSERT (SettingKey, SettingValue, Remarks)
    VALUES (source.SettingKey, source.SettingValue, source.Remarks);
GO

IF OBJECT_ID('dbo.AppModules','U') IS NOT NULL
   AND OBJECT_ID('dbo.AppModuleActions','U') IS NOT NULL
BEGIN
    IF NOT EXISTS (SELECT 1 FROM dbo.AppModules WHERE ModuleKey = 'settings')
    BEGIN
        INSERT INTO dbo.AppModules (ModuleKey, ModuleName, SortOrder, Active)
        VALUES ('settings', 'Settings', 90, 1);
    END

    DECLARE @SettingsModuleId INT = (SELECT TOP 1 ModuleId FROM dbo.AppModules WHERE ModuleKey = 'settings');

    IF @SettingsModuleId IS NOT NULL
    BEGIN
        IF NOT EXISTS (SELECT 1 FROM dbo.AppModuleActions WHERE ModuleId = @SettingsModuleId AND ActionKey = 'view')
            INSERT INTO dbo.AppModuleActions (ModuleId, ActionKey, ActionName, SortOrder, Active)
            VALUES (@SettingsModuleId, 'view', 'View Settings', 1, 1);

        IF NOT EXISTS (SELECT 1 FROM dbo.AppModuleActions WHERE ModuleId = @SettingsModuleId AND ActionKey = 'edit')
            INSERT INTO dbo.AppModuleActions (ModuleId, ActionKey, ActionName, SortOrder, Active)
            VALUES (@SettingsModuleId, 'edit', 'Edit Settings', 2, 1);

        IF OBJECT_ID('dbo.AppRoles','U') IS NOT NULL AND OBJECT_ID('dbo.AppRolePermissions','U') IS NOT NULL
        BEGIN
            INSERT INTO dbo.AppRolePermissions (RoleId, ModuleId, ActionKey, IsAllowed)
            SELECT r.RoleId, @SettingsModuleId, a.ActionKey, 1
            FROM dbo.AppRoles r
            CROSS JOIN (VALUES ('view'), ('edit')) a(ActionKey)
            WHERE r.Active = 1
              AND LOWER(r.RoleKey) IN ('super_admin','superadmin','admin','owner','manager')
              AND NOT EXISTS
              (
                  SELECT 1
                  FROM dbo.AppRolePermissions p
                  WHERE p.RoleId = r.RoleId
                    AND p.ModuleId = @SettingsModuleId
                    AND p.ActionKey = a.ActionKey
              );

            INSERT INTO dbo.AppRolePermissions (RoleId, ModuleId, ActionKey, IsAllowed)
            SELECT r.RoleId, @SettingsModuleId, 'view', 1
            FROM dbo.AppRoles r
            WHERE r.Active = 1
              AND LOWER(r.RoleKey) IN ('viewer','operator')
              AND NOT EXISTS
              (
                  SELECT 1
                  FROM dbo.AppRolePermissions p
                  WHERE p.RoleId = r.RoleId
                    AND p.ModuleId = @SettingsModuleId
                    AND p.ActionKey = 'view'
              );
        END
    END
END
GO

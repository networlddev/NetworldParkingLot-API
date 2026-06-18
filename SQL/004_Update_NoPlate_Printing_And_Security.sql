/*
Run this after the original gate operation scripts.
Purpose:
1. Vehicle plate/reference is now optional.
2. Existing systems can store NULL for vehicles without plates.
3. Printing is handled by API endpoints and Windows printer spooler; no extra DB table required for MVP.
*/

IF COL_LENGTH('dbo.ParkingSessions', 'PlateNo') IS NOT NULL
BEGIN
    ALTER TABLE dbo.ParkingSessions ALTER COLUMN PlateNo NVARCHAR(30) NULL;
END
GO

IF COL_LENGTH('dbo.GateActivityLogs', 'PlateNo') IS NOT NULL
BEGIN
    ALTER TABLE dbo.GateActivityLogs ALTER COLUMN PlateNo NVARCHAR(30) NULL;
END
GO

IF COL_LENGTH('dbo.OutsideDisplayEvents', 'PlateNo') IS NOT NULL
BEGIN
    ALTER TABLE dbo.OutsideDisplayEvents ALTER COLUMN PlateNo NVARCHAR(30) NULL;
END
GO

-- Optional: add default printer settings for the UI/API settings screen later.
IF NOT EXISTS (SELECT 1 FROM dbo.SystemSettings WHERE SettingKey = 'BarcodePrinterName')
BEGIN
    INSERT INTO dbo.SystemSettings (SettingKey, SettingValue, Description)
    VALUES ('BarcodePrinterName', 'Networld Barcode Printer', 'Installed Windows printer name for barcode label printing');
END
GO

IF NOT EXISTS (SELECT 1 FROM dbo.SystemSettings WHERE SettingKey = 'InvoicePrinterName')
BEGIN
    INSERT INTO dbo.SystemSettings (SettingKey, SettingValue, Description)
    VALUES ('InvoicePrinterName', 'Networld Invoice Printer', 'Installed Windows printer name for invoice/receipt printing');
END
GO

IF NOT EXISTS (SELECT 1 FROM dbo.SystemSettings WHERE SettingKey = 'BarcodePrinterLanguage')
BEGIN
    INSERT INTO dbo.SystemSettings (SettingKey, SettingValue, Description)
    VALUES ('BarcodePrinterLanguage', 'ZPL', 'Barcode printer command language: TSPL or ZPL');
END
GO

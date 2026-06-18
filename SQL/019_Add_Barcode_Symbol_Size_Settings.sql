/*
Adds physical barcode symbol width/height controls in millimeters.
Run after 018_Add_Barcode_Margin_Settings.sql.
*/

IF OBJECT_ID('dbo.SystemSettings', 'U') IS NOT NULL
BEGIN
    IF NOT EXISTS (SELECT 1 FROM dbo.SystemSettings WHERE SettingKey = 'BarcodeSymbolWidthMm')
    BEGIN
        INSERT INTO dbo.SystemSettings (SettingKey, SettingValue, Remarks)
        VALUES ('BarcodeSymbolWidthMm', '90', 'Barcode symbol width in millimeters');
    END

    IF NOT EXISTS (SELECT 1 FROM dbo.SystemSettings WHERE SettingKey = 'BarcodeSymbolHeightMm')
    BEGIN
        INSERT INTO dbo.SystemSettings (SettingKey, SettingValue, Remarks)
        VALUES ('BarcodeSymbolHeightMm', '35', 'Barcode symbol height in millimeters');
    END
END

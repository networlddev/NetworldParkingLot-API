/*
Adds barcode label margin controls used by image labels and TSPL/ZPL raw printer commands.
Run after 017_Add_Barcode_Bar_Size_Settings.sql.
*/

IF OBJECT_ID('dbo.SystemSettings', 'U') IS NOT NULL
BEGIN
    IF NOT EXISTS (SELECT 1 FROM dbo.SystemSettings WHERE SettingKey = 'BarcodeMarginLeftMm')
    BEGIN
        INSERT INTO dbo.SystemSettings (SettingKey, SettingValue, Remarks)
        VALUES ('BarcodeMarginLeftMm', '5', 'Barcode label left margin in millimeters');
    END

    IF NOT EXISTS (SELECT 1 FROM dbo.SystemSettings WHERE SettingKey = 'BarcodeMarginTopMm')
    BEGIN
        INSERT INTO dbo.SystemSettings (SettingKey, SettingValue, Remarks)
        VALUES ('BarcodeMarginTopMm', '2', 'Barcode label top margin in millimeters');
    END

    IF NOT EXISTS (SELECT 1 FROM dbo.SystemSettings WHERE SettingKey = 'BarcodeMarginRightMm')
    BEGIN
        INSERT INTO dbo.SystemSettings (SettingKey, SettingValue, Remarks)
        VALUES ('BarcodeMarginRightMm', '5', 'Barcode label right margin in millimeters');
    END

    IF NOT EXISTS (SELECT 1 FROM dbo.SystemSettings WHERE SettingKey = 'BarcodeMarginBottomMm')
    BEGIN
        INSERT INTO dbo.SystemSettings (SettingKey, SettingValue, Remarks)
        VALUES ('BarcodeMarginBottomMm', '2', 'Barcode label bottom margin in millimeters');
    END
END

/*
Adds barcode symbol width/height controls used by TSPL/ZPL raw printer commands.
Run after 016_Full_Settings_Module.sql.
*/

IF OBJECT_ID('dbo.SystemSettings', 'U') IS NOT NULL
BEGIN
    IF NOT EXISTS (SELECT 1 FROM dbo.SystemSettings WHERE SettingKey = 'BarcodeBarWidth')
    BEGIN
        INSERT INTO dbo.SystemSettings (SettingKey, SettingValue, Remarks)
        VALUES ('BarcodeBarWidth', '2', 'Barcode bar width/module size used in raw printer commands');
    END

    IF NOT EXISTS (SELECT 1 FROM dbo.SystemSettings WHERE SettingKey = 'BarcodeBarHeight')
    BEGIN
        INSERT INTO dbo.SystemSettings (SettingKey, SettingValue, Remarks)
        VALUES ('BarcodeBarHeight', '78', 'Barcode bar height in printer dots used in raw printer commands');
    END
END

IF NOT EXISTS (SELECT 1 FROM dbo.SystemSettings WHERE SettingKey = 'BarcodeLabelWidthMm')
BEGIN
    INSERT INTO dbo.SystemSettings (SettingKey, SettingValue, Remarks)
    VALUES ('BarcodeLabelWidthMm', '100', 'Barcode image label width in millimeters');
END
GO

IF NOT EXISTS (SELECT 1 FROM dbo.SystemSettings WHERE SettingKey = 'BarcodeLabelHeightMm')
BEGIN
    INSERT INTO dbo.SystemSettings (SettingKey, SettingValue, Remarks)
    VALUES ('BarcodeLabelHeightMm', '110', 'Barcode image label height in millimeters');
END
GO

IF NOT EXISTS (SELECT 1 FROM dbo.SystemSettings WHERE SettingKey = 'BarcodePrinterDpi')
BEGIN
    INSERT INTO dbo.SystemSettings (SettingKey, SettingValue, Remarks)
    VALUES ('BarcodePrinterDpi', '203', 'Barcode label printer DPI. Most thermal label printers use 203 DPI.');
END
GO

IF NOT EXISTS (SELECT 1 FROM dbo.SystemSettings WHERE SettingKey = 'BarcodeRotate90')
BEGIN
    INSERT INTO dbo.SystemSettings (SettingKey, SettingValue, Remarks)
    VALUES ('BarcodeRotate90', 'false', 'Set true if printer driver uses portrait 35x60 label and output must be rotated.');
END
GO

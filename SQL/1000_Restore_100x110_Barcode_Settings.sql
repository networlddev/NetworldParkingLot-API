/*
Restores the deployment barcode sticker settings after a database reset.
Keeps the large 100 x 110 mm Zebra/ZPL label with a readable, focused layout.
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
('BarcodePrinterName', 'ZDesigner ZT231-203dpi ZPL IP.88', 'Installed Windows printer name for barcode labels'),
('BarcodePrinterLanguage', 'ZPL', 'Barcode printer command language: TSPL or ZPL'),
('BarcodePrintMode', 'Command', 'Barcode printing mode: Command or Image'),
('BarcodeLabelWidthMm', '100', 'Barcode label width in millimeters'),
('BarcodeLabelHeightMm', '110', 'Barcode label height in millimeters'),
('BarcodeSymbolWidthMm', '90', 'Barcode symbol width in millimeters'),
('BarcodeSymbolHeightMm', '35', 'Barcode symbol height in millimeters'),
('BarcodeMarginLeftMm', '5', 'Barcode label left margin in millimeters'),
('BarcodeMarginTopMm', '5', 'Barcode label top margin in millimeters'),
('BarcodeMarginRightMm', '5', 'Barcode label right margin in millimeters'),
('BarcodeMarginBottomMm', '5', 'Barcode label bottom margin in millimeters'),
('BarcodeTextScalePercent', '100', 'Barcode label content text scale percentage'),
('BarcodeSymbolScalePercent', '100', 'Barcode symbol scale percentage'),
('BarcodePrinterDpi', '203', 'Barcode label printer DPI'),
('BarcodePrintDensity', '8', 'Thermal barcode print density'),
('BarcodeShowHumanReadable', 'true', 'Show barcode number below barcode'),
('BarcodeRotate90', 'false', 'Rotate barcode image label by 90 degrees'),
('BarcodeLabelTitle', 'NETWORLD SMART PARKING', 'Barcode label title'),
('BarcodeLabelNote', 'One parking session only', 'Barcode label footer note');

MERGE dbo.SystemSettings AS target
USING @Settings AS source
    ON target.SettingKey = source.SettingKey
WHEN MATCHED THEN
    UPDATE SET
        SettingValue = source.SettingValue,
        Remarks = source.Remarks
WHEN NOT MATCHED THEN
    INSERT (SettingKey, SettingValue, Remarks)
    VALUES (source.SettingKey, source.SettingValue, source.Remarks);
GO

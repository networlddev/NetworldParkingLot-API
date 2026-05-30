USE [NetworldParkingLot]
GO

/*
    Optional performance indexes for Live Parking, Payments and Vehicles/Barcode modules.
    Run after table creation scripts. Safe to re-run.
*/

IF NOT EXISTS (SELECT 1 FROM sys.indexes WHERE name = 'IX_ParkingSessions_Status_BarcodeStatus_EntryTime' AND object_id = OBJECT_ID('dbo.ParkingSessions'))
BEGIN
    CREATE INDEX IX_ParkingSessions_Status_BarcodeStatus_EntryTime
    ON dbo.ParkingSessions (Status, BarcodeStatus, EntryTime)
    INCLUDE (CompanyId, SubscriptionId, BarcodeNo, PlateNo, VehicleType, ExitTime, CreatedDate, OverstayDays, OverstayAmount);
END
GO

IF NOT EXISTS (SELECT 1 FROM sys.indexes WHERE name = 'IX_ParkingSessions_Company_CreatedDate' AND object_id = OBJECT_ID('dbo.ParkingSessions'))
BEGIN
    CREATE INDEX IX_ParkingSessions_Company_CreatedDate
    ON dbo.ParkingSessions (CompanyId, CreatedDate DESC)
    INCLUDE (BarcodeNo, PlateNo, Status, BarcodeStatus, EntryTime, ExitTime);
END
GO

IF NOT EXISTS (SELECT 1 FROM sys.indexes WHERE name = 'IX_ParkingPayments_PaymentDate_Company' AND object_id = OBJECT_ID('dbo.ParkingPayments'))
BEGIN
    CREATE INDEX IX_ParkingPayments_PaymentDate_Company
    ON dbo.ParkingPayments (PaymentDate DESC, CompanyId)
    INCLUDE (ReceiptNo, InvoiceId, SessionId, PaymentType, Amount, PaymentMode, ReferenceNo, ReceivedBy);
END
GO

IF NOT EXISTS (SELECT 1 FROM sys.indexes WHERE name = 'IX_ParkingPayments_Invoice_Session' AND object_id = OBJECT_ID('dbo.ParkingPayments'))
BEGIN
    CREATE INDEX IX_ParkingPayments_Invoice_Session
    ON dbo.ParkingPayments (InvoiceId, SessionId)
    INCLUDE (ReceiptNo, CompanyId, Amount, PaymentMode, PaymentDate);
END
GO

IF NOT EXISTS (SELECT 1 FROM sys.indexes WHERE name = 'IX_GateActivityLogs_Session_Barcode_Date' AND object_id = OBJECT_ID('dbo.GateActivityLogs'))
BEGIN
    CREATE INDEX IX_GateActivityLogs_Session_Barcode_Date
    ON dbo.GateActivityLogs (SessionId, BarcodeNo, ActionDate DESC)
    INCLUDE (ActionType, Status, Message, OperatorId);
END
GO

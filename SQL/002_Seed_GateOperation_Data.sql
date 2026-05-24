USE NetworldParkingLot;
GO

INSERT INTO dbo.AppUsers (Username, PasswordHash, FullName, Role, Active)
VALUES ('admin', 'CHANGE_THIS_HASH', 'System Admin', 'Admin', 1),
       ('operator', 'CHANGE_THIS_HASH', 'Gate Operator', 'Operator', 1);
GO

INSERT INTO dbo.SystemSettings (SettingKey, SettingValue, Remarks)
VALUES
('TotalParkingCapacity', '500', 'Total physical parking capacity'),
('DailyRatePerSlot', '50', 'Default daily rate per slot'),
('WeeklyRatePerSlot', '150', 'Default weekly rate per slot'),
('MonthlyRatePerSlot', '500', 'Default monthly rate per slot'),
('OverstayDailyCharge', '50', 'Charge per overstay day'),
('BlockEntryIfPaymentDue', 'false', 'If true, payment due blocks barcode generation'),
('BarcodePrefix', 'KP', 'Barcode prefix'),
('InvoicePrefix', 'INV', 'Invoice prefix'),
('ReceiptPrefix', 'RCT', 'Receipt prefix');
GO

INSERT INTO dbo.ParkingCompanies (CompanyCode, CompanyName, ContactPerson, Mobile, Email, Status, CreatedBy)
VALUES
('COMP-001', 'Company A Transport LLC', 'Mr. Ahmed', '+971 50 123 4567', 'accounts@companya.ae', 'Active', 1),
('COMP-002', 'Gulf Contracting Co.', 'Mr. Faisal', '+971 55 234 5678', 'accounts@gulfcontracting.ae', 'Active', 1),
('COMP-003', 'Prime Logistics UAE', 'Mr. Kareem', '+971 56 345 6789', 'billing@primelogistics.ae', 'Active', 1),
('COMP-004', 'Emirates Facility Services', 'Ms. Sara', '+971 52 456 7890', 'finance@emiratesfacility.ae', 'Active', 1);
GO

DECLARE @Today DATE = CAST(GETDATE() AS DATE);
DECLARE @EndDate DATE = DATEADD(DAY, 30, @Today);

INSERT INTO dbo.ParkingSubscriptions
(CompanyId, PlanType, SlotsPurchased, RatePerSlot, StartDate, EndDate, TotalAmount, PaidAmount, BalanceAmount, Status, CreatedBy)
SELECT CompanyId, 'Monthly', 20, 500, @Today, @EndDate, 10000, 10000, 0, 'Active', 1
FROM dbo.ParkingCompanies WHERE CompanyCode = 'COMP-001';

INSERT INTO dbo.ParkingSubscriptions
(CompanyId, PlanType, SlotsPurchased, RatePerSlot, StartDate, EndDate, TotalAmount, PaidAmount, BalanceAmount, Status, CreatedBy)
SELECT CompanyId, 'Monthly', 30, 500, @Today, @EndDate, 15000, 10000, 5000, 'Active', 1
FROM dbo.ParkingCompanies WHERE CompanyCode = 'COMP-002';
GO

INSERT INTO dbo.ParkingInvoices
(InvoiceNo, CompanyId, SubscriptionId, InvoiceType, InvoiceDate, DueDate, PlanType, Slots, SubTotal, TotalAmount, PaidAmount, BalanceAmount, Status, CreatedBy)
SELECT 'INV-SEED-001', CompanyId, SubscriptionId, 'Subscription', GETDATE(), EndDate, PlanType, SlotsPurchased, TotalAmount, TotalAmount, PaidAmount, BalanceAmount,
       CASE WHEN BalanceAmount = 0 THEN 'Paid' WHEN PaidAmount > 0 THEN 'Partial' ELSE 'Unpaid' END, 1
FROM dbo.ParkingSubscriptions;
GO

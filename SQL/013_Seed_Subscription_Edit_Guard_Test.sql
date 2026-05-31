/*
Networld Parking Lot - Subscription Edit Guard Test Seed

Purpose:
- Tests the case where a future subscription was temporarily changed to today's date,
  one extra car was allowed inside, and then the user tries to move the subscription
  back to next month.
- After the backend patch, that edit must be blocked when the inside vehicle cannot
  fit into other currently-active subscriptions.

How to test:
1) Run this seed in the parking database.
2) Open Subscriptions and find company code EDIT-GUARD-01.
3) Edit the subscription whose remarks contain: "TEMPORARILY ACTIVE NEXT MONTH SUB".
4) Change Start Date to @NextMonthStart / next month and save.
5) Expected API/UI result: save is rejected with a message that inside vehicle(s)
   would lose active slot coverage.
6) Optional positive test: increase the MAIN CURRENT SUB slots from 20 to 21 first,
   then retry moving the TEMP subscription back to next month. It should save because
   the 21 inside vehicles can fit into the other active subscription.
*/

USE NetworldParkingLot;
GO

DECLARE @Today DATE = CAST(GETDATE() AS DATE);
DECLARE @CurrentEnd DATE = EOMONTH(@Today);
DECLARE @NextMonthStart DATE = DATEADD(DAY, 1, @CurrentEnd);
DECLARE @NextMonthEnd DATE = EOMONTH(@NextMonthStart);
DECLARE @Now DATETIME2 = SYSDATETIME();
DECLARE @SeedCompanyCode NVARCHAR(50) = 'EDIT-GUARD-01';

/* Clean previous seed safely */
DELETE p
FROM dbo.ParkingPayments p
INNER JOIN dbo.ParkingCompanies c ON c.CompanyId = p.CompanyId
WHERE c.CompanyCode = @SeedCompanyCode;

DELETE i
FROM dbo.ParkingInvoices i
INNER JOIN dbo.ParkingCompanies c ON c.CompanyId = i.CompanyId
WHERE c.CompanyCode = @SeedCompanyCode;

DELETE s
FROM dbo.ParkingSessions s
INNER JOIN dbo.ParkingCompanies c ON c.CompanyId = s.CompanyId
WHERE c.CompanyCode = @SeedCompanyCode;

DELETE ps
FROM dbo.ParkingSubscriptions ps
INNER JOIN dbo.ParkingCompanies c ON c.CompanyId = ps.CompanyId
WHERE c.CompanyCode = @SeedCompanyCode;

DELETE FROM dbo.ParkingCompanies WHERE CompanyCode = @SeedCompanyCode;
GO

DECLARE @Today DATE = CAST(GETDATE() AS DATE);
DECLARE @CurrentEnd DATE = EOMONTH(@Today);
DECLARE @NextMonthStart DATE = DATEADD(DAY, 1, @CurrentEnd);
DECLARE @NextMonthEnd DATE = EOMONTH(@NextMonthStart);
DECLARE @Now DATETIME2 = SYSDATETIME();
DECLARE @SeedCompanyCode NVARCHAR(50) = 'EDIT-GUARD-01';
DECLARE @InvoiceStamp NVARCHAR(30) = REPLACE(REPLACE(REPLACE(CONVERT(NVARCHAR(19), @Now, 120), '-', ''), ':', ''), ' ', '');

INSERT INTO dbo.ParkingCompanies
(CompanyCode, CompanyName, ContactPerson, Mobile, Email, Status, CreatedBy, Remarks)
VALUES
(@SeedCompanyCode, 'Subscription Edit Guard Test LLC', 'Test User', '+971503333333', 'edit.guard@test.local', 'Active', 1,
 'Seed company to test blocking subscription date edits when inside cars use the edited subscription slots.');

DECLARE @CompanyId INT = (SELECT CompanyId FROM dbo.ParkingCompanies WHERE CompanyCode = @SeedCompanyCode);

/* Main current monthly subscription: 20 slots, all already used. */
INSERT INTO dbo.ParkingSubscriptions
(CompanyId, PlanType, SlotsPurchased, RatePerSlot, StartDate, EndDate, TotalAmount, PaidAmount, BalanceAmount, Status, IsExtraSlot, Remarks, CreatedBy)
VALUES
(@CompanyId, 'Monthly', 20, 300, @Today, @CurrentEnd, 6000, 6000, 0, 'Active', 0,
 'MAIN CURRENT SUB - 20 slots already filled by seed vehicles', 1);

DECLARE @MainCurrentSubId INT = SCOPE_IDENTITY();

/* This represents the next-month subscription that the user temporarily moved to today.
   One extra vehicle is inside against this subscription. Moving this back to next month
   must be blocked unless other active subscriptions can cover that vehicle. */
INSERT INTO dbo.ParkingSubscriptions
(CompanyId, PlanType, SlotsPurchased, RatePerSlot, StartDate, EndDate, TotalAmount, PaidAmount, BalanceAmount, Status, IsExtraSlot, Remarks, CreatedBy)
VALUES
(@CompanyId, 'Monthly', 1, 300, @Today, @NextMonthEnd, 300, 300, 0, 'Active', 0,
 'TEMPORARILY ACTIVE NEXT MONTH SUB - try changing StartDate back to next month', 1);

DECLARE @TempNextMonthSubId INT = SCOPE_IDENTITY();

INSERT INTO dbo.ParkingInvoices
(InvoiceNo, CompanyId, SubscriptionId, InvoiceType, InvoiceDate, DueDate, PlanType, Slots, SubTotal, TotalAmount, PaidAmount, BalanceAmount, Status, Remarks, CreatedBy)
VALUES
(CONCAT('INV-EDIT-GUARD-MAIN-', @InvoiceStamp), @CompanyId, @MainCurrentSubId, 'Subscription', @Now, @CurrentEnd, 'Monthly', 20, 6000, 6000, 6000, 0, 'Paid', 'Seed main current monthly invoice', 1),
(CONCAT('INV-EDIT-GUARD-TEMP-', @InvoiceStamp), @CompanyId, @TempNextMonthSubId, 'Subscription', @Now, @NextMonthEnd, 'Monthly', 1, 300, 300, 300, 0, 'Paid', 'Seed temporary active next-month invoice', 1);

DECLARE @N INT = 1;
WHILE @N <= 20
BEGIN
    INSERT INTO dbo.ParkingSessions
    (BarcodeNo, CompanyId, SubscriptionId, PlateNo, VehicleType, EntryTime, Status, BarcodeStatus, EntryOperatorId, CreatedDate, CreatedBy, Remarks)
    VALUES
    (CONCAT('NW-EDIT-GUARD-', RIGHT(CONCAT('000', @N), 3)),
     @CompanyId,
     @MainCurrentSubId,
     CONCAT('EG-', RIGHT(CONCAT('000', @N), 3)),
     'Car',
     DATEADD(MINUTE, -600 + @N, @Now),
     'Inside',
     'Active',
     1,
     DATEADD(MINUTE, -600 + @N, @Now),
     1,
     'Seed inside vehicle using the 20-slot main current subscription');

    SET @N += 1;
END;

/* 21st car: this is the car that should prevent moving the temp subscription back to next month. */
INSERT INTO dbo.ParkingSessions
(BarcodeNo, CompanyId, SubscriptionId, PlateNo, VehicleType, EntryTime, Status, BarcodeStatus, EntryOperatorId, CreatedDate, CreatedBy, Remarks)
VALUES
('NW-EDIT-GUARD-021', @CompanyId, @TempNextMonthSubId, 'EG-021', 'Car', DATEADD(MINUTE, -100, @Now), 'Inside', 'Active', 1, DATEADD(MINUTE, -100, @Now), 1,
 'This car uses the temporarily-active future subscription slot. Editing that subscription back to next month should be blocked.');
GO

/* Verification: identify the subscription to edit and confirm 21 vehicles are inside. */
SELECT
    c.CompanyCode,
    c.CompanyName,
    ps.SubscriptionId,
    ps.PlanType,
    ps.SlotsPurchased,
    ps.StartDate,
    ps.EndDate,
    ps.Status,
    ps.Remarks,
    InsideVehicles = COUNT(s.SessionId)
FROM dbo.ParkingSubscriptions ps
INNER JOIN dbo.ParkingCompanies c ON c.CompanyId = ps.CompanyId
LEFT JOIN dbo.ParkingSessions s ON s.SubscriptionId = ps.SubscriptionId AND s.Status = 'Inside'
WHERE c.CompanyCode = 'EDIT-GUARD-01'
GROUP BY c.CompanyCode, c.CompanyName, ps.SubscriptionId, ps.PlanType, ps.SlotsPurchased, ps.StartDate, ps.EndDate, ps.Status, ps.Remarks
ORDER BY ps.SubscriptionId;
GO

SELECT
    c.CompanyCode,
    s.BarcodeNo,
    s.PlateNo,
    s.SubscriptionId,
    ps.SlotsPurchased,
    ps.StartDate,
    ps.EndDate,
    ps.Remarks AS SubscriptionRemarks,
    s.Status,
    s.EntryTime
FROM dbo.ParkingSessions s
INNER JOIN dbo.ParkingCompanies c ON c.CompanyId = s.CompanyId
LEFT JOIN dbo.ParkingSubscriptions ps ON ps.SubscriptionId = s.SubscriptionId
WHERE c.CompanyCode = 'EDIT-GUARD-01'
ORDER BY s.EntryTime, s.SessionId;
GO

/* Optional positive-test SQL after confirming the block:
   Uncomment this, run it, then retry moving the TEMP subscription to next month.
   It should now pass because the main current subscription can cover all 21 inside cars.

UPDATE ps
SET SlotsPurchased = 21,
    TotalAmount = 6300,
    PaidAmount = CASE WHEN PaidAmount = 6000 THEN 6300 ELSE PaidAmount END,
    BalanceAmount = CASE WHEN BalanceAmount = 0 THEN 0 ELSE BalanceAmount END,
    ModifiedDate = SYSDATETIME(),
    Remarks = CONCAT(ISNULL(Remarks, ''), ' | Positive test: slots increased to 21')
FROM dbo.ParkingSubscriptions ps
INNER JOIN dbo.ParkingCompanies c ON c.CompanyId = ps.CompanyId
WHERE c.CompanyCode = 'EDIT-GUARD-01'
  AND ps.Remarks LIKE 'MAIN CURRENT SUB%';
*/

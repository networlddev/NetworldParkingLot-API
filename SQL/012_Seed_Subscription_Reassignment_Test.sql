/*
Networld Parking Lot - Subscription Reassignment Test Seed

Purpose:
- Tests automatic movement of inside vehicles from an expired subscription to another active subscription of the same company.
- Tests the overflow case where only available active slots are covered and remaining cars still show as overstay.

How to test after running this seed:
1) Open Live Parking or Vehicles/Barcodes, or scan the test barcode at exit.
2) The API will automatically reassign covered inside vehicles to the active subscription.
3) Run the verification SELECT at the bottom of this script.

Expected result after the API endpoint is called:
- NW-AUTO-REALLOC-001 should move to the active weekly subscription and should not show overstay.
- NW-AUTO-REALLOC-002 should remain on the expired daily subscription and should show overstay because only 1 active slot is available.
- NW-AUTO-NOACTIVE-001 should remain overstay because the company has no active subscription.
*/

USE NetworldParkingLot;
GO

DECLARE @Today DATE = CAST(GETDATE() AS DATE);
DECLARE @Yesterday DATE = DATEADD(DAY, -1, @Today);
DECLARE @WeekEnd DATE = DATEADD(DAY, 7, @Today);

DECLARE @SeedCodes TABLE (CompanyCode NVARCHAR(50) NOT NULL PRIMARY KEY);
INSERT INTO @SeedCodes (CompanyCode)
VALUES ('AUTO-REALLOC-01'), ('AUTO-NOACTIVE-01');

/* Clean previous seed safely */
DELETE p
FROM dbo.ParkingPayments p
INNER JOIN dbo.ParkingCompanies c ON c.CompanyId = p.CompanyId
INNER JOIN @SeedCodes sc ON sc.CompanyCode = c.CompanyCode;

DELETE i
FROM dbo.ParkingInvoices i
INNER JOIN dbo.ParkingCompanies c ON c.CompanyId = i.CompanyId
INNER JOIN @SeedCodes sc ON sc.CompanyCode = c.CompanyCode;

DELETE s
FROM dbo.ParkingSessions s
INNER JOIN dbo.ParkingCompanies c ON c.CompanyId = s.CompanyId
INNER JOIN @SeedCodes sc ON sc.CompanyCode = c.CompanyCode;

DELETE ps
FROM dbo.ParkingSubscriptions ps
INNER JOIN dbo.ParkingCompanies c ON c.CompanyId = ps.CompanyId
INNER JOIN @SeedCodes sc ON sc.CompanyCode = c.CompanyCode;

DELETE c
FROM dbo.ParkingCompanies c
INNER JOIN @SeedCodes sc ON sc.CompanyCode = c.CompanyCode;
GO

DECLARE @Today DATE = CAST(GETDATE() AS DATE);
DECLARE @Yesterday DATE = DATEADD(DAY, -1, @Today);
DECLARE @WeekEnd DATE = DATEADD(DAY, 7, @Today);
DECLARE @Now DATETIME2 = SYSDATETIME();

INSERT INTO dbo.ParkingCompanies
(CompanyCode, CompanyName, ContactPerson, Mobile, Email, Status, CreatedBy)
VALUES
('AUTO-REALLOC-01', 'Auto Reallocation Test LLC', 'Test User', '+971501111111', 'auto.reallocation@test.local', 'Active', 1),
('AUTO-NOACTIVE-01', 'Auto No Active Subscription Test LLC', 'Test User', '+971502222222', 'auto.noactive@test.local', 'Active', 1);

DECLARE @ReallocCompanyId INT = (SELECT CompanyId FROM dbo.ParkingCompanies WHERE CompanyCode = 'AUTO-REALLOC-01');
DECLARE @NoActiveCompanyId INT = (SELECT CompanyId FROM dbo.ParkingCompanies WHERE CompanyCode = 'AUTO-NOACTIVE-01');

/* Company 1: expired 2-slot subscription + active 1-slot subscription.
   After API reconciliation, only one of the two inside vehicles should be covered. */
INSERT INTO dbo.ParkingSubscriptions
(CompanyId, PlanType, SlotsPurchased, RatePerSlot, StartDate, EndDate, TotalAmount, PaidAmount, BalanceAmount, Status, IsExtraSlot, Remarks, CreatedBy)
VALUES
(@ReallocCompanyId, 'Daily', 2, 50, DATEADD(DAY, -2, @Today), @Yesterday, 100, 100, 0, 'Active', 0, 'Seed expired daily subscription with 2 slots', 1),
(@ReallocCompanyId, 'Weekly', 1, 150, @Today, @WeekEnd, 150, 150, 0, 'Active', 0, 'Seed active weekly subscription with 1 slot', 1);

DECLARE @ExpiredReallocSubId INT =
(
    SELECT TOP 1 SubscriptionId
    FROM dbo.ParkingSubscriptions
    WHERE CompanyId = @ReallocCompanyId AND PlanType = 'Daily'
    ORDER BY SubscriptionId DESC
);
DECLARE @ActiveReallocSubId INT =
(
    SELECT TOP 1 SubscriptionId
    FROM dbo.ParkingSubscriptions
    WHERE CompanyId = @ReallocCompanyId AND PlanType = 'Weekly'
    ORDER BY SubscriptionId DESC
);

INSERT INTO dbo.ParkingInvoices
(InvoiceNo, CompanyId, SubscriptionId, InvoiceType, InvoiceDate, DueDate, PlanType, Slots, SubTotal, TotalAmount, PaidAmount, BalanceAmount, Status, Remarks, CreatedBy)
VALUES
(CONCAT('INV-AUTO-REALLOC-EXP-', FORMAT(@Now, 'yyyyMMddHHmmss')), @ReallocCompanyId, @ExpiredReallocSubId, 'Subscription', DATEADD(DAY, -2, @Now), @Yesterday, 'Daily', 2, 100, 100, 100, 0, 'Paid', 'Seed expired subscription invoice', 1),
(CONCAT('INV-AUTO-REALLOC-ACT-', FORMAT(@Now, 'yyyyMMddHHmmss')), @ReallocCompanyId, @ActiveReallocSubId, 'Subscription', @Now, @WeekEnd, 'Weekly', 1, 150, 150, 150, 0, 'Paid', 'Seed active subscription invoice', 1);

INSERT INTO dbo.ParkingSessions
(BarcodeNo, CompanyId, SubscriptionId, PlateNo, VehicleType, EntryTime, Status, BarcodeStatus, EntryOperatorId, CreatedDate, CreatedBy, Remarks)
VALUES
('NW-AUTO-REALLOC-001', @ReallocCompanyId, @ExpiredReallocSubId, 'AUTO-R-001', 'Car', DATEADD(HOUR, -10, @Now), 'Inside', 'Active', 1, DATEADD(HOUR, -10, @Now), 1, 'Expected to move to active weekly subscription'),
('NW-AUTO-REALLOC-002', @ReallocCompanyId, @ExpiredReallocSubId, 'AUTO-R-002', 'Car', DATEADD(HOUR, -9, @Now), 'Inside', 'Active', 1, DATEADD(HOUR, -9, @Now), 1, 'Expected to remain overstay because only one active slot is available');

/* Company 2: expired subscription only. Vehicle should remain overstay. */
INSERT INTO dbo.ParkingSubscriptions
(CompanyId, PlanType, SlotsPurchased, RatePerSlot, StartDate, EndDate, TotalAmount, PaidAmount, BalanceAmount, Status, IsExtraSlot, Remarks, CreatedBy)
VALUES
(@NoActiveCompanyId, 'Daily', 1, 50, DATEADD(DAY, -2, @Today), @Yesterday, 50, 50, 0, 'Active', 0, 'Seed expired subscription only', 1);

DECLARE @NoActiveExpiredSubId INT =
(
    SELECT TOP 1 SubscriptionId
    FROM dbo.ParkingSubscriptions
    WHERE CompanyId = @NoActiveCompanyId
    ORDER BY SubscriptionId DESC
);

INSERT INTO dbo.ParkingInvoices
(InvoiceNo, CompanyId, SubscriptionId, InvoiceType, InvoiceDate, DueDate, PlanType, Slots, SubTotal, TotalAmount, PaidAmount, BalanceAmount, Status, Remarks, CreatedBy)
VALUES
(CONCAT('INV-AUTO-NOACTIVE-', FORMAT(@Now, 'yyyyMMddHHmmss')), @NoActiveCompanyId, @NoActiveExpiredSubId, 'Subscription', DATEADD(DAY, -2, @Now), @Yesterday, 'Daily', 1, 50, 50, 50, 0, 'Paid', 'Seed no active subscription invoice', 1);

INSERT INTO dbo.ParkingSessions
(BarcodeNo, CompanyId, SubscriptionId, PlateNo, VehicleType, EntryTime, Status, BarcodeStatus, EntryOperatorId, CreatedDate, CreatedBy, Remarks)
VALUES
('NW-AUTO-NOACTIVE-001', @NoActiveCompanyId, @NoActiveExpiredSubId, 'AUTO-N-001', 'Car', DATEADD(HOUR, -8, @Now), 'Inside', 'Active', 1, DATEADD(HOUR, -8, @Now), 1, 'Expected to remain overstay because there is no active subscription');
GO

/* Verification query: run once before API call and once after opening Live Parking/Vehicles or scanning a barcode. */
SELECT
    c.CompanyCode,
    c.CompanyName,
    s.BarcodeNo,
    s.PlateNo,
    s.Status,
    s.BarcodeStatus,
    s.SubscriptionId,
    ps.PlanType,
    ps.SlotsPurchased,
    ps.StartDate,
    ps.EndDate,
    CASE
        WHEN s.Status = 'Inside' AND ps.EndDate < CAST(GETDATE() AS DATE) THEN 'OVERSTAY'
        WHEN s.Status = 'Inside' THEN 'COVERED'
        ELSE s.Status
    END AS ExpectedRuntimeStatus,
    s.OverstayDays,
    s.OverstayAmount
FROM dbo.ParkingSessions s
INNER JOIN dbo.ParkingCompanies c ON c.CompanyId = s.CompanyId
LEFT JOIN dbo.ParkingSubscriptions ps ON ps.SubscriptionId = s.SubscriptionId
WHERE c.CompanyCode IN ('AUTO-REALLOC-01', 'AUTO-NOACTIVE-01')
ORDER BY c.CompanyCode, s.EntryTime, s.SessionId;
GO

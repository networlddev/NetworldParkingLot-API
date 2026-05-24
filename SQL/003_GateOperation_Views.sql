USE NetworldParkingLot;
GO

CREATE OR ALTER VIEW dbo.Vw_GateCompanyStatus
AS
SELECT
    c.CompanyId,
    c.CompanyCode,
    c.CompanyName,
    c.ContactPerson,
    c.Mobile,
    c.Status AS CompanyStatus,
    ISNULL(s.PurchasedSlots, 0) AS PurchasedSlots,
    ISNULL(ps.VehiclesInside, 0) AS VehiclesInside,
    CASE WHEN ISNULL(s.PurchasedSlots, 0) - ISNULL(ps.VehiclesInside, 0) < 0 THEN 0 ELSE ISNULL(s.PurchasedSlots, 0) - ISNULL(ps.VehiclesInside, 0) END AS AvailableSlots,
    ISNULL(inv.PendingAmount, 0) AS PendingAmount,
    s.SubscriptionStartDate,
    s.SubscriptionEndDate
FROM dbo.ParkingCompanies c
OUTER APPLY
(
    SELECT SUM(SlotsPurchased) AS PurchasedSlots, MIN(StartDate) AS SubscriptionStartDate, MAX(EndDate) AS SubscriptionEndDate
    FROM dbo.ParkingSubscriptions
    WHERE CompanyId = c.CompanyId
      AND Status = 'Active'
      AND CAST(GETDATE() AS DATE) BETWEEN StartDate AND EndDate
) s
OUTER APPLY
(
    SELECT COUNT(1) AS VehiclesInside
    FROM dbo.ParkingSessions
    WHERE CompanyId = c.CompanyId
      AND Status = 'Inside'
) ps
OUTER APPLY
(
    SELECT SUM(BalanceAmount) AS PendingAmount
    FROM dbo.ParkingInvoices
    WHERE CompanyId = c.CompanyId
      AND BalanceAmount > 0
) inv;
GO

CREATE OR ALTER VIEW dbo.Vw_LiveParking
AS
SELECT
    ps.SessionId,
    ps.BarcodeNo,
    ps.PlateNo,
    c.CompanyName,
    ps.EntryTime,
    sub.EndDate AS ValidUntil,
    ps.Status,
    ps.BarcodeStatus,
    ISNULL(inv.PendingAmount, 0) AS PendingAmount
FROM dbo.ParkingSessions ps
INNER JOIN dbo.ParkingCompanies c ON c.CompanyId = ps.CompanyId
LEFT JOIN dbo.ParkingSubscriptions sub ON sub.SubscriptionId = ps.SubscriptionId
OUTER APPLY
(
    SELECT SUM(BalanceAmount) AS PendingAmount
    FROM dbo.ParkingInvoices
    WHERE CompanyId = ps.CompanyId
      AND BalanceAmount > 0
) inv
WHERE ps.Status = 'Inside';
GO

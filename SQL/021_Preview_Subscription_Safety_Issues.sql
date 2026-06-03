/*
Preview-only subscription safety checks.

Purpose:
- Find stale subscriptions still marked Active after their EndDate.
- Find overlapping active regular subscriptions for the same company.
- Find inside vehicles currently linked to stale/non-active subscription coverage.

Rules:
- SELECT only.
- Do not update live data automatically.
- Review these result sets before deciding any manual cleanup script.
*/

SET NOCOUNT ON;

DECLARE @Today DATE = CONVERT(DATE, GETDATE());

PRINT '1) Stale active subscriptions: Status = Active but EndDate is before today.';

SELECT
    c.CompanyId,
    c.CompanyCode,
    c.CompanyName,
    s.SubscriptionId,
    s.PlanType,
    s.IsExtraSlot,
    s.SlotsPurchased,
    CONVERT(DATE, s.StartDate) AS StartDate,
    CONVERT(DATE, s.EndDate) AS EndDate,
    s.Status,
    s.BalanceAmount,
    COUNT(ps.SessionId) AS InsideVehiclesLinkedToSubscription
FROM dbo.ParkingSubscriptions s
INNER JOIN dbo.ParkingCompanies c ON c.CompanyId = s.CompanyId
LEFT JOIN dbo.ParkingSessions ps
    ON ps.SubscriptionId = s.SubscriptionId
   AND ps.Status = 'Inside'
WHERE s.Status = 'Active'
  AND CONVERT(DATE, s.EndDate) < @Today
GROUP BY
    c.CompanyId,
    c.CompanyCode,
    c.CompanyName,
    s.SubscriptionId,
    s.PlanType,
    s.IsExtraSlot,
    s.SlotsPurchased,
    s.StartDate,
    s.EndDate,
    s.Status,
    s.BalanceAmount
ORDER BY c.CompanyName, s.EndDate, s.SubscriptionId;

PRINT '2) Overlapping active regular subscriptions. Extra-slot subscriptions are excluded by business rule.';

SELECT
    c.CompanyId,
    c.CompanyCode,
    c.CompanyName,
    a.SubscriptionId AS SubscriptionAId,
    CONVERT(DATE, a.StartDate) AS SubscriptionAStartDate,
    CONVERT(DATE, a.EndDate) AS SubscriptionAEndDate,
    a.SlotsPurchased AS SubscriptionASlots,
    b.SubscriptionId AS SubscriptionBId,
    CONVERT(DATE, b.StartDate) AS SubscriptionBStartDate,
    CONVERT(DATE, b.EndDate) AS SubscriptionBEndDate,
    b.SlotsPurchased AS SubscriptionBSlots
FROM dbo.ParkingSubscriptions a
INNER JOIN dbo.ParkingSubscriptions b
    ON b.CompanyId = a.CompanyId
   AND b.SubscriptionId > a.SubscriptionId
   AND b.IsExtraSlot = 0
   AND b.Status = 'Active'
   AND CONVERT(DATE, b.EndDate) >= @Today
   AND CONVERT(DATE, a.StartDate) <= CONVERT(DATE, b.EndDate)
   AND CONVERT(DATE, a.EndDate) >= CONVERT(DATE, b.StartDate)
INNER JOIN dbo.ParkingCompanies c ON c.CompanyId = a.CompanyId
WHERE a.IsExtraSlot = 0
  AND a.Status = 'Active'
  AND CONVERT(DATE, a.EndDate) >= @Today
ORDER BY c.CompanyName, a.StartDate, a.SubscriptionId, b.SubscriptionId;

PRINT '3) Inside vehicles linked to stale or non-active subscriptions.';

SELECT
    c.CompanyId,
    c.CompanyCode,
    c.CompanyName,
    ps.SessionId,
    ps.BarcodeNo,
    ps.PlateNo,
    ps.VehicleType,
    ps.Status AS SessionStatus,
    ps.BarcodeStatus,
    ps.EntryTime,
    s.SubscriptionId,
    s.PlanType,
    s.IsExtraSlot,
    CONVERT(DATE, s.StartDate) AS SubscriptionStartDate,
    CONVERT(DATE, s.EndDate) AS SubscriptionEndDate,
    s.Status AS SubscriptionStatus
FROM dbo.ParkingSessions ps
INNER JOIN dbo.ParkingCompanies c ON c.CompanyId = ps.CompanyId
LEFT JOIN dbo.ParkingSubscriptions s ON s.SubscriptionId = ps.SubscriptionId
WHERE ps.Status = 'Inside'
  AND
  (
      s.SubscriptionId IS NULL
      OR s.Status <> 'Active'
      OR CONVERT(DATE, s.StartDate) > @Today
      OR CONVERT(DATE, s.EndDate) < @Today
  )
ORDER BY c.CompanyName, ps.EntryTime, ps.SessionId;

PRINT '4) Active slot capacity versus inside vehicles by company.';

WITH ActiveSlots AS
(
    SELECT
        CompanyId,
        SUM(CASE
            WHEN Status = 'Active'
             AND CONVERT(DATE, StartDate) <= @Today
             AND CONVERT(DATE, EndDate) >= @Today
             AND SlotsPurchased > 0
            THEN SlotsPurchased
            ELSE 0
        END) AS ActiveSlots
    FROM dbo.ParkingSubscriptions
    GROUP BY CompanyId
),
InsideCounts AS
(
    SELECT CompanyId, COUNT(*) AS InsideVehicles
    FROM dbo.ParkingSessions
    WHERE Status = 'Inside'
    GROUP BY CompanyId
)
SELECT
    c.CompanyId,
    c.CompanyCode,
    c.CompanyName,
    ISNULL(a.ActiveSlots, 0) AS ActiveSlots,
    ISNULL(i.InsideVehicles, 0) AS InsideVehicles,
    ISNULL(a.ActiveSlots, 0) - ISNULL(i.InsideVehicles, 0) AS RemainingSlots,
    CASE
        WHEN ISNULL(i.InsideVehicles, 0) > ISNULL(a.ActiveSlots, 0) THEN 'Inside vehicles exceed active slots'
        ELSE 'OK'
    END AS CapacityStatus
FROM dbo.ParkingCompanies c
LEFT JOIN ActiveSlots a ON a.CompanyId = c.CompanyId
LEFT JOIN InsideCounts i ON i.CompanyId = c.CompanyId
WHERE ISNULL(i.InsideVehicles, 0) > 0
   OR ISNULL(a.ActiveSlots, 0) > 0
ORDER BY CapacityStatus DESC, c.CompanyName;

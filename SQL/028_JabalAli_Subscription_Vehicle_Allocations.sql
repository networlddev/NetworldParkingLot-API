/*
    Jabal Ali subscription vehicle-type allocations.

    Adds optional line-level slot/rate rows for subscriptions, so one company
    subscription can include mixed vehicle types such as cars, trucks and buses.

    Safe to run multiple times. Do not run directly from Codex; apply manually
    during database deployment after backup/review.
*/

SET NOCOUNT ON;
SET XACT_ABORT ON;

IF OBJECT_ID('dbo.ParkingSubscriptionVehicleAllocations', 'U') IS NULL
BEGIN
    CREATE TABLE dbo.ParkingSubscriptionVehicleAllocations
    (
        SubscriptionVehicleAllocationId int IDENTITY(1,1) NOT NULL CONSTRAINT PK_ParkingSubscriptionVehicleAllocations PRIMARY KEY,
        SubscriptionId int NOT NULL,
        VehicleTypeId int NOT NULL,
        SlotsPurchased int NOT NULL,
        RatePerSlot decimal(18,2) NOT NULL,
        LineTotal decimal(18,2) NOT NULL,
        CreatedDate datetime2 NOT NULL CONSTRAINT DF_ParkingSubscriptionVehicleAllocations_CreatedDate DEFAULT (sysdatetime()),
        CreatedBy int NULL
    );

    ALTER TABLE dbo.ParkingSubscriptionVehicleAllocations WITH CHECK
        ADD CONSTRAINT FK_ParkingSubscriptionVehicleAllocations_Subscription
        FOREIGN KEY (SubscriptionId) REFERENCES dbo.ParkingSubscriptions(SubscriptionId) ON DELETE CASCADE;

    ALTER TABLE dbo.ParkingSubscriptionVehicleAllocations WITH CHECK
        ADD CONSTRAINT FK_ParkingSubscriptionVehicleAllocations_VehicleType
        FOREIGN KEY (VehicleTypeId) REFERENCES dbo.ParkingVehicleTypes(VehicleTypeId);

    CREATE INDEX IX_ParkingSubscriptionVehicleAllocations_SubscriptionVehicleType
        ON dbo.ParkingSubscriptionVehicleAllocations (SubscriptionId, VehicleTypeId);
END;

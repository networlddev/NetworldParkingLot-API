namespace NetworldParkingLot.Api.Domain.Constants;

public static class ParkingConstants
{
    public static class CompanyStatus
    {
        public const string Active = "Active";
        public const string Inactive = "Inactive";
        public const string Blocked = "Blocked";
    }

    public static class SubscriptionStatus
    {
        public const string Active = "Active";
        public const string Expired = "Expired";
        public const string Inactive = "Inactive";
        public const string Cancelled = "Cancelled";
    }

    public static class PaymentStatus
    {
        public const string Paid = "Paid";
        public const string Partial = "Partial";
        public const string Unpaid = "Unpaid";
        public const string Overdue = "Overdue";
    }

    public static class SessionStatus
    {
        public const string BarcodeGenerated = "BarcodeGenerated";
        public const string Inside = "Inside";
        public const string Exited = "Exited";
        public const string Rejected = "Rejected";
        public const string Cancelled = "Cancelled";
    }

    public static class BarcodeStatus
    {
        public const string Generated = "Generated";
        public const string Active = "Active";
        public const string Used = "Used";
        public const string Invalid = "Invalid";
    }

    public static class GateActionType
    {
        public const string EntryCheck = "EntryCheck";
        public const string BarcodeGenerated = "BarcodeGenerated";
        public const string EntryAllowed = "EntryAllowed";
        public const string EntryRejected = "EntryRejected";
        public const string ExitScanned = "ExitScanned";
        public const string ExitAllowed = "ExitAllowed";
        public const string PaymentCollected = "PaymentCollected";
        public const string ExtraSlotInvoice = "ExtraSlotInvoice";
    }

    public static class ExitStatus
    {
        public const string ClearToExit = "ClearToExit";
        public const string PaymentRequired = "PaymentRequired";
        public const string Warning = "Warning";
        public const string InvalidBarcode = "InvalidBarcode";
        public const string AlreadyExited = "AlreadyExited";
    }

    public static class OutsideDisplayStatus
    {
        public const string ClearToExit = "ClearToExit";
        public const string PaymentRequired = "PaymentRequired";
        public const string OverstayDetected = "OverstayDetected";
        public const string InvalidBarcode = "InvalidBarcode";
        public const string EntryAllowed = "EntryAllowed";
        public const string ForceExit = "ForceExit";
    }
}

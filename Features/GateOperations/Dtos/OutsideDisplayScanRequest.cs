namespace NetworldParkingLot.Api.Features.GateOperations.Dtos
{
    public sealed class OutsideDisplayScanRequest
    {
        public string BarcodeNo { get; set; } = "";
        public int OperatorId { get; set; } = 1;
    }

    // Make sure your OutsideDisplayEvent DTO includes EventId.
    public sealed class OutsideDisplayEventDto
    {
        public long EventId { get; set; }
        public string BarcodeNo { get; set; } = "";
        public string VehicleReference { get; set; } = "";
        public string CompanyName { get; set; } = "";
        public string Status { get; set; } = "";
        public string DisplayStatus { get; set; } = "";
        public string DisplayColor { get; set; } = "";
        public string Message { get; set; } = "";
        public decimal AmountDue { get; set; }
        public int OverstayDays { get; set; }
        public DateTime CreatedDate { get; set; }
    }
}

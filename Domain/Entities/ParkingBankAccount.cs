namespace NetworldParkingLot.Api.Domain.Entities;

public sealed class ParkingBankAccount
{
    public int BankAccountId { get; set; }
    public string BankName { get; set; } = string.Empty;
    public string? AccountName { get; set; }
    public string? AccountNumber { get; set; }
    public string? Iban { get; set; }
    public string? BranchName { get; set; }
    public bool IsActive { get; set; } = true;
    public int SortOrder { get; set; }
    public string? Remarks { get; set; }
    public DateTime CreatedDate { get; set; } = DateTime.Now;
    public int? CreatedBy { get; set; }
    public DateTime? ModifiedDate { get; set; }
    public int? ModifiedBy { get; set; }
}

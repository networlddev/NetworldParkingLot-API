namespace NetworldParkingLot.Api.Domain.Entities;

public sealed class AppUser
{
    public int UserId { get; set; }
    public string Username { get; set; } = string.Empty;
    public string PasswordHash { get; set; } = string.Empty;
    public string FullName { get; set; } = string.Empty;
    public string Role { get; set; } = "Operator";
    public bool Active { get; set; } = true;
    public DateTime CreatedDate { get; set; } = DateTime.Now;
}

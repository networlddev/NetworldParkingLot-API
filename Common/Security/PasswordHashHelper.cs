using System.Security.Cryptography;
using System.Text;

namespace NetworldParkingLot.Api.Common.Security;

public static class PasswordHashHelper
{
    public static string HashPassword(string password)
    {
        var bytes = SHA256.HashData(Encoding.UTF8.GetBytes(password ?? string.Empty));
        return "SHA256$" + Convert.ToHexString(bytes);
    }

    public static bool VerifyPassword(string password, string storedHash)
    {
        if (string.IsNullOrWhiteSpace(storedHash)) return false;

        if (storedHash.StartsWith("SHA256$", StringComparison.OrdinalIgnoreCase))
        {
            return string.Equals(HashPassword(password), storedHash, StringComparison.OrdinalIgnoreCase);
        }

        // Backward compatibility for old seed/demo values until SQL patch is applied.
        return string.Equals(password, storedHash, StringComparison.Ordinal);
    }
}

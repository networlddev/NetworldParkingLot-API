using System.IdentityModel.Tokens.Jwt;
using System.Security.Claims;
using System.Text;
using Microsoft.IdentityModel.Tokens;
using NetworldParkingLot.Api.Features.UserAccess.Dtos;

namespace NetworldParkingLot.Api.Common.Security;

public sealed class JwtTokenService(IConfiguration configuration)
{
    public (string Token, DateTime ExpiryTime) CreateToken(CurrentParkingUserDto user)
    {
        var issuer = configuration["Jwt:Issuer"] ?? "NetworldParkingLot";
        var audience = configuration["Jwt:Audience"] ?? "NetworldParkingLotClient";
        var key = configuration["Jwt:Key"];

        if (string.IsNullOrWhiteSpace(key) || key.Length < 32)
            key = "NetworldParkingLot_Default_Development_Key_Change_This_Immediately";

        var expiry = DateTime.UtcNow.AddHours(GetExpiryHours());
        var claims = new List<Claim>
        {
            new(ClaimTypes.NameIdentifier, user.UserId.ToString()),
            new(ClaimTypes.Name, user.FullName),
            new("username", user.Username),
            new("status", user.Status)
        };

        foreach (var role in user.Roles.Distinct(StringComparer.OrdinalIgnoreCase))
            claims.Add(new Claim(ClaimTypes.Role, role));

        var credentials = new SigningCredentials(
            new SymmetricSecurityKey(Encoding.UTF8.GetBytes(key)),
            SecurityAlgorithms.HmacSha256);

        var token = new JwtSecurityToken(
            issuer: issuer,
            audience: audience,
            claims: claims,
            expires: expiry,
            signingCredentials: credentials);

        return (new JwtSecurityTokenHandler().WriteToken(token), expiry);
    }

    private int GetExpiryHours()
    {
        var configured = configuration["Jwt:ExpiryHours"];
        return int.TryParse(configured, out var hours) && hours > 0 ? hours : 12;
    }
}

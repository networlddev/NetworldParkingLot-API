using System.Security.Claims;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using NetworldParkingLot.Api.Common;
using NetworldParkingLot.Api.Common.Security;
using NetworldParkingLot.Api.Features.SystemActivity.Services;
using NetworldParkingLot.Api.Features.UserAccess.Dtos;
using NetworldParkingLot.Api.Features.UserAccess.Services;

namespace NetworldParkingLot.Api.Features.UserAccess.Controllers;

[ApiController]
[Route("api/auth")]
public sealed class AuthController(IUserAccessService userAccessService, ISystemActivityService activityService) : ControllerBase
{
    [AllowAnonymous]
    [HttpPost("login")]
    public async Task<ActionResult<ApiResponse<LoginResultDto>>> Login([FromBody] LoginRequestDto request, CancellationToken cancellationToken)
    {
        try
        {
            var data = await userAccessService.LoginAsync(request, cancellationToken);
            await activityService.RecordAsync(this.BuildActivity(
                "auth",
                "login",
                "Success",
                "AppUser",
                data.User.UserId.ToString(),
                "Login successful",
                $"User {data.User.Username} logged in.",
                userId: data.User.UserId,
                username: string.IsNullOrWhiteSpace(data.User.FullName) ? data.User.Username : data.User.FullName), cancellationToken);
            return Ok(ApiResponse<LoginResultDto>.Ok(data, "Login successful."));
        }
        catch (UnauthorizedAccessException ex)
        {
            await RecordLoginFailureAsync(request.Username, ex.Message, cancellationToken);
            return Unauthorized(ApiResponse<LoginResultDto>.Fail(ex.Message));
        }
        catch (InvalidOperationException ex)
        {
            await RecordLoginFailureAsync(request.Username, ex.Message, cancellationToken);
            return BadRequest(ApiResponse<LoginResultDto>.Fail(ex.Message));
        }
    }

    [Authorize]
    [HttpGet("me")]
    public async Task<ActionResult<ApiResponse<CurrentParkingUserDto>>> Me(CancellationToken cancellationToken)
    {
        var userIdText = User.FindFirstValue(ClaimTypes.NameIdentifier);
        if (!int.TryParse(userIdText, out var userId))
            return Unauthorized(ApiResponse<CurrentParkingUserDto>.Fail("Unauthorized."));

        var data = await userAccessService.GetCurrentUserAsync(userId, cancellationToken);
        return Ok(ApiResponse<CurrentParkingUserDto>.Ok(data));
    }

    private Task RecordLoginFailureAsync(string? username, string message, CancellationToken cancellationToken)
    {
        var cleanUsername = string.IsNullOrWhiteSpace(username) ? "-" : username.Trim();
        return activityService.RecordAsync(this.BuildActivity(
            "auth",
            "login",
            "Failure",
            "AppUser",
            null,
            "Login failed",
            $"Login failed for username {cleanUsername}: {message}",
            userId: null,
            username: cleanUsername), cancellationToken);
    }
}

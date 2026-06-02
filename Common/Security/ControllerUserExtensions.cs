using System.Security.Claims;
using Microsoft.AspNetCore.Mvc;
using NetworldParkingLot.Api.Features.SystemActivity.Services;

namespace NetworldParkingLot.Api.Common.Security;

public static class ControllerUserExtensions
{
    public static int CurrentUserId(this ControllerBase controller)
    {
        var id = controller.User.FindFirstValue(ClaimTypes.NameIdentifier) ?? controller.User.FindFirstValue("sub");
        return int.TryParse(id, out var parsed) ? parsed : 0;
    }

    public static string CurrentUsername(this ControllerBase controller)
    {
        var fullName = controller.User.FindFirstValue(ClaimTypes.Name);
        if (!string.IsNullOrWhiteSpace(fullName))
            return fullName;

        return controller.User.FindFirstValue("username")
            ?? controller.User.Identity?.Name
            ?? string.Empty;
    }

    public static SystemActivityRequest BuildActivity(
        this ControllerBase controller,
        string moduleKey,
        string actionKey,
        string result,
        string entityType,
        string? entityId,
        string title,
        string? message = null,
        IReadOnlyList<SystemActivityChange>? changes = null,
        int? userId = null,
        string? username = null)
    {
        return new SystemActivityRequest(
            userId ?? controller.CurrentUserId(),
            username ?? controller.CurrentUsername(),
            moduleKey,
            actionKey,
            result,
            entityType,
            entityId,
            title,
            message,
            controller.HttpContext.Connection.RemoteIpAddress?.ToString(),
            controller.Request.Headers.UserAgent.ToString(),
            controller.Request.Path,
            changes);
    }
}

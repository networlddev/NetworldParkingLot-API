using System.Security.Claims;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.Filters;
using NetworldParkingLot.Api.Common;
using NetworldParkingLot.Api.Features.UserAccess.Services;

namespace NetworldParkingLot.Api.Features.UserAccess.Filters;

public sealed class RequireParkingPermissionAttribute : TypeFilterAttribute
{
    public RequireParkingPermissionAttribute(string moduleKey, string actionKey)
        : base(typeof(RequireParkingPermissionFilter))
    {
        Arguments = [moduleKey, actionKey];
    }
}

public sealed class RequireParkingPermissionFilter(
    string moduleKey,
    string actionKey,
    IUserAccessService userAccessService) : IAsyncActionFilter
{
    public async Task OnActionExecutionAsync(ActionExecutingContext context, ActionExecutionDelegate next)
    {
        var userIdText = context.HttpContext.User.FindFirstValue(ClaimTypes.NameIdentifier);
        if (!int.TryParse(userIdText, out var userId))
        {
            context.Result = new UnauthorizedObjectResult(ApiResponse<object>.Fail("Unauthorized."));
            return;
        }

        var allowed = await userAccessService.HasPermissionAsync(userId, moduleKey, actionKey, context.HttpContext.RequestAborted);
        if (!allowed)
        {
            context.Result = new ObjectResult(ApiResponse<object>.Fail("You don't have access to this feature."))
            {
                StatusCode = StatusCodes.Status403Forbidden
            };
            return;
        }

        await next();
    }
}

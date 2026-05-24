using System.Text.Json;
using Microsoft.Data.SqlClient;
using NetworldParkingLot.Api.Common;

namespace NetworldParkingLot.Api.Common;

public sealed class GlobalExceptionMiddleware(RequestDelegate next, ILogger<GlobalExceptionMiddleware> logger)
{
    public async Task InvokeAsync(HttpContext context)
    {
        try
        {
            await next(context);
        }
        catch (Exception ex)
        {
            logger.LogError(ex, "Unhandled API exception. TraceId: {TraceId}", context.TraceIdentifier);

            context.Response.ContentType = "application/json";
            context.Response.StatusCode = StatusCodes.Status500InternalServerError;

            var response = ApiResponse<object>.Fail(
                "System cannot process this request now. Please try again or contact support.",
                context.TraceIdentifier);

            await context.Response.WriteAsync(JsonSerializer.Serialize(response, new JsonSerializerOptions
            {
                PropertyNamingPolicy = JsonNamingPolicy.CamelCase
            }));
        }
    }
}

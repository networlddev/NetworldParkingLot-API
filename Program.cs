using System.Text;
using Microsoft.AspNetCore.Authentication.JwtBearer;
using Microsoft.EntityFrameworkCore;
using Microsoft.IdentityModel.Tokens;
using NetworldParkingLot.Api.Common;
using NetworldParkingLot.Api.Data;
using NetworldParkingLot.Api.Features.GateOperations.Repositories;
using NetworldParkingLot.Api.Features.GateOperations.Services;
using NetworldParkingLot.Api.Infrastructure.Printing;
using NetworldParkingLot.Api.Common.Security;
using NetworldParkingLot.Api.Features.UserAccess.Services;
using NetworldParkingLot.Api.Features.Settings.Services;

var builder = WebApplication.CreateBuilder(args);

builder.Services.AddControllers();

var jwtKey = builder.Configuration["Jwt:Key"];
if (string.IsNullOrWhiteSpace(jwtKey) || jwtKey.Length < 32)
{
    jwtKey = "NetworldParkingLot_Default_Development_Key_Change_This_Immediately";
}
var jwtIssuer = builder.Configuration["Jwt:Issuer"] ?? "NetworldParkingLot";
var jwtAudience = builder.Configuration["Jwt:Audience"] ?? "NetworldParkingLotClient";

builder.Services
    .AddAuthentication(JwtBearerDefaults.AuthenticationScheme)
    .AddJwtBearer(options =>
    {
        options.TokenValidationParameters = new TokenValidationParameters
        {
            ValidateIssuer = true,
            ValidateAudience = true,
            ValidateIssuerSigningKey = true,
            ValidateLifetime = true,
            ValidIssuer = jwtIssuer,
            ValidAudience = jwtAudience,
            IssuerSigningKey = new SymmetricSecurityKey(Encoding.UTF8.GetBytes(jwtKey)),
            ClockSkew = TimeSpan.FromMinutes(2)
        };
    });

builder.Services.AddCors(options =>
{
    options.AddPolicy("FlutterWebCors", policy =>
    {
        policy
            .SetIsOriginAllowed(origin =>
            {
                if (string.IsNullOrWhiteSpace(origin)) return false;
                var uri = new Uri(origin);
                return uri.Host == "localhost" || uri.Host == "127.0.0.1" || uri.Host.StartsWith("192.168.");
            })
            .AllowAnyHeader()
            .AllowAnyMethod();
    });
});

builder.Services.AddEndpointsApiExplorer();
builder.Services.AddSwaggerGen();

builder.Services.AddDbContext<NetworldParkingDbContext>(options =>
{
    options.UseSqlServer(builder.Configuration.GetConnectionString("DefaultConnection"));
});

builder.Services.AddScoped<IGateRepository, GateRepository>();
builder.Services.AddScoped<IGateOperationService, GateOperationService>();
builder.Services.AddScoped<IWindowsRawPrinterService, WindowsRawPrinterService>();
builder.Services.AddScoped<JwtTokenService>();
builder.Services.AddScoped<IUserAccessService, UserAccessService>();
builder.Services.AddScoped<ISettingsService, SettingsService>();

var app = builder.Build();

app.UseMiddleware<GlobalExceptionMiddleware>();

app.UseSwagger();
app.UseSwaggerUI();

app.MapGet("/", context =>
{
    context.Response.Redirect("/swagger");
    return Task.CompletedTask;
});

// Keep HTTPS redirection disabled for Flutter Web local development.
// app.UseHttpsRedirection();

app.UseCors("FlutterWebCors");
app.UseAuthentication();
app.UseAuthorization();
app.MapControllers();
app.Run();

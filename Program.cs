using Microsoft.EntityFrameworkCore;
using NetworldParkingLot.Api.Common;
using NetworldParkingLot.Api.Data;
using NetworldParkingLot.Api.Features.GateOperations.Repositories;
using NetworldParkingLot.Api.Features.GateOperations.Services;
using NetworldParkingLot.Api.Infrastructure.Printing;

var builder = WebApplication.CreateBuilder(args);

builder.Services.AddControllers();

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
app.UseAuthorization();
app.MapControllers();
app.Run();

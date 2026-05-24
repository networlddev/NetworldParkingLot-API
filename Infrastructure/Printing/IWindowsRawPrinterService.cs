namespace NetworldParkingLot.Api.Infrastructure.Printing;

public interface IWindowsRawPrinterService
{
    Task SendRawCommandAsync(string printerName, string documentName, string command, CancellationToken cancellationToken = default);
}

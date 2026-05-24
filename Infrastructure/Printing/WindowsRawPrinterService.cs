using System.Runtime.InteropServices;
using System.Text;

namespace NetworldParkingLot.Api.Infrastructure.Printing;

public sealed class WindowsRawPrinterService : IWindowsRawPrinterService
{
    public Task SendRawCommandAsync(string printerName, string documentName, string command, CancellationToken cancellationToken = default)
    {
        if (!OperatingSystem.IsWindows())
            throw new InvalidOperationException("Windows printing is supported only when the API is running on Windows.");

        if (string.IsNullOrWhiteSpace(printerName))
            throw new InvalidOperationException("Printer name is required.");

        if (string.IsNullOrWhiteSpace(command))
            throw new InvalidOperationException("Print command is empty.");

        return Task.Run(() => Send(printerName, documentName, command), cancellationToken);
    }

    private static void Send(string printerName, string documentName, string command)
    {
        nint printerHandle = 0;
        var docInfo = new DOCINFOA
        {
            pDocName = documentName,
            pDataType = "RAW"
        };

        try
        {
            if (!OpenPrinter(printerName.Normalize(), out printerHandle, IntPtr.Zero))
                throw new InvalidOperationException($"Printer is not available: {printerName}");

            if (!StartDocPrinter(printerHandle, 1, docInfo))
                throw new InvalidOperationException("Cannot start printer document.");

            if (!StartPagePrinter(printerHandle))
                throw new InvalidOperationException("Cannot start printer page.");

            var bytes = Encoding.ASCII.GetBytes(command);
            if (!WritePrinter(printerHandle, bytes, bytes.Length, out var written) || written != bytes.Length)
                throw new InvalidOperationException("Cannot write data to printer.");

            EndPagePrinter(printerHandle);
            EndDocPrinter(printerHandle);
        }
        finally
        {
            if (printerHandle != 0)
                ClosePrinter(printerHandle);
        }
    }

    [StructLayout(LayoutKind.Sequential, CharSet = CharSet.Ansi)]
    private sealed class DOCINFOA
    {
        [MarshalAs(UnmanagedType.LPStr)] public string? pDocName;
        [MarshalAs(UnmanagedType.LPStr)] public string? pOutputFile;
        [MarshalAs(UnmanagedType.LPStr)] public string? pDataType;
    }

    [DllImport("winspool.Drv", EntryPoint = "OpenPrinterA", SetLastError = true, CharSet = CharSet.Ansi)]
    private static extern bool OpenPrinter(string szPrinter, out nint hPrinter, IntPtr pd);

    [DllImport("winspool.Drv", EntryPoint = "ClosePrinter", SetLastError = true)]
    private static extern bool ClosePrinter(nint hPrinter);

    [DllImport("winspool.Drv", EntryPoint = "StartDocPrinterA", SetLastError = true, CharSet = CharSet.Ansi)]
    private static extern bool StartDocPrinter(nint hPrinter, int level, [In] DOCINFOA di);

    [DllImport("winspool.Drv", EntryPoint = "EndDocPrinter", SetLastError = true)]
    private static extern bool EndDocPrinter(nint hPrinter);

    [DllImport("winspool.Drv", EntryPoint = "StartPagePrinter", SetLastError = true)]
    private static extern bool StartPagePrinter(nint hPrinter);

    [DllImport("winspool.Drv", EntryPoint = "EndPagePrinter", SetLastError = true)]
    private static extern bool EndPagePrinter(nint hPrinter);

    [DllImport("winspool.Drv", EntryPoint = "WritePrinter", SetLastError = true)]
    private static extern bool WritePrinter(nint hPrinter, byte[] pBytes, int dwCount, out int dwWritten);
}

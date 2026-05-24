using SkiaSharp;
using ZXing;
using ZXing.Common;
using ZXing.SkiaSharp;

namespace NetworldParkingLot.Api.Common.Printing;

public static class BarcodeLabelImageGenerator
{
    public static byte[] GenerateLabelPng(
        string projectName,
        string companyName,
        string vehicleReference,
        string barcodeNo,
        string validUntil,
        string note,
        int widthMm,
        int heightMm,
        int dpi,
        bool rotate90)
    {
        var widthPx = MmToPx(widthMm, dpi);
        var heightPx = MmToPx(heightMm, dpi);

        using var surface = SKSurface.Create(new SKImageInfo(widthPx, heightPx, SKColorType.Rgba8888, SKAlphaType.Premul));
        var canvas = surface.Canvas;
        canvas.Clear(SKColors.White);

        using var black = new SKPaint
        {
            Color = SKColors.Black,
            IsAntialias = true
        };

        using var gray = new SKPaint
        {
            Color = new SKColor(55, 65, 81),
            IsAntialias = true
        };

        // 60mm x 35mm at 203 DPI is around 480 x 280 px.
        // These positions are intentionally pixel-based to keep the final printed image stable.
        DrawCenteredText(canvas, Safe(projectName, 34), widthPx / 2f, ScaleY(22, heightPx), ScaleFont(15, heightPx), true, black);
        DrawCenteredText(canvas, Safe(companyName, 38), widthPx / 2f, ScaleY(43, heightPx), ScaleFont(12, heightPx), true, black);
        DrawCenteredText(canvas, Safe(vehicleReference, 30), widthPx / 2f, ScaleY(68, heightPx), ScaleFont(18, heightPx), true, black);

        var barcodeTop = ScaleY(84, heightPx);
        var barcodeHeight = ScaleY(82, heightPx);
        var barcodeLeft = ScaleX(36, widthPx);
        var barcodeRight = widthPx - ScaleX(36, widthPx);

        using var barcodeBitmap = GenerateCode128Bitmap(
            barcodeNo,
            Math.Max(120, (int)(barcodeRight - barcodeLeft)),
            Math.Max(35, (int)barcodeHeight));

        canvas.DrawBitmap(
            barcodeBitmap,
            new SKRect(barcodeLeft, barcodeTop, barcodeRight, barcodeTop + barcodeHeight));

        DrawCenteredText(canvas, Safe(barcodeNo, 34), widthPx / 2f, ScaleY(186, heightPx), ScaleFont(12, heightPx), true, black);
        DrawCenteredText(canvas, Safe($"Valid Until: {validUntil}", 42), widthPx / 2f, ScaleY(207, heightPx), ScaleFont(9, heightPx), false, gray);
        DrawCenteredText(canvas, Safe(note, 46), widthPx / 2f, ScaleY(226, heightPx), ScaleFont(8.5f, heightPx), false, gray);

        using var image = surface.Snapshot();

        if (!rotate90)
        {
            using var data = image.Encode(SKEncodedImageFormat.Png, 100);
            return data.ToArray();
        }

        using var rotatedBitmap = RotateImage90(image);
        using var rotatedImage = SKImage.FromBitmap(rotatedBitmap);
        using var rotatedData = rotatedImage.Encode(SKEncodedImageFormat.Png, 100);
        return rotatedData.ToArray();
    }

    private static SKBitmap GenerateCode128Bitmap(string value, int width, int height)
    {
        var writer = new BarcodeWriter
        {
            Format = BarcodeFormat.CODE_128,
            Options = new EncodingOptions
            {
                Width = width,
                Height = height,
                Margin = 0,
                PureBarcode = true
            }
        };

        return writer.Write(value);
    }

    private static void DrawCenteredText(
        SKCanvas canvas,
        string text,
        float centerX,
        float baselineY,
        float fontSize,
        bool bold,
        SKPaint paint)
    {
        using var font = new SKFont
        {
            Size = fontSize,
            Typeface = SKTypeface.FromFamilyName(
                "Arial",
                bold ? SKFontStyle.Bold : SKFontStyle.Normal)
        };

        var textWidth = font.MeasureText(text);
        canvas.DrawText(text, centerX - (textWidth / 2f), baselineY, font, paint);
    }

    private static SKBitmap RotateImage90(SKImage image)
    {
        using var bitmap = SKBitmap.FromImage(image);
        var rotated = new SKBitmap(bitmap.Height, bitmap.Width, bitmap.ColorType, bitmap.AlphaType);

        using var canvas = new SKCanvas(rotated);
        canvas.Clear(SKColors.White);
        canvas.Translate(bitmap.Height, 0);
        canvas.RotateDegrees(90);
        canvas.DrawBitmap(bitmap, 0, 0);

        return rotated;
    }

    private static int MmToPx(int mm, int dpi) =>
        Math.Max(1, (int)Math.Round(mm / 25.4d * dpi));

    private static float ScaleX(float value, int actualWidthPx) => value / 480f * actualWidthPx;
    private static float ScaleY(float value, int actualHeightPx) => value / 280f * actualHeightPx;
    private static float ScaleFont(float value, int actualHeightPx) => Math.Max(6f, value / 280f * actualHeightPx);

    private static string Safe(string? value, int maxLength)
    {
        value = string.IsNullOrWhiteSpace(value) ? "-" : value.Trim();
        value = value.Replace("\r", " ").Replace("\n", " ").Replace("\"", "'");
        return value.Length <= maxLength ? value : value[..maxLength];
    }
}

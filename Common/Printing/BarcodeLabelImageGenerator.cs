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
        string? companyCode,
        string? companyContact,
        string? companyMobile,
        string? companyTrn,
        string vehicleReference,
        string? vehicleType,
        string? driverName,
        string? driverMobile,
        string barcodeNo,
        string stickerCreatedTime,
        string entryNumberText,
        string validUntil,
        string subscriptionText,
        string note,
        int widthMm,
        int heightMm,
        int dpi,
        int marginLeftMm,
        int marginTopMm,
        int marginRightMm,
        int marginBottomMm,
        int textScalePercent,
        int symbolScalePercent,
        bool rotate90)
    {
        var widthPx = MmToPx(widthMm, dpi);
        var heightPx = MmToPx(heightMm, dpi);
        var marginLeftPx = Math.Min(MmToPx(Math.Max(0, marginLeftMm), dpi), widthPx - 2);
        var marginTopPx = Math.Min(MmToPx(Math.Max(0, marginTopMm), dpi), heightPx - 2);
        var marginRightPx = Math.Min(MmToPx(Math.Max(0, marginRightMm), dpi), widthPx - marginLeftPx - 1);
        var marginBottomPx = Math.Min(MmToPx(Math.Max(0, marginBottomMm), dpi), heightPx - marginTopPx - 1);
        var contentLeft = marginLeftPx;
        var contentRight = Math.Max(contentLeft + 1, widthPx - marginRightPx);
        var contentCenterX = (contentLeft + contentRight) / 2f;

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

        var largeLabel = heightMm >= 90 || widthMm >= 90;
        var y = marginTopPx + ScaleY(largeLabel ? 16 : 6, heightPx);
        DrawCenteredText(canvas, Safe(projectName, 42), contentCenterX, y, ScaleFont(largeLabel ? 10 : 15, heightPx, textScalePercent), true, black);
        y += ScaleY(largeLabel ? 19 : 21, heightPx);
        DrawCenteredText(canvas, Safe($"Company : {companyName}", 64), contentCenterX, y, ScaleFont(largeLabel ? 12 : 12, heightPx, textScalePercent), true, black);

        if (largeLabel)
        {
            y += ScaleY(17, heightPx);
            DrawText(canvas, $"Code: {Safe(companyCode, 22)}", contentLeft, y, ScaleFont(6.5f, heightPx, textScalePercent), false, gray);
            DrawText(canvas, $"Mobile: {Safe(companyMobile, 22)}", contentLeft + ((contentRight - contentLeft) / 2f), y, ScaleFont(6.5f, heightPx, textScalePercent), false, gray);
            y += ScaleY(14, heightPx);
            DrawText(canvas, $"Contact: {Safe(companyContact, 28)}", contentLeft, y, ScaleFont(6.5f, heightPx, textScalePercent), false, gray);
            DrawText(canvas, $"TRN: {Safe(companyTrn, 24)}", contentLeft + ((contentRight - contentLeft) / 2f), y, ScaleFont(6.5f, heightPx, textScalePercent), false, gray);
            y += ScaleY(16, heightPx);
            DrawText(canvas, $"Subscription: {Safe(subscriptionText, 72)}", contentLeft, y, ScaleFont(6.8f, heightPx, textScalePercent), false, black);
            y += ScaleY(16, heightPx);
            DrawText(canvas, $"Entry Number: {Safe(entryNumberText, 24)}", contentLeft, y, ScaleFont(6.8f, heightPx, textScalePercent), true, black);
            DrawText(canvas, $"Valid Until: {Safe(validUntil, 24)}", contentLeft + ((contentRight - contentLeft) / 2f), y, ScaleFont(6.8f, heightPx, textScalePercent), false, black);
            y += ScaleY(16, heightPx);
            DrawText(canvas, $"Sticker Created: {Safe(stickerCreatedTime, 28)}", contentLeft, y, ScaleFont(6.8f, heightPx, textScalePercent), false, black);
            y += ScaleY(18, heightPx);
            DrawText(canvas, $"Vehicle: {Safe(vehicleType, 18)}  {Safe(vehicleReference, 28)}", contentLeft, y, ScaleFont(8f, heightPx, textScalePercent), true, black);
            y += ScaleY(16, heightPx);
            DrawText(canvas, $"Driver: {Safe(driverName, 28)}  {Safe(driverMobile, 22)}", contentLeft, y, ScaleFont(6.5f, heightPx, textScalePercent), false, gray);
        }
        else
        {
            y += ScaleY(25, heightPx);
            DrawCenteredText(canvas, Safe(vehicleReference, 30), contentCenterX, y, ScaleFont(18, heightPx, textScalePercent), true, black);
        }

        var barcodeTop = y + ScaleY(largeLabel ? 18 : 16, heightPx);
        var barcodeLeft = contentLeft;
        var barcodeRight = contentRight;
        var maxBarcodeHeight = heightPx - marginBottomPx - barcodeTop - ScaleY(largeLabel ? 48 : 64, heightPx);
        var barcodeHeight = Math.Max(ScaleY(largeLabel ? 72 : 35, heightPx), Math.Min(ScaleY(largeLabel ? 118 : 82, heightPx) * (symbolScalePercent / 100f), maxBarcodeHeight));

        using var barcodeBitmap = GenerateCode128Bitmap(
            barcodeNo,
            Math.Max(120, (int)(barcodeRight - barcodeLeft)),
            Math.Max(35, (int)barcodeHeight));

        canvas.DrawBitmap(
            barcodeBitmap,
            new SKRect(barcodeLeft, barcodeTop, barcodeRight, barcodeTop + barcodeHeight));

        DrawCenteredText(canvas, Safe(barcodeNo, 44), contentCenterX, barcodeTop + barcodeHeight + ScaleY(largeLabel ? 18 : 20, heightPx), ScaleFont(largeLabel ? 8.5f : 12, heightPx, textScalePercent), true, black);
        if (!largeLabel)
            DrawCenteredText(canvas, Safe($"Valid Until: {validUntil}", 42), contentCenterX, barcodeTop + barcodeHeight + ScaleY(41, heightPx), ScaleFont(9, heightPx, textScalePercent), false, gray);
        DrawCenteredText(canvas, Safe(note, 62), contentCenterX, barcodeTop + barcodeHeight + ScaleY(largeLabel ? 34 : 60, heightPx), ScaleFont(largeLabel ? 6.2f : 8.5f, heightPx, textScalePercent), false, gray);

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

    private static void DrawText(
        SKCanvas canvas,
        string text,
        float x,
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

        canvas.DrawText(text, x, baselineY, font, paint);
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
    private static float ScaleFont(float value, int actualHeightPx, int scalePercent = 100) => Math.Max(6f, value / 280f * actualHeightPx * (Math.Clamp(scalePercent, 60, 250) / 100f));

    private static string Safe(string? value, int maxLength)
    {
        value = string.IsNullOrWhiteSpace(value) ? "-" : value.Trim();
        value = value.Replace("\r", " ").Replace("\n", " ").Replace("\"", "'");
        return value.Length <= maxLength ? value : value[..maxLength];
    }
}

using System.Drawing;
using System.Drawing.Drawing2D;
using System.Drawing.Imaging;

namespace VRCInventoryManager.Core;

public static class ImagePayloadFactory
{
    public const int MaxInventoryImageSize = 1024;
    public const int MaxUploadBytes = 10 * 1024 * 1024;

    public static byte[] GetPngPayload(string path)
    {
        using Image source = Image.FromFile(path);
        if (CanUseOriginalPng(path, source))
        {
            return File.ReadAllBytes(path);
        }

        Size size = FitWithinLimit(source.Width, source.Height, MaxInventoryImageSize);
        return RenderPng(source, size.Width, size.Height);
    }

    public static byte[] GetSpriteSheetPngPayload(string path)
    {
        if (!IsPngPath(path))
        {
            throw new InvalidOperationException("Animated sprite sheet upload requires a PNG file.");
        }

        using Image source = Image.FromFile(path);
        if (source.Width != source.Height)
        {
            throw new InvalidOperationException("Animated sprite sheet upload requires a square PNG.");
        }

        if (CanUseOriginalPng(path, source))
        {
            return File.ReadAllBytes(path);
        }

        int side = Math.Min(source.Width, MaxInventoryImageSize);
        return RenderPng(source, side, side);
    }

    public static bool NeedsStaticNormalization(string path)
    {
        using Image source = Image.FromFile(path);
        return !CanUseOriginalPng(path, source);
    }

    private static bool CanUseOriginalPng(string path, Image source) =>
        IsPngPath(path) &&
        source.Width <= MaxInventoryImageSize &&
        source.Height <= MaxInventoryImageSize &&
        new FileInfo(path).Length < MaxUploadBytes;

    private static byte[] RenderPng(Image source, int width, int height)
    {
        using Bitmap bitmap = new(width, height, PixelFormat.Format32bppArgb);
        using Graphics graphics = Graphics.FromImage(bitmap);
        ConfigureGraphics(graphics);
        graphics.Clear(Color.Transparent);
        graphics.DrawImage(source, 0, 0, width, height);

        using MemoryStream stream = new();
        bitmap.Save(stream, ImageFormat.Png);
        return stream.ToArray();
    }

    private static Size FitWithinLimit(int width, int height, int maxSide)
    {
        if (width <= 0 || height <= 0)
        {
            throw new InvalidOperationException("Image dimensions must be positive.");
        }

        if (width <= maxSide && height <= maxSide)
        {
            return new Size(width, height);
        }

        double scale = Math.Min(maxSide / (double)width, maxSide / (double)height);
        return new Size(
            Math.Max(1, (int)Math.Round(width * scale)),
            Math.Max(1, (int)Math.Round(height * scale)));
    }

    private static bool IsPngPath(string path) =>
        string.Equals(Path.GetExtension(path), ".png", StringComparison.OrdinalIgnoreCase);

    private static void ConfigureGraphics(Graphics graphics)
    {
        graphics.CompositingMode = CompositingMode.SourceOver;
        graphics.CompositingQuality = CompositingQuality.HighQuality;
        graphics.InterpolationMode = InterpolationMode.HighQualityBicubic;
        graphics.PixelOffsetMode = PixelOffsetMode.HighQuality;
        graphics.SmoothingMode = SmoothingMode.HighQuality;
    }
}

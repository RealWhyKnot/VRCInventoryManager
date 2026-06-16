using System.Drawing;
using System.Drawing.Drawing2D;
using System.Drawing.Imaging;

namespace VRCInventoryManager.Core;

public static class ImagePayloadFactory
{
    public static byte[] GetPngPayload(string path)
    {
        using Image source = Image.FromFile(path);
        int side = Math.Max(source.Width, source.Height);
        using Bitmap bitmap = new(side, side, PixelFormat.Format32bppArgb);
        using Graphics graphics = Graphics.FromImage(bitmap);
        ConfigureGraphics(graphics);
        graphics.Clear(Color.Transparent);

        int x = (side - source.Width) / 2;
        int y = (side - source.Height) / 2;
        graphics.DrawImage(source, x, y, source.Width, source.Height);

        using MemoryStream stream = new();
        bitmap.Save(stream, ImageFormat.Png);
        return stream.ToArray();
    }

    public static bool NeedsSquarePadding(string path)
    {
        using Image source = Image.FromFile(path);
        return source.Width != source.Height;
    }

    private static void ConfigureGraphics(Graphics graphics)
    {
        graphics.CompositingMode = CompositingMode.SourceOver;
        graphics.CompositingQuality = CompositingQuality.HighQuality;
        graphics.InterpolationMode = InterpolationMode.HighQualityBicubic;
        graphics.PixelOffsetMode = PixelOffsetMode.HighQuality;
        graphics.SmoothingMode = SmoothingMode.HighQuality;
    }
}

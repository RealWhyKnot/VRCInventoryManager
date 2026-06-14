using System.Drawing;
using System.Drawing.Imaging;

namespace VRCInventoryManager.Core;

public static class ImagePayloadFactory
{
    public static byte[] GetPngPayload(string path)
    {
        if (string.Equals(Path.GetExtension(path), ".png", StringComparison.OrdinalIgnoreCase))
        {
            return File.ReadAllBytes(path);
        }

        using Image source = Image.FromFile(path);
        using Bitmap bitmap = new(source.Width, source.Height, PixelFormat.Format32bppArgb);
        using Graphics graphics = Graphics.FromImage(bitmap);
        graphics.Clear(Color.Transparent);
        graphics.DrawImage(source, 0, 0, source.Width, source.Height);

        using MemoryStream stream = new();
        bitmap.Save(stream, ImageFormat.Png);
        return stream.ToArray();
    }
}

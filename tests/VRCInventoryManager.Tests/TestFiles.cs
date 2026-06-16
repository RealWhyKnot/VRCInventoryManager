using System.Drawing;
using System.Drawing.Imaging;
using System.IO;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using DrawingColor = System.Drawing.Color;
using MediaColors = System.Windows.Media.Colors;

namespace VRCInventoryManager.Tests;

internal static class TestFiles
{
    public static string CreateTempDirectory()
    {
        string path = Path.Combine(Path.GetTempPath(), "vrc-inventory-tests", Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(path);
        return path;
    }

    public static void WritePng(string path, DrawingColor color)
    {
        using Bitmap bitmap = new(16, 16);
        using Graphics graphics = Graphics.FromImage(bitmap);
        graphics.Clear(color);
        bitmap.Save(path, ImageFormat.Png);
    }

    public static void WriteAnimatedGif(string path)
    {
        GifBitmapEncoder encoder = new();
        encoder.Frames.Add(BitmapFrame.Create(CreateBitmapSource(MediaColors.Red)));
        encoder.Frames.Add(BitmapFrame.Create(CreateBitmapSource(MediaColors.Blue)));
        using FileStream stream = File.Create(path);
        encoder.Save(stream);
    }

    private static BitmapSource CreateBitmapSource(System.Windows.Media.Color color)
    {
        int width = 8;
        int height = 8;
        int stride = width * 4;
        byte[] pixels = new byte[stride * height];
        for (int i = 0; i < pixels.Length; i += 4)
        {
            pixels[i] = color.B;
            pixels[i + 1] = color.G;
            pixels[i + 2] = color.R;
            pixels[i + 3] = color.A;
        }

        return BitmapSource.Create(width, height, 96, 96, PixelFormats.Bgra32, null, pixels, stride);
    }
}

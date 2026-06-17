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

    public static void WritePng(string path, int width, int height, DrawingColor color)
    {
        using Bitmap bitmap = new(width, height, System.Drawing.Imaging.PixelFormat.Format32bppArgb);
        using Graphics graphics = Graphics.FromImage(bitmap);
        graphics.Clear(color);
        bitmap.Save(path, ImageFormat.Png);
    }

    public static void WriteJpeg(string path, int width, int height, DrawingColor color)
    {
        using Bitmap bitmap = new(width, height, System.Drawing.Imaging.PixelFormat.Format24bppRgb);
        using Graphics graphics = Graphics.FromImage(bitmap);
        graphics.Clear(color);
        bitmap.Save(path, ImageFormat.Jpeg);
    }

    public static void WriteAnimatedGif(string path)
    {
        WriteAnimatedGif(path, MediaColors.Red, MediaColors.Blue);
    }

    public static void WriteAnimatedGif(string path, params System.Windows.Media.Color[] colors)
    {
        WriteAnimatedGif(path, 10, colors);
    }

    public static void WriteAnimatedGif(string path, int delayHundredths, params System.Windows.Media.Color[] colors)
    {
        if (colors.Length == 0)
        {
            throw new ArgumentException("At least one GIF frame is required.", nameof(colors));
        }

        using FileStream stream = File.Create(path);
        WriteAscii(stream, "GIF89a");
        WriteUInt16(stream, 8);
        WriteUInt16(stream, 8);
        stream.WriteByte(0xF7);
        stream.WriteByte(0);
        stream.WriteByte(0);

        byte[] palette = new byte[256 * 3];
        for (int i = 0; i < colors.Length && i < 256; i++)
        {
            palette[i * 3] = colors[i].R;
            palette[i * 3 + 1] = colors[i].G;
            palette[i * 3 + 2] = colors[i].B;
        }

        stream.Write(palette, 0, palette.Length);

        stream.WriteByte(0x21);
        stream.WriteByte(0xFF);
        stream.WriteByte(11);
        WriteAscii(stream, "NETSCAPE2.0");
        stream.WriteByte(3);
        stream.WriteByte(1);
        WriteUInt16(stream, 0);
        stream.WriteByte(0);

        for (int i = 0; i < colors.Length; i++)
        {
            byte colorIndex = (byte)Math.Min(i, 255);
            stream.WriteByte(0x21);
            stream.WriteByte(0xF9);
            stream.WriteByte(4);
            stream.WriteByte(0);
            WriteUInt16(stream, (ushort)Math.Max(0, delayHundredths));
            stream.WriteByte(0);
            stream.WriteByte(0);

            stream.WriteByte(0x2C);
            WriteUInt16(stream, 0);
            WriteUInt16(stream, 0);
            WriteUInt16(stream, 8);
            WriteUInt16(stream, 8);
            stream.WriteByte(0);
            stream.WriteByte(8);
            WriteSubBlocks(stream, EncodeSolidGifFrame(colorIndex));
        }

        stream.WriteByte(0x3B);
    }

    public static void WriteOptimizedPartialGif(string path)
    {
        const string base64 =
            "R0lGODlhEAAQAIEAAP8AAAAAAAAAAAAAACH/C05FVFNDQVBFMi4wAwEAAAAh+QQECgAAACwAAAAAEAAQAAAIHQABCBxIsKDBgwgTKlzIsKHDhxAjSpxIsaLFgQEBACH5BAUKAAIALAgABAAIAAgAgf8AAAAA/wAAAAAAAAgPAAMIHEiwoMGDCBMqTBgQADs=";
        File.WriteAllBytes(path, Convert.FromBase64String(base64));
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

    private static byte[] EncodeSolidGifFrame(byte colorIndex)
    {
        List<int> codes = [256];
        for (int i = 0; i < 64; i++)
        {
            codes.Add(colorIndex);
        }

        codes.Add(257);
        return PackCodes(codes, 9);
    }

    private static byte[] PackCodes(IEnumerable<int> codes, int bitsPerCode)
    {
        List<byte> bytes = [];
        int bitBuffer = 0;
        int bitCount = 0;
        foreach (int code in codes)
        {
            bitBuffer |= code << bitCount;
            bitCount += bitsPerCode;
            while (bitCount >= 8)
            {
                bytes.Add((byte)(bitBuffer & 0xFF));
                bitBuffer >>= 8;
                bitCount -= 8;
            }
        }

        if (bitCount > 0)
        {
            bytes.Add((byte)(bitBuffer & 0xFF));
        }

        return bytes.ToArray();
    }

    private static void WriteSubBlocks(Stream stream, byte[] bytes)
    {
        int offset = 0;
        while (offset < bytes.Length)
        {
            int count = Math.Min(255, bytes.Length - offset);
            stream.WriteByte((byte)count);
            stream.Write(bytes, offset, count);
            offset += count;
        }

        stream.WriteByte(0);
    }

    private static void WriteAscii(Stream stream, string text)
    {
        byte[] bytes = System.Text.Encoding.ASCII.GetBytes(text);
        stream.Write(bytes, 0, bytes.Length);
    }

    private static void WriteUInt16(Stream stream, int value)
    {
        stream.WriteByte((byte)(value & 0xFF));
        stream.WriteByte((byte)((value >> 8) & 0xFF));
    }
}

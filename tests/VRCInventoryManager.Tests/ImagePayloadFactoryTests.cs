using System.Drawing;
using System.Drawing.Imaging;
using System.IO;
using VRCInventoryManager.Core;
using DrawingColor = System.Drawing.Color;

namespace VRCInventoryManager.Tests;

internal static class ImagePayloadFactoryTests
{
    public static Task SquarePadNonSquarePngAsync()
    {
        string root = TestFiles.CreateTempDirectory();
        try
        {
            string path = Path.Combine(root, "wide.png");
            TestFiles.WritePng(path, 20, 10, DrawingColor.FromArgb(255, 200, 20, 10));

            TestAssert.True(ImagePayloadFactory.NeedsSquarePadding(path), "padding needed");
            using Bitmap bitmap = Decode(ImagePayloadFactory.GetPngPayload(path));
            TestAssert.Equal(20, bitmap.Width, "payload width");
            TestAssert.Equal(20, bitmap.Height, "payload height");
            TestAssert.Equal(0, bitmap.GetPixel(10, 1).A, "top padding transparent");

            Color content = bitmap.GetPixel(10, 10);
            TestAssert.True(content.A > 240, "content alpha");
            TestAssert.True(content.R > 150, "content red channel");
            return Task.CompletedTask;
        }
        finally
        {
            Directory.Delete(root, recursive: true);
        }
    }

    public static Task SquareEncodeJpegAsync()
    {
        string root = TestFiles.CreateTempDirectory();
        try
        {
            string path = Path.Combine(root, "tall.jpg");
            TestFiles.WriteJpeg(path, 12, 18, DrawingColor.RoyalBlue);

            TestAssert.True(ImagePayloadFactory.NeedsSquarePadding(path), "jpeg padding needed");
            byte[] payload = ImagePayloadFactory.GetPngPayload(path);
            using Bitmap bitmap = Decode(payload);
            TestAssert.Equal(18, bitmap.Width, "jpeg payload width");
            TestAssert.Equal(18, bitmap.Height, "jpeg payload height");
            TestAssert.Equal(0x89, payload[0], "png signature 0");
            TestAssert.Equal((byte)'P', payload[1], "png signature 1");
            return Task.CompletedTask;
        }
        finally
        {
            Directory.Delete(root, recursive: true);
        }
    }

    private static Bitmap Decode(byte[] bytes)
    {
        using MemoryStream stream = new(bytes);
        using Image image = Image.FromStream(stream);
        return new Bitmap(image);
    }
}

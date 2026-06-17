using System.Drawing;
using System.Drawing.Imaging;
using System.IO;
using VRCInventoryManager.Core;
using DrawingColor = System.Drawing.Color;

namespace VRCInventoryManager.Tests;

internal static class ImagePayloadFactoryTests
{
    public static Task PreserveUploadReadyStaticPngAsync()
    {
        string root = TestFiles.CreateTempDirectory();
        try
        {
            string path = Path.Combine(root, "wide.png");
            TestFiles.WritePng(path, 120, 80, DrawingColor.FromArgb(255, 200, 20, 10));

            byte[] expected = File.ReadAllBytes(path);
            TestAssert.False(ImagePayloadFactory.NeedsStaticNormalization(path), "static PNG is upload ready");
            TestAssert.True(expected.SequenceEqual(ImagePayloadFactory.GetPngPayload(path)), "static PNG pass-through");
            return Task.CompletedTask;
        }
        finally
        {
            Directory.Delete(root, recursive: true);
        }
    }

    public static Task ConvertJpegPayloadToPngWithoutSquarePaddingAsync()
    {
        string root = TestFiles.CreateTempDirectory();
        try
        {
            string path = Path.Combine(root, "tall.jpg");
            TestFiles.WriteJpeg(path, 80, 120, DrawingColor.RoyalBlue);

            TestAssert.True(ImagePayloadFactory.NeedsStaticNormalization(path), "jpeg conversion needed");
            byte[] payload = ImagePayloadFactory.GetPngPayload(path);
            using Bitmap bitmap = Decode(payload);
            TestAssert.Equal(80, bitmap.Width, "jpeg payload width");
            TestAssert.Equal(120, bitmap.Height, "jpeg payload height");
            TestAssert.Equal(0x89, payload[0], "png signature 0");
            TestAssert.Equal((byte)'P', payload[1], "png signature 1");
            return Task.CompletedTask;
        }
        finally
        {
            Directory.Delete(root, recursive: true);
        }
    }

    public static Task DownscaleOversizedStaticPayloadAsync()
    {
        string root = TestFiles.CreateTempDirectory();
        try
        {
            string path = Path.Combine(root, "large.png");
            TestFiles.WritePng(path, 2048, 1024, DrawingColor.ForestGreen);

            TestAssert.True(ImagePayloadFactory.NeedsStaticNormalization(path), "large PNG normalization needed");
            using Bitmap bitmap = Decode(ImagePayloadFactory.GetPngPayload(path));
            TestAssert.Equal(1024, bitmap.Width, "downscaled width");
            TestAssert.Equal(512, bitmap.Height, "downscaled height");
            return Task.CompletedTask;
        }
        finally
        {
            Directory.Delete(root, recursive: true);
        }
    }

    public static Task PreserveSquareSpriteSheetPngAsync()
    {
        string root = TestFiles.CreateTempDirectory();
        try
        {
            string path = Path.Combine(root, "sheet_stopanimationStyle_4frames_20fps.png");
            TestFiles.WritePng(path, 128, 128, DrawingColor.Purple);

            byte[] expected = File.ReadAllBytes(path);
            TestAssert.True(expected.SequenceEqual(ImagePayloadFactory.GetSpriteSheetPngPayload(path)), "sprite sheet PNG pass-through");
            return Task.CompletedTask;
        }
        finally
        {
            Directory.Delete(root, recursive: true);
        }
    }

    public static async Task RejectNonSquareSpriteSheetPngAsync()
    {
        string root = TestFiles.CreateTempDirectory();
        try
        {
            string path = Path.Combine(root, "sheet_stopanimationStyle_4frames_20fps.png");
            TestFiles.WritePng(path, 128, 64, DrawingColor.Purple);

            await TestAssert.ThrowsAsync<InvalidOperationException>(
                () => Task.FromResult(ImagePayloadFactory.GetSpriteSheetPngPayload(path)),
                "non-square sprite sheet rejected");
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

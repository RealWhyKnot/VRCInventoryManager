using System.Drawing;
using System.IO;
using System.Windows.Media;
using VRCInventoryManager.Core;
using DrawingColor = System.Drawing.Color;
using MediaColors = System.Windows.Media.Colors;

namespace VRCInventoryManager.Tests;

internal static class GifSpriteSheetConverterTests
{
    public static Task ConvertAnimatedGifToSpriteSheetAsync()
    {
        string root = TestFiles.CreateTempDirectory();
        string gifPath = Path.Combine(root, "animated.gif");
        try
        {
            TestFiles.WriteAnimatedGif(gifPath);
            GifSpriteSheetConverter converter = new();
            SpriteSheetResult result = converter.Convert(gifPath);
            TestAssert.Equal(2, result.Frames, "frame count");
            TestAssert.True(result.FramesOverTime >= 1 && result.FramesOverTime <= 64, "fps clamp");
            TestAssert.Equal(1024, result.CanvasSize, "canvas size");
            TestAssert.True(result.PngBytes.Length > 8, "png bytes present");
            TestAssert.Equal(0x89, result.PngBytes[0], "png signature 0");
            TestAssert.Equal((byte)'P', result.PngBytes[1], "png signature 1");
            return Task.CompletedTask;
        }
        finally
        {
            Directory.Delete(root, recursive: true);
        }
    }

    public static Task UsePowerOfTwoSpriteSheetGridAsync()
    {
        string root = TestFiles.CreateTempDirectory();
        string gifPath = Path.Combine(root, "six.gif");
        try
        {
            TestFiles.WriteAnimatedGif(
                gifPath,
                MediaColors.Red,
                MediaColors.Green,
                MediaColors.Blue,
                MediaColors.Yellow,
                MediaColors.Purple,
                MediaColors.Orange);
            GifSpriteSheetConverter converter = new();
            SpriteSheetResult result = converter.Convert(gifPath);
            TestAssert.Equal(6, result.Frames, "frame count");
            TestAssert.Equal(4, result.Grid, "grid");
            TestAssert.Equal(256, result.CellSize, "cell size");
            return Task.CompletedTask;
        }
        finally
        {
            Directory.Delete(root, recursive: true);
        }
    }

    public static Task PreserveDurationWhenDownsamplingGifAsync()
    {
        string root = TestFiles.CreateTempDirectory();
        string gifPath = Path.Combine(root, "many.gif");
        try
        {
            System.Windows.Media.Color[] colors = Enumerable
                .Range(0, 128)
                .Select(index => System.Windows.Media.Color.FromRgb((byte)index, (byte)(255 - index), (byte)(index / 2)))
                .ToArray();
            TestFiles.WriteAnimatedGif(gifPath, 10, colors);

            GifSpriteSheetConverter converter = new();
            SpriteSheetResult result = converter.Convert(gifPath);
            TestAssert.Equal(64, result.Frames, "downsampled frame count");
            TestAssert.Equal(5, result.FramesOverTime, "duration-preserving fps");
            return Task.CompletedTask;
        }
        finally
        {
            Directory.Delete(root, recursive: true);
        }
    }

    public static Task ConvertOptimizedGifUsingCompositedFramesAsync()
    {
        string root = TestFiles.CreateTempDirectory();
        string gifPath = Path.Combine(root, "optimized.gif");
        try
        {
            TestFiles.WriteOptimizedPartialGif(gifPath);

            GifSpriteSheetConverter converter = new();
            SpriteSheetResult result = converter.Convert(gifPath, canvasSize: 128);

            TestAssert.Equal(2, result.Frames, "frame count");
            TestAssert.Equal(10, result.FramesOverTime, "fps");
            using Bitmap sheet = Decode(result.PngBytes);
            AssertColorNear(DrawingColor.Red, sheet.GetPixel(16, 16), "first frame red");
            AssertColorNear(DrawingColor.Red, sheet.GetPixel(72, 8), "second frame retained red background");
            AssertColorNear(DrawingColor.Blue, sheet.GetPixel(112, 32), "second frame blue update");
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

    private static void AssertColorNear(DrawingColor expected, DrawingColor actual, string label)
    {
        TestAssert.True(Math.Abs(expected.R - actual.R) <= 8, $"{label} red");
        TestAssert.True(Math.Abs(expected.G - actual.G) <= 8, $"{label} green");
        TestAssert.True(Math.Abs(expected.B - actual.B) <= 8, $"{label} blue");
        TestAssert.True(actual.A >= 240, $"{label} alpha");
    }
}

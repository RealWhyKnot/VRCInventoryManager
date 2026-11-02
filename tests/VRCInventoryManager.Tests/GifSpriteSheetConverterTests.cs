using System.Drawing;
using System.IO;
using VRCInventoryManager.Core;
using DrawingColor = System.Drawing.Color;
using MediaColors = System.Windows.Media.Colors;

namespace VRCInventoryManager.Tests;

public sealed class GifSpriteSheetConverterTests
{
    [Fact]
    public Task ConvertAnimatedGifToSpriteSheetAsync()
    {
        string root = TestFiles.CreateTempDirectory();
        string gifPath = Path.Combine(root, "animated.gif");
        try
        {
            TestFiles.WriteAnimatedGif(gifPath);
            SpriteSheetResult result = GifSpriteSheetConverter.Convert(gifPath);
            TestAssert.Equal(2, result.Frames, "frame count");
            TestAssert.True(result.FramesOverTime is >= 1 and <= 64, "fps clamp");
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

    [Fact]
    public Task UsePowerOfTwoSpriteSheetGridAsync()
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
            SpriteSheetResult result = GifSpriteSheetConverter.Convert(gifPath);
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

    [Fact]
    public Task PreserveDurationWhenDownsamplingGifAsync()
    {
        string root = TestFiles.CreateTempDirectory();
        string gifPath = Path.Combine(root, "many.gif");
        try
        {
            System.Windows.Media.Color[] colors = [.. Enumerable
                .Range(0, 128)
                .Select(index => System.Windows.Media.Color.FromRgb((byte)index, (byte)(255 - index), (byte)(index / 2)))];
            TestFiles.WriteAnimatedGif(gifPath, 10, colors);
            SpriteSheetResult result = GifSpriteSheetConverter.Convert(gifPath);
            TestAssert.Equal(64, result.Frames, "downsampled frame count");
            TestAssert.Equal(5, result.FramesOverTime, "duration-preserving fps");
            return Task.CompletedTask;
        }
        finally
        {
            Directory.Delete(root, recursive: true);
        }
    }

    [Fact]
    public Task ConvertOptimizedGifUsingCompositedFramesAsync()
    {
        string root = TestFiles.CreateTempDirectory();
        string gifPath = Path.Combine(root, "optimized.gif");
        try
        {
            TestFiles.WriteOptimizedPartialGif(gifPath);
            SpriteSheetResult result = GifSpriteSheetConverter.Convert(gifPath, canvasSize: 128);

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
        using var image = Image.FromStream(stream);
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

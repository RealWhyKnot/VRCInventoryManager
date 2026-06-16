using System.IO;
using System.Windows.Media;
using VRCInventoryManager.Core;
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
}

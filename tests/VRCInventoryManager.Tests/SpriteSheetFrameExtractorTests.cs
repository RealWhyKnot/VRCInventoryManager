using System.Windows;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using VRCInventoryManager.Core;
using MediaColor = System.Windows.Media.Color;
using MediaColors = System.Windows.Media.Colors;

namespace VRCInventoryManager.Tests;

internal static class SpriteSheetFrameExtractorTests
{
    public static Task ExtractSpriteSheetFramesInRowMajorOrderAsync()
    {
        BitmapSource sheet = CreateSheet(
            MediaColors.Red,
            MediaColors.Green,
            MediaColors.Blue,
            MediaColors.Yellow);

        AnimatedImageFrames animation = SpriteSheetFrameExtractor.Extract(sheet, 4, 20, 8);
        TestAssert.Equal(4, animation.Frames.Count, "frame count");
        TestAssert.Equal(TimeSpan.FromMilliseconds(50), animation.Delays[0], "frame delay");
        AssertColor(MediaColors.Red, animation.Frames[0], "frame 0");
        AssertColor(MediaColors.Green, animation.Frames[1], "frame 1");
        AssertColor(MediaColors.Blue, animation.Frames[2], "frame 2");
        AssertColor(MediaColors.Yellow, animation.Frames[3], "frame 3");

        IReadOnlyList<Int32Rect> rects = SpriteSheetFrameExtractor.CalculateFrameRects(16, 16, 4);
        TestAssert.Equal(new Int32Rect(0, 0, 8, 8), rects[0], "rect 0");
        TestAssert.Equal(new Int32Rect(8, 0, 8, 8), rects[1], "rect 1");
        return Task.CompletedTask;
    }

    public static Task RejectInvalidSpriteSheetMetadataAsync()
    {
        BitmapSource sheet = CreateSheet(MediaColors.Red, MediaColors.Green, MediaColors.Blue, MediaColors.Yellow);
        TestAssert.False(SpriteSheetFrameExtractor.CanAnimate(1, 20), "one frame cannot animate");
        TestAssert.False(SpriteSheetFrameExtractor.CanAnimate(4, 0), "zero fps cannot animate");
        TestAssert.Throws<ArgumentOutOfRangeException>(() => SpriteSheetFrameExtractor.Extract(sheet, 1, 20, 8), "reject one frame");
        TestAssert.Throws<ArgumentOutOfRangeException>(() => SpriteSheetFrameExtractor.Extract(sheet, 4, 0, 8), "reject zero fps");
        return Task.CompletedTask;
    }

    public static Task UsePowerOfTwoGridForSparseSpriteSheetsAsync()
    {
        TestAssert.Equal(4, SpriteSheetFrameExtractor.CalculateGrid(6), "six frame grid");
        TestAssert.Equal(4, SpriteSheetFrameExtractor.CalculateGrid(16), "sixteen frame grid");
        TestAssert.Equal(8, SpriteSheetFrameExtractor.CalculateGrid(35), "thirty five frame grid");

        IReadOnlyList<Int32Rect> rects = SpriteSheetFrameExtractor.CalculateFrameRects(16, 16, 6);
        TestAssert.Equal(new Int32Rect(0, 0, 4, 4), rects[0], "sparse rect 0");
        TestAssert.Equal(new Int32Rect(12, 0, 4, 4), rects[3], "sparse rect 3");
        TestAssert.Equal(new Int32Rect(0, 4, 4, 4), rects[4], "sparse rect 4");
        TestAssert.Equal(new Int32Rect(4, 4, 4, 4), rects[5], "sparse rect 5");
        return Task.CompletedTask;
    }

    private static BitmapSource CreateSheet(params MediaColor[] colors)
    {
        int cell = 8;
        int grid = 2;
        int width = cell * grid;
        int height = cell * grid;
        int stride = width * 4;
        byte[] pixels = new byte[stride * height];
        for (int index = 0; index < colors.Length; index++)
        {
            int column = index % grid;
            int row = index / grid;
            MediaColor color = colors[index];
            for (int y = row * cell; y < row * cell + cell; y++)
            {
                for (int x = column * cell; x < column * cell + cell; x++)
                {
                    int offset = y * stride + x * 4;
                    pixels[offset] = color.B;
                    pixels[offset + 1] = color.G;
                    pixels[offset + 2] = color.R;
                    pixels[offset + 3] = color.A;
                }
            }
        }

        BitmapSource source = BitmapSource.Create(width, height, 96, 96, PixelFormats.Bgra32, null, pixels, stride);
        source.Freeze();
        return source;
    }

    private static void AssertColor(MediaColor expected, BitmapSource frame, string label)
    {
        byte[] pixel = new byte[4];
        frame.CopyPixels(new Int32Rect(frame.PixelWidth / 2, frame.PixelHeight / 2, 1, 1), pixel, 4, 0);
        TestAssert.Equal(expected.B, pixel[0], $"{label} blue");
        TestAssert.Equal(expected.G, pixel[1], $"{label} green");
        TestAssert.Equal(expected.R, pixel[2], $"{label} red");
        TestAssert.Equal(expected.A, pixel[3], $"{label} alpha");
    }
}

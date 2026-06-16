using System.Windows;
using System.Windows.Media;
using System.Windows.Media.Imaging;

namespace VRCInventoryManager.Core;

public static class SpriteSheetFrameExtractor
{
    public static bool CanAnimate(int? frames, int? framesOverTime) =>
        frames.GetValueOrDefault() > 1 && framesOverTime.GetValueOrDefault() > 0;

    public static AnimatedImageFrames Extract(
        BitmapSource spriteSheet,
        int frames,
        int framesOverTime,
        int maxFrameWidth)
    {
        if (frames <= 1)
        {
            throw new ArgumentOutOfRangeException(nameof(frames), "Sprite sheet animation requires at least two frames.");
        }

        if (framesOverTime <= 0)
        {
            throw new ArgumentOutOfRangeException(nameof(framesOverTime), "Sprite sheet animation requires a positive frame rate.");
        }

        IReadOnlyList<Int32Rect> rects = CalculateFrameRects(spriteSheet.PixelWidth, spriteSheet.PixelHeight, frames);
        List<BitmapSource> outputFrames = new(rects.Count);
        foreach (Int32Rect rect in rects)
        {
            CroppedBitmap cropped = new(spriteSheet, rect);
            outputFrames.Add(ResizeFrame(cropped, maxFrameWidth));
        }

        TimeSpan delay = TimeSpan.FromMilliseconds(1000.0 / Math.Clamp(framesOverTime, 1, GifSpriteSheetConverter.MaxFramesPerSecond));
        return new AnimatedImageFrames(outputFrames, Enumerable.Repeat(delay, outputFrames.Count).ToArray());
    }

    public static IReadOnlyList<Int32Rect> CalculateFrameRects(int spriteSheetWidth, int spriteSheetHeight, int frames)
    {
        if (spriteSheetWidth <= 0)
        {
            throw new ArgumentOutOfRangeException(nameof(spriteSheetWidth), "Sprite sheet width must be positive.");
        }

        if (spriteSheetHeight <= 0)
        {
            throw new ArgumentOutOfRangeException(nameof(spriteSheetHeight), "Sprite sheet height must be positive.");
        }

        if (frames <= 0)
        {
            throw new ArgumentOutOfRangeException(nameof(frames), "Frame count must be positive.");
        }

        int grid = CalculateGrid(frames);
        int cellWidth = Math.Max(1, spriteSheetWidth / grid);
        int cellHeight = Math.Max(1, spriteSheetHeight / grid);

        List<Int32Rect> rects = new(frames);
        for (int index = 0; index < frames; index++)
        {
            int column = index % grid;
            int row = index / grid;
            int x = column * cellWidth;
            int y = row * cellHeight;
            int width = Math.Min(cellWidth, spriteSheetWidth - x);
            int height = Math.Min(cellHeight, spriteSheetHeight - y);
            if (width <= 0 || height <= 0)
            {
                throw new InvalidOperationException("Frame count exceeds the available sprite sheet cells.");
            }

            rects.Add(new Int32Rect(x, y, width, height));
        }

        return rects;
    }

    public static int CalculateGrid(int frames)
    {
        if (frames <= 0)
        {
            throw new ArgumentOutOfRangeException(nameof(frames), "Frame count must be positive.");
        }

        int minimum = (int)Math.Ceiling(Math.Sqrt(frames));
        int grid = 1;
        while (grid < minimum)
        {
            grid *= 2;
        }

        return Math.Min(grid, 8);
    }

    private static BitmapSource ResizeFrame(BitmapSource source, int maxFrameWidth)
    {
        int decodeWidth = Math.Max(1, maxFrameWidth);
        double scale = source.PixelWidth <= 0 || source.PixelWidth <= decodeWidth
            ? 1.0
            : decodeWidth / (double)source.PixelWidth;
        BitmapSource result = scale >= 1.0
            ? source
            : new TransformedBitmap(source, new ScaleTransform(scale, scale));
        if (!result.CanFreeze)
        {
            result = new CachedBitmap(result, BitmapCreateOptions.None, BitmapCacheOption.OnLoad);
        }

        result.Freeze();
        return result;
    }
}

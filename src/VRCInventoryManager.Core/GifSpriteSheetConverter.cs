using System.Drawing;
using System.Drawing.Drawing2D;
using System.Drawing.Imaging;

namespace VRCInventoryManager.Core;

public sealed class GifSpriteSheetConverter
{
    public const int DefaultCanvasSize = 1024;
    public const int MaxFrames = 64;
    public const int MaxFramesPerSecond = 64;
    private const int PropertyTagFrameDelay = 0x5100;
    private const int DefaultFrameDelayHundredths = 10;

    public SpriteSheetResult Convert(string gifPath, int canvasSize = DefaultCanvasSize)
    {
        using Image gif = Image.FromFile(gifPath);
        if (gif.FrameDimensionsList.Length == 0)
        {
            throw new InvalidOperationException("GIF did not contain frame dimensions.");
        }

        FrameDimension frameDimension = new(gif.FrameDimensionsList[0]);
        int sourceFrameCount = gif.GetFrameCount(frameDimension);
        if (sourceFrameCount < 2)
        {
            throw new InvalidOperationException("Animated emoji upload requires a GIF with at least two frames.");
        }

        int[] frameDelays = ReadFrameDelays(gif, sourceFrameCount);
        int outputFrameCount = Math.Clamp(sourceFrameCount, 2, MaxFrames);
        int[] frameIndexes = ChooseFrameIndexes(sourceFrameCount, outputFrameCount);
        int framesPerSecond = EstimateFramesPerSecond(frameDelays, outputFrameCount);
        int grid = SpriteSheetFrameExtractor.CalculateGrid(outputFrameCount);
        int cellSize = canvasSize / grid;

        using Bitmap sheet = new(canvasSize, canvasSize, PixelFormat.Format32bppArgb);
        using (Graphics graphics = Graphics.FromImage(sheet))
        {
            ConfigureGraphics(graphics);
            graphics.Clear(Color.Transparent);
            for (int i = 0; i < frameIndexes.Length; i++)
            {
                gif.SelectActiveFrame(frameDimension, frameIndexes[i]);
                using Bitmap frame = CopySelectedFrame(gif);
                RectangleF destination = GetCenteredDestination(frame.Width, frame.Height, i, grid, cellSize);
                graphics.DrawImage(frame, destination);
            }
        }

        using MemoryStream stream = new();
        sheet.Save(stream, ImageFormat.Png);
        return new SpriteSheetResult(stream.ToArray(), outputFrameCount, framesPerSecond, canvasSize, grid, cellSize);
    }

    private static int[] ChooseFrameIndexes(int sourceFrameCount, int outputFrameCount)
    {
        if (sourceFrameCount == outputFrameCount)
        {
            return Enumerable.Range(0, sourceFrameCount).ToArray();
        }

        int[] indexes = new int[outputFrameCount];
        for (int i = 0; i < outputFrameCount; i++)
        {
            indexes[i] = (int)Math.Round(i * (sourceFrameCount - 1) / (double)(outputFrameCount - 1));
        }

        return indexes;
    }

    private static RectangleF GetCenteredDestination(int width, int height, int frameIndex, int grid, int cellSize)
    {
        int column = frameIndex % grid;
        int row = frameIndex / grid;
        double scale = Math.Min(cellSize / (double)Math.Max(1, width), cellSize / (double)Math.Max(1, height));
        double scaledWidth = Math.Max(1, Math.Round(width * scale));
        double scaledHeight = Math.Max(1, Math.Round(height * scale));
        double x = column * cellSize + (cellSize - scaledWidth) / 2;
        double y = row * cellSize + (cellSize - scaledHeight) / 2;
        return new RectangleF((float)x, (float)y, (float)scaledWidth, (float)scaledHeight);
    }

    private static int EstimateFramesPerSecond(IReadOnlyList<int> frameDelays, int outputFrameCount)
    {
        double durationSeconds = frameDelays
            .Select(delay => delay > 0 ? delay : DefaultFrameDelayHundredths)
            .Sum() / 100.0;
        int fps = (int)Math.Round(outputFrameCount / Math.Max(durationSeconds, 0.01));
        return Math.Clamp(fps, 1, MaxFramesPerSecond);
    }

    private static int[] ReadFrameDelays(Image image, int frameCount)
    {
        int[] delays = Enumerable.Repeat(DefaultFrameDelayHundredths, frameCount).ToArray();
        try
        {
            PropertyItem? item = image.GetPropertyItem(PropertyTagFrameDelay);
            byte[]? values = item?.Value;
            if (values is null)
            {
                return delays;
            }

            for (int i = 0; i < frameCount && i * 4 + 3 < values.Length; i++)
            {
                int delay = BitConverter.ToInt32(values, i * 4);
                if (delay > 0)
                {
                    delays[i] = delay;
                }
            }
        }
        catch (ArgumentException)
        {
        }

        return delays;
    }

    private static Bitmap CopySelectedFrame(Image gif)
    {
        Bitmap frame = new(gif.Width, gif.Height, PixelFormat.Format32bppArgb);
        using Graphics graphics = Graphics.FromImage(frame);
        ConfigureGraphics(graphics);
        graphics.Clear(Color.Transparent);
        graphics.DrawImage(gif, 0, 0, gif.Width, gif.Height);
        return frame;
    }

    private static void ConfigureGraphics(Graphics graphics)
    {
        graphics.CompositingMode = CompositingMode.SourceOver;
        graphics.CompositingQuality = CompositingQuality.HighQuality;
        graphics.InterpolationMode = InterpolationMode.HighQualityBicubic;
        graphics.PixelOffsetMode = PixelOffsetMode.HighQuality;
        graphics.SmoothingMode = SmoothingMode.HighQuality;
    }
}

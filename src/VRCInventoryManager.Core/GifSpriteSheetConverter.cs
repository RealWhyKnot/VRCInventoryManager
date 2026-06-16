using System.Windows;
using System.Windows.Media;
using System.Windows.Media.Imaging;

namespace VRCInventoryManager.Core;

public sealed class GifSpriteSheetConverter
{
    public const int DefaultCanvasSize = 1024;
    public const int MaxFrames = 64;
    public const int MaxFramesPerSecond = 64;

    public SpriteSheetResult Convert(string gifPath, int canvasSize = DefaultCanvasSize)
    {
        GifBitmapDecoder decoder = new(
            new Uri(gifPath, UriKind.Absolute),
            BitmapCreateOptions.PreservePixelFormat,
            BitmapCacheOption.OnLoad);

        int sourceFrameCount = decoder.Frames.Count;
        if (sourceFrameCount < 2)
        {
            throw new InvalidOperationException("Animated emoji upload requires a GIF with at least two frames.");
        }

        int outputFrameCount = Math.Clamp(sourceFrameCount, 2, MaxFrames);
        int[] frameIndexes = ChooseFrameIndexes(sourceFrameCount, outputFrameCount);
        int framesPerSecond = EstimateFramesPerSecond(decoder.Frames, frameIndexes);
        int grid = SpriteSheetFrameExtractor.CalculateGrid(outputFrameCount);
        int cellSize = canvasSize / grid;

        DrawingVisual visual = new();
        using (DrawingContext context = visual.RenderOpen())
        {
            context.DrawRectangle(Brushes.Transparent, null, new Rect(0, 0, canvasSize, canvasSize));
            for (int i = 0; i < frameIndexes.Length; i++)
            {
                BitmapFrame frame = decoder.Frames[frameIndexes[i]];
                Rect destination = GetCenteredDestination(frame.PixelWidth, frame.PixelHeight, i, grid, cellSize);
                context.DrawImage(frame, destination);
            }
        }

        RenderTargetBitmap render = new(canvasSize, canvasSize, 96, 96, PixelFormats.Pbgra32);
        render.Render(visual);
        PngBitmapEncoder encoder = new();
        encoder.Frames.Add(BitmapFrame.Create(render));
        using MemoryStream stream = new();
        encoder.Save(stream);
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

    private static Rect GetCenteredDestination(int width, int height, int frameIndex, int grid, int cellSize)
    {
        int column = frameIndex % grid;
        int row = frameIndex / grid;
        double scale = Math.Min(cellSize / (double)Math.Max(1, width), cellSize / (double)Math.Max(1, height));
        double scaledWidth = Math.Max(1, Math.Round(width * scale));
        double scaledHeight = Math.Max(1, Math.Round(height * scale));
        double x = column * cellSize + (cellSize - scaledWidth) / 2;
        double y = row * cellSize + (cellSize - scaledHeight) / 2;
        return new Rect(x, y, scaledWidth, scaledHeight);
    }

    private static int EstimateFramesPerSecond(IReadOnlyList<BitmapFrame> frames, IReadOnlyList<int> selectedFrames)
    {
        double averageDelay = selectedFrames
            .Select(index => index < frames.Count ? ReadFrameDelay(frames[index]) : 0)
            .Where(delay => delay > 0)
            .DefaultIfEmpty(4)
            .Average();

        int fps = (int)Math.Round(100.0 / averageDelay);
        return Math.Clamp(fps, 1, MaxFramesPerSecond);
    }

    private static int ReadFrameDelay(BitmapFrame frame)
    {
        if (frame.Metadata is not BitmapMetadata metadata)
        {
            return 0;
        }

        try
        {
            object value = metadata.GetQuery("/grctlext/Delay");
            return value switch
            {
                byte b => b,
                ushort u => u,
                short s => s,
                int i => i,
                _ => 0
            };
        }
        catch (ArgumentException)
        {
            return 0;
        }
        catch (NotSupportedException)
        {
            return 0;
        }
    }
}

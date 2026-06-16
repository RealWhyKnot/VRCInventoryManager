using System.Windows.Media.Imaging;

namespace VRCInventoryManager.Core;

public sealed record AnimatedImageFrames(IReadOnlyList<BitmapSource> Frames, IReadOnlyList<TimeSpan> Delays)
{
    public BitmapSource FirstFrame => Frames.Count > 0
        ? Frames[0]
        : throw new InvalidOperationException("Animation did not contain frames.");

    public bool HasAnimation => Frames.Count > 1;
}

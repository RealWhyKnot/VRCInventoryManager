using System.ComponentModel;
using System.Runtime.CompilerServices;
using System.Windows.Media.Imaging;
using System.Windows.Threading;
using VRCInventoryManager.Core;

namespace VRCInventoryManager;

internal sealed class RemoteInventoryViewModel(RemoteInventoryItem item) : INotifyPropertyChanged
{
    private bool thumbnailRequested;
    private BitmapSource? thumbnail;
    private AnimatedImageFrames? animation;
    private DispatcherTimer? animationTimer;
    private int animationFrameIndex;

    public event PropertyChangedEventHandler? PropertyChanged;

    public RemoteInventoryItem Item { get; } = item;

    public BitmapSource? Thumbnail
    {
        get => thumbnail;
        private set
        {
            if (!ReferenceEquals(thumbnail, value))
            {
                thumbnail = value;
                OnPropertyChanged();
            }
        }
    }

    public string DisplayType => Item.DisplayType;

    public string Summary => Item.Summary;

    public string Id => Item.Id;

    public string Status => Item.Status;

    public async Task EnsureThumbnailAsync(ImageThumbnailLoader loader)
    {
        if (thumbnailRequested || string.IsNullOrWhiteSpace(Item.PreviewUrl))
        {
            return;
        }

        thumbnailRequested = true;
        if (SpriteSheetFrameExtractor.CanAnimate(Item.Frames, Item.FramesOverTime))
        {
            animation = await loader.LoadRemoteAnimationAsync(Item);
            if (animation is not null)
            {
                Thumbnail = animation.FirstFrame;
                return;
            }
        }

        Thumbnail = await loader.LoadRemoteAsync(Item.PreviewUrl);
    }

    public void StartThumbnailAnimation()
    {
        if (animation is not { HasAnimation: true })
        {
            return;
        }

        animationTimer ??= new DispatcherTimer();
        animationTimer.Tick -= AnimationTimer_Tick;
        animationTimer.Tick += AnimationTimer_Tick;
        animationFrameIndex = 0;
        Thumbnail = animation.FirstFrame;
        animationTimer.Interval = animation.Delays[0];
        animationTimer.Start();
    }

    public void StopThumbnailAnimation()
    {
        animationTimer?.Stop();
        animationFrameIndex = 0;
        if (animation is not null)
        {
            Thumbnail = animation.FirstFrame;
        }
    }

    private void AnimationTimer_Tick(object? sender, EventArgs e)
    {
        if (animation is not { HasAnimation: true })
        {
            animationTimer?.Stop();
            return;
        }

        animationFrameIndex = (animationFrameIndex + 1) % animation.Frames.Count;
        Thumbnail = animation.Frames[animationFrameIndex];
        animationTimer!.Interval = animation.Delays[Math.Min(animationFrameIndex, animation.Delays.Count - 1)];
    }

    private void OnPropertyChanged([CallerMemberName] string? propertyName = null) =>
        PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(propertyName));
}

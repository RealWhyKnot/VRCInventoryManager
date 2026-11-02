using System.ComponentModel;
using System.Runtime.CompilerServices;
using System.Windows.Media.Imaging;
using System.Windows.Threading;
using VRCInventoryManager.Core;

namespace VRCInventoryManager;

internal sealed class LocalAssetViewModel : INotifyPropertyChanged
{
    private bool _isThumbnailRequested;
    private AnimatedImageFrames? _animation;
    private DispatcherTimer? _animationTimer;
    private int _animationFrameIndex;

    public event PropertyChangedEventHandler? PropertyChanged;

    public LocalAssetViewModel(LocalAsset asset)
    {
        Asset = asset;
    }

    public LocalAsset Asset { get; }

    public BitmapSource? Thumbnail
    {
        get;
        private set
        {
            if (!ReferenceEquals(field, value))
            {
                field = value;
                OnPropertyChanged();
            }
        }
    }

    public string Name => Asset.Name;

    public string DetailsText => Asset.DetailsText;

    public string TileMeta => $"{Asset.Extension.TrimStart('.').ToUpperInvariant()}  {Asset.SizeText}";

    public string FolderName => Asset.Bucket;

    public async Task EnsureThumbnailAsync(ImageThumbnailLoader loader, CancellationToken cancellationToken = default)
    {
        if (_isThumbnailRequested)
        {
            return;
        }

        _isThumbnailRequested = true;
        if (Asset.IsGif || Asset.HasSpriteSheetAnimation)
        {
            _animation = await loader.LoadLocalAnimationAsync(Asset, cancellationToken);
            if (_animation is not null)
            {
                Thumbnail = _animation.FirstFrame;
                return;
            }
        }

        Thumbnail = await loader.LoadLocalAsync(Asset, cancellationToken);
    }

    public void StartThumbnailAnimation()
    {
        if (_animation is not { HasAnimation: true })
        {
            return;
        }

        _animationTimer ??= new DispatcherTimer();
        _animationTimer.Tick -= AnimationTimer_Tick;
        _animationTimer.Tick += AnimationTimer_Tick;
        _animationFrameIndex = 0;
        Thumbnail = _animation.FirstFrame;
        _animationTimer.Interval = _animation.Delays[0];
        _animationTimer.Start();
    }

    public void StopThumbnailAnimation()
    {
        _animationTimer?.Stop();
        _animationFrameIndex = 0;
        if (_animation is not null)
        {
            Thumbnail = _animation.FirstFrame;
        }
    }

    private void AnimationTimer_Tick(object? sender, EventArgs e)
    {
        if (_animation is not { HasAnimation: true })
        {
            _animationTimer?.Stop();
            return;
        }

        _animationFrameIndex = (_animationFrameIndex + 1) % _animation.Frames.Count;
        Thumbnail = _animation.Frames[_animationFrameIndex];
        _animationTimer!.Interval = _animation.Delays[Math.Min(_animationFrameIndex, _animation.Delays.Count - 1)];
    }

    private void OnPropertyChanged([CallerMemberName] string? propertyName = null) =>
        PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(propertyName));
}

using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using System.Windows.Threading;
using VRCInventoryManager.Core;

namespace VRCInventoryManager;

public partial class MainWindow
{
    private readonly DispatcherTimer previewTimer = new();
    private readonly DispatcherTimer previewSelectionTimer = new() { Interval = TimeSpan.FromMilliseconds(120) };

    private List<BitmapSource> previewFrames = [];
    private List<TimeSpan> previewFrameDelays = [];
    private CancellationTokenSource? previewLoadCts;
    private LocalAsset? pendingPreviewAsset;
    private int previewFrameIndex;

    private void LoadPreview(LocalAsset? asset)
    {
        pendingPreviewAsset = asset;
        previewSelectionTimer.Stop();
        CancelPreviewLoad();
        if (asset is null)
        {
            ClearPreview();
            return;
        }

        StopPreviewAnimation();
        SelectedFileTitle.Text = asset.Name;
        SelectedFileText.Text = $"{asset.SizeText}  style {asset.AnimationStyle}";
        PreviewImage.Source = null;
        PreviewImage.Visibility = Visibility.Collapsed;
        PreviewEmptyText.Text = "Loading preview...";
        PreviewEmptyText.Visibility = Visibility.Visible;
        previewSelectionTimer.Start();
    }

    private async void PreviewSelectionTimer_Tick(object? sender, EventArgs e)
    {
        previewSelectionTimer.Stop();
        LocalAsset? asset = pendingPreviewAsset;
        if (asset is null)
        {
            return;
        }

        await LoadPreviewAsync(asset);
    }

    private async Task LoadPreviewAsync(LocalAsset asset)
    {
        CancellationTokenSource cts = new();
        CancellationToken cancellationToken = cts.Token;
        previewLoadCts = cts;
        try
        {
            int decodeWidth = GetPreviewDecodeWidth();
            if (asset.IsGif)
            {
                AnimatedPreview preview = await Task.Run(
                    () => LoadAnimatedPreview(asset.Path, decodeWidth, cancellationToken),
                    cancellationToken);
                if (!IsCurrentPreview(asset, cancellationToken))
                {
                    return;
                }

                ApplyAnimatedPreview(preview);
            }
            else
            {
                BitmapImage image = await Task.Run(
                    () => LoadBitmap(asset.Path, decodeWidth, cancellationToken),
                    cancellationToken);
                if (!IsCurrentPreview(asset, cancellationToken))
                {
                    return;
                }

                PreviewImage.Source = image;
                PreviewImage.Visibility = Visibility.Visible;
                PreviewEmptyText.Visibility = Visibility.Collapsed;
            }
        }
        catch (OperationCanceledException)
        {
        }
        catch (Exception ex)
        {
            if (IsCurrentPreview(asset, cancellationToken))
            {
                StopPreviewAnimation();
                PreviewImage.Source = null;
                PreviewImage.Visibility = Visibility.Collapsed;
                PreviewEmptyText.Text = "Preview failed.";
                PreviewEmptyText.Visibility = Visibility.Visible;
                SelectedFileTitle.Text = asset.Name;
                SelectedFileText.Text = "Preview failed.";
                App.Log.Error($"Preview failed for '{asset.Path}'.", ex);
            }
        }
        finally
        {
            if (ReferenceEquals(previewLoadCts, cts))
            {
                previewLoadCts = null;
                cts.Dispose();
            }
        }
    }

    private void LocalAssetList_SelectionChanged(object sender, SelectionChangedEventArgs e)
    {
        LoadPreview((LocalAssetList.SelectedItem as LocalAssetViewModel)?.Asset);
        UpdateButtons();
    }

    private async void LocalThumbnail_Loaded(object sender, RoutedEventArgs e)
    {
        if (sender is FrameworkElement { DataContext: LocalAssetViewModel asset })
        {
            await asset.EnsureThumbnailAsync(thumbnailLoader);
        }
    }

    private async void LocalThumbnail_DataContextChanged(object sender, DependencyPropertyChangedEventArgs e)
    {
        if (e.NewValue is LocalAssetViewModel asset)
        {
            await asset.EnsureThumbnailAsync(thumbnailLoader);
        }
    }

    private async void RemoteThumbnail_Loaded(object sender, RoutedEventArgs e)
    {
        if (sender is FrameworkElement { DataContext: RemoteInventoryViewModel item })
        {
            await item.EnsureThumbnailAsync(thumbnailLoader);
        }
    }

    private async void RemoteThumbnail_DataContextChanged(object sender, DependencyPropertyChangedEventArgs e)
    {
        if (e.NewValue is RemoteInventoryViewModel item)
        {
            await item.EnsureThumbnailAsync(thumbnailLoader);
        }
    }

    private void ClearPreview()
    {
        pendingPreviewAsset = null;
        previewSelectionTimer.Stop();
        CancelPreviewLoad();
        StopPreviewAnimation();
        PreviewImage.Source = null;
        PreviewImage.Visibility = Visibility.Collapsed;
        PreviewEmptyText.Text = "Select a local image.";
        PreviewEmptyText.Visibility = Visibility.Visible;
        SelectedFileTitle.Text = "Preview";
        SelectedFileText.Text = string.Empty;
    }

    private void StopPreviewAnimation()
    {
        previewTimer.Stop();
        previewFrames = [];
        previewFrameDelays = [];
        previewFrameIndex = 0;
    }

    private void CancelPreviewLoad()
    {
        if (previewLoadCts is null)
        {
            return;
        }

        previewLoadCts.Cancel();
        previewLoadCts.Dispose();
        previewLoadCts = null;
    }

    private AnimatedPreview LoadAnimatedPreview(string path, int decodeWidth, CancellationToken cancellationToken)
    {
        GifBitmapDecoder decoder = new(
            new Uri(path, UriKind.Absolute),
            BitmapCreateOptions.PreservePixelFormat,
            BitmapCacheOption.OnLoad);

        List<BitmapSource> frames = [];
        List<TimeSpan> delays = [];
        foreach (BitmapFrame frame in decoder.Frames)
        {
            cancellationToken.ThrowIfCancellationRequested();
            frames.Add(ResizeFrame(frame, decodeWidth));
            delays.Add(GetFrameDelay(frame));
        }

        if (frames.Count == 0)
        {
            throw new InvalidOperationException("GIF did not contain preview frames.");
        }

        return new AnimatedPreview(frames, delays);
    }

    private void ApplyAnimatedPreview(AnimatedPreview preview)
    {
        previewFrames = preview.Frames.ToList();
        previewFrameDelays = preview.Delays.ToList();
        previewFrameIndex = 0;
        PreviewImage.Source = previewFrames[0];
        PreviewImage.Visibility = Visibility.Visible;
        PreviewEmptyText.Visibility = Visibility.Collapsed;
        if (previewFrames.Count > 1)
        {
            previewTimer.Interval = previewFrameDelays[0];
            previewTimer.Start();
        }
    }

    private void PreviewTimer_Tick(object? sender, EventArgs e)
    {
        if (previewFrames.Count == 0)
        {
            previewTimer.Stop();
            return;
        }

        previewFrameIndex = (previewFrameIndex + 1) % previewFrames.Count;
        PreviewImage.Source = previewFrames[previewFrameIndex];
        previewTimer.Interval = previewFrameDelays[Math.Min(previewFrameIndex, previewFrameDelays.Count - 1)];
    }

    private BitmapImage LoadBitmap(string path, int decodeWidth, CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();
        BitmapImage image = new();
        image.BeginInit();
        image.CacheOption = BitmapCacheOption.OnLoad;
        image.CreateOptions = BitmapCreateOptions.IgnoreColorProfile;
        image.DecodePixelWidth = decodeWidth;
        image.UriSource = new Uri(path, UriKind.Absolute);
        image.EndInit();
        image.Freeze();

        return image;
    }

    private int GetPreviewDecodeWidth()
    {
        double width = Math.Max(PreviewImage.ActualWidth, 800);
        double dpiScale = VisualTreeHelper.GetDpi(PreviewImage).DpiScaleX;
        return Math.Clamp((int)Math.Ceiling(width * dpiScale), 512, 2048);
    }

    private bool IsCurrentPreview(LocalAsset asset, CancellationToken cancellationToken) =>
        !cancellationToken.IsCancellationRequested &&
        string.Equals(pendingPreviewAsset?.Path, asset.Path, StringComparison.OrdinalIgnoreCase);

    private static BitmapSource ResizeFrame(BitmapSource source, int decodeWidth)
    {
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

    private static TimeSpan GetFrameDelay(BitmapFrame frame)
    {
        const string delayQuery = "/grctlext/Delay";
        try
        {
            if (frame.Metadata is BitmapMetadata metadata && metadata.ContainsQuery(delayQuery))
            {
                int hundredths = Convert.ToInt32(metadata.GetQuery(delayQuery));
                if (hundredths > 1)
                {
                    return TimeSpan.FromMilliseconds(hundredths * 10);
                }
            }
        }
        catch (InvalidOperationException)
        {
        }
        catch (NotSupportedException)
        {
        }

        return TimeSpan.FromMilliseconds(100);
    }

    private sealed record AnimatedPreview(IReadOnlyList<BitmapSource> Frames, IReadOnlyList<TimeSpan> Delays);
}

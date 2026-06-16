using System.IO;
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
    private PreviewRequest? pendingPreviewRequest;
    private int previewFrameIndex;

    private void LoadPreview(LocalAsset? asset)
    {
        SetPreviewRequest(asset is null ? null : PreviewRequest.FromLocal(asset));
    }

    private void LoadRemotePreview(RemoteInventoryItem? item)
    {
        SetPreviewRequest(item is null ? null : PreviewRequest.FromRemote(item));
    }

    private void SetPreviewRequest(PreviewRequest? request)
    {
        pendingPreviewRequest = request;
        previewSelectionTimer.Stop();
        CancelPreviewLoad();
        if (request is null)
        {
            ClearPreview();
            return;
        }

        StopPreviewAnimation();
        SelectedFileTitle.Text = request.Title;
        SelectedFileText.Text = request.Details;
        PreviewImage.Source = null;
        PreviewImage.Visibility = Visibility.Collapsed;
        PreviewEmptyText.Text = "Loading preview...";
        PreviewEmptyText.Visibility = Visibility.Visible;
        previewSelectionTimer.Start();
    }

    private async void PreviewSelectionTimer_Tick(object? sender, EventArgs e)
    {
        previewSelectionTimer.Stop();
        PreviewRequest? request = pendingPreviewRequest;
        if (request is null)
        {
            return;
        }

        await LoadPreviewAsync(request);
    }

    private async Task LoadPreviewAsync(PreviewRequest request)
    {
        CancellationTokenSource cts = new();
        CancellationToken cancellationToken = cts.Token;
        previewLoadCts = cts;
        try
        {
            int decodeWidth = GetPreviewDecodeWidth();
            if (request.LocalAsset is { } asset)
            {
                if (asset.IsGif)
                {
                    AnimatedImageFrames preview = await Task.Run(
                        () => LoadAnimatedGifPreview(asset.Path, decodeWidth, cancellationToken),
                        cancellationToken);
                    if (!IsCurrentPreview(request, cancellationToken))
                    {
                        return;
                    }

                    ApplyAnimatedPreview(preview);
                }
                else if (asset.HasSpriteSheetAnimation)
                {
                    AnimatedImageFrames preview = await Task.Run(
                        () => LoadLocalSpriteSheetPreview(asset, decodeWidth, cancellationToken),
                        cancellationToken);
                    if (!IsCurrentPreview(request, cancellationToken))
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
                    if (!IsCurrentPreview(request, cancellationToken))
                    {
                        return;
                    }

                    ApplyStaticPreview(image);
                }
            }
            else if (request.RemoteItem is { } remoteItem)
            {
                if (string.IsNullOrWhiteSpace(remoteItem.PreviewUrl))
                {
                    throw new InvalidOperationException("Remote file does not have a preview URL.");
                }

                byte[] bytes = await thumbnailLoader.DownloadRemoteBytesAsync(remoteItem.PreviewUrl, cancellationToken);
                PreviewRender render = await Task.Run(
                    () => LoadRemotePreviewRender(bytes, remoteItem, decodeWidth, cancellationToken),
                    cancellationToken);
                if (!IsCurrentPreview(request, cancellationToken))
                {
                    return;
                }

                if (render.Animation is not null)
                {
                    ApplyAnimatedPreview(render.Animation);
                }
                else
                {
                    ApplyStaticPreview(render.Image!);
                }
            }
        }
        catch (OperationCanceledException)
        {
        }
        catch (Exception ex)
        {
            if (IsCurrentPreview(request, cancellationToken))
            {
                StopPreviewAnimation();
                PreviewImage.Source = null;
                PreviewImage.Visibility = Visibility.Collapsed;
                PreviewEmptyText.Text = "Preview failed.";
                PreviewEmptyText.Visibility = Visibility.Visible;
                SelectedFileTitle.Text = request.Title;
                SelectedFileText.Text = "Preview failed.";
                App.Log.Error($"Preview failed for '{request.Key}'.", ex);
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
        if (suppressLocalSelectionChanged)
        {
            return;
        }

        if (LocalAssetList.SelectedItem is LocalAssetViewModel)
        {
            suppressRemoteSelectionChanged = true;
            RemoteFileList.SelectedItem = null;
            suppressRemoteSelectionChanged = false;
            RemoteSelectedText.Text = string.Empty;
        }

        LoadPreview((LocalAssetList.SelectedItem as LocalAssetViewModel)?.Asset);
        UpdateButtons();
    }

    private async void LocalThumbnail_Loaded(object sender, RoutedEventArgs e)
    {
        if (sender is FrameworkElement { DataContext: LocalAssetViewModel asset })
        {
            await asset.EnsureThumbnailAsync(thumbnailLoader);
            asset.StartThumbnailAnimation();
        }
    }

    private void LocalThumbnail_Unloaded(object sender, RoutedEventArgs e)
    {
        if (sender is FrameworkElement { DataContext: LocalAssetViewModel asset })
        {
            asset.StopThumbnailAnimation();
        }
    }

    private async void LocalThumbnail_DataContextChanged(object sender, DependencyPropertyChangedEventArgs e)
    {
        if (e.OldValue is LocalAssetViewModel oldAsset)
        {
            oldAsset.StopThumbnailAnimation();
        }

        if (e.NewValue is LocalAssetViewModel asset)
        {
            await asset.EnsureThumbnailAsync(thumbnailLoader);
            asset.StartThumbnailAnimation();
        }
    }

    private async void RemoteThumbnail_Loaded(object sender, RoutedEventArgs e)
    {
        if (sender is FrameworkElement { DataContext: RemoteInventoryViewModel item })
        {
            await item.EnsureThumbnailAsync(thumbnailLoader);
            item.StartThumbnailAnimation();
        }
    }

    private void RemoteThumbnail_Unloaded(object sender, RoutedEventArgs e)
    {
        if (sender is FrameworkElement { DataContext: RemoteInventoryViewModel item })
        {
            item.StopThumbnailAnimation();
        }
    }

    private async void RemoteThumbnail_DataContextChanged(object sender, DependencyPropertyChangedEventArgs e)
    {
        if (e.OldValue is RemoteInventoryViewModel oldItem)
        {
            oldItem.StopThumbnailAnimation();
        }

        if (e.NewValue is RemoteInventoryViewModel item)
        {
            await item.EnsureThumbnailAsync(thumbnailLoader);
            item.StartThumbnailAnimation();
        }
    }

    private void ClearPreview()
    {
        pendingPreviewRequest = null;
        previewSelectionTimer.Stop();
        CancelPreviewLoad();
        StopPreviewAnimation();
        PreviewImage.Source = null;
        PreviewImage.Visibility = Visibility.Collapsed;
        PreviewEmptyText.Text = "Select a local or VRChat inventory item.";
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

    private AnimatedImageFrames LoadAnimatedGifPreview(string path, int decodeWidth, CancellationToken cancellationToken)
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

        return new AnimatedImageFrames(frames, delays);
    }

    private AnimatedImageFrames LoadAnimatedGifPreview(byte[] bytes, int decodeWidth, CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();
        using MemoryStream stream = new(bytes);
        GifBitmapDecoder decoder = new(
            stream,
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

        return new AnimatedImageFrames(frames, delays);
    }

    private PreviewRender LoadRemotePreviewRender(
        byte[] bytes,
        RemoteInventoryItem item,
        int decodeWidth,
        CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();
        if (LooksLikeGif(bytes, item))
        {
            return PreviewRender.FromAnimation(LoadAnimatedGifPreview(bytes, decodeWidth, cancellationToken));
        }

        if (SpriteSheetFrameExtractor.CanAnimate(item.Frames, item.FramesOverTime))
        {
            try
            {
                int grid = SpriteSheetFrameExtractor.CalculateGrid(item.Frames!.Value);
                int sheetDecodeWidth = Math.Clamp(decodeWidth * grid, 512, 2048);
                BitmapImage spriteSheet = LoadBitmap(bytes, sheetDecodeWidth, cancellationToken);
                AnimatedImageFrames frames = SpriteSheetFrameExtractor.Extract(
                    spriteSheet,
                    item.Frames.Value,
                    item.FramesOverTime!.Value,
                    decodeWidth);
                return PreviewRender.FromAnimation(frames);
            }
            catch (Exception ex) when (ex is not OperationCanceledException)
            {
                App.Log.Warning($"Animated remote preview fell back to static for '{item.Id}': {ex.Message}");
            }
        }

        return PreviewRender.FromImage(LoadBitmap(bytes, decodeWidth, cancellationToken));
    }

    private AnimatedImageFrames LoadLocalSpriteSheetPreview(
        LocalAsset asset,
        int decodeWidth,
        CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();
        int frames = asset.Frames.GetValueOrDefault();
        int framesOverTime = asset.FramesOverTime.GetValueOrDefault();
        if (!SpriteSheetFrameExtractor.CanAnimate(frames, framesOverTime))
        {
            throw new InvalidOperationException("Local sprite sheet is missing animation metadata.");
        }

        int grid = SpriteSheetFrameExtractor.CalculateGrid(frames);
        int sheetDecodeWidth = Math.Clamp(decodeWidth * grid, 512, 2048);
        BitmapImage spriteSheet = LoadBitmap(asset.Path, sheetDecodeWidth, cancellationToken);
        return SpriteSheetFrameExtractor.Extract(spriteSheet, frames, framesOverTime, decodeWidth);
    }

    private void ApplyAnimatedPreview(AnimatedImageFrames preview)
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

    private void ApplyStaticPreview(BitmapSource image)
    {
        StopPreviewAnimation();
        PreviewImage.Source = image;
        PreviewImage.Visibility = Visibility.Visible;
        PreviewEmptyText.Visibility = Visibility.Collapsed;
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

    private BitmapImage LoadBitmap(byte[] bytes, int decodeWidth, CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();
        using MemoryStream stream = new(bytes);
        BitmapImage image = new();
        image.BeginInit();
        image.CacheOption = BitmapCacheOption.OnLoad;
        image.CreateOptions = BitmapCreateOptions.IgnoreColorProfile;
        image.DecodePixelWidth = decodeWidth;
        image.StreamSource = stream;
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

    private bool IsCurrentPreview(PreviewRequest request, CancellationToken cancellationToken) =>
        !cancellationToken.IsCancellationRequested &&
        string.Equals(pendingPreviewRequest?.Key, request.Key, StringComparison.OrdinalIgnoreCase);

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

    private static bool LooksLikeGif(byte[] bytes, RemoteInventoryItem item) =>
        item.MimeType.Contains("gif", StringComparison.OrdinalIgnoreCase) ||
        bytes is [0x47, 0x49, 0x46, ..];

    private sealed record PreviewRequest(
        string Key,
        string Title,
        string Details,
        LocalAsset? LocalAsset,
        RemoteInventoryItem? RemoteItem)
    {
        public static PreviewRequest FromLocal(LocalAsset asset) =>
            new($"local:{asset.Path}", asset.Name, $"{asset.SizeText}  style {asset.AnimationStyle}", asset, null);

        public static PreviewRequest FromRemote(RemoteInventoryItem item)
        {
            string title = string.IsNullOrWhiteSpace(item.Name) ? item.DisplayType : item.Name;
            string status = string.IsNullOrWhiteSpace(item.Status) ? item.Summary : $"{item.Summary}  {item.Status}";
            return new($"remote:{item.Id}:{item.PreviewUrl}", title, status, null, item);
        }
    }

    private sealed record PreviewRender(BitmapSource? Image, AnimatedImageFrames? Animation)
    {
        public static PreviewRender FromImage(BitmapSource image) => new(image, null);

        public static PreviewRender FromAnimation(AnimatedImageFrames animation) => new(null, animation);
    }
}

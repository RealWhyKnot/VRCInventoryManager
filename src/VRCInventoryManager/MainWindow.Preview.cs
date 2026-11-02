using System.IO;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using System.Windows.Threading;
using VRCInventoryManager.Core;

namespace VRCInventoryManager;

internal sealed partial class MainWindow
{
    private readonly DispatcherTimer _previewTimer = new();
    private readonly DispatcherTimer _previewSelectionTimer = new() { Interval = TimeSpan.FromMilliseconds(120) };

    private List<BitmapSource> _previewFrames = [];
    private List<TimeSpan> _previewFrameDelays = [];
    private CancellationTokenSource? _previewCancellationSource;
    private PreviewRequest? _pendingPreviewRequest;
    private int _previewFrameIndex;

    private void LoadPreview(LocalAsset? asset) => SetPreviewRequest(asset is null ? null : PreviewRequest.FromLocal(asset));

    private void LoadRemotePreview(RemoteInventoryItem? item) => SetPreviewRequest(item is null ? null : PreviewRequest.FromRemote(item));

    private void SetPreviewRequest(PreviewRequest? request)
    {
        _pendingPreviewRequest = request;
        _previewSelectionTimer.Stop();
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
        _previewSelectionTimer.Start();
    }

    private async void PreviewSelectionTimer_Tick(object? sender, EventArgs e)
    {
        _previewSelectionTimer.Stop();
        PreviewRequest? request = _pendingPreviewRequest;
        if (request is null)
        {
            return;
        }

        await LoadPreviewAsync(request, CancellationToken.None);
    }

    private async Task LoadPreviewAsync(PreviewRequest request, CancellationToken cancellationToken = default)
    {
        var previewCancellationSource = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
        cancellationToken = previewCancellationSource.Token;
        _previewCancellationSource = previewCancellationSource;
        try
        {
            PreviewRender? render = await CreatePreviewRenderAsync(request, cancellationToken);
            if (render is null || !IsCurrentPreview(request, cancellationToken))
            {
                return;
            }

            ApplyPreviewRender(render);
        }
        catch (OperationCanceledException)
        {
            return;
        }
        catch (IOException exception)
        {
            HandleLoadPreviewFailure(exception, request, cancellationToken);
        }
        catch (UnauthorizedAccessException exception)
        {
            HandleLoadPreviewFailure(exception, request, cancellationToken);
        }
        catch (InvalidOperationException exception)
        {
            HandleLoadPreviewFailure(exception, request, cancellationToken);
        }
        catch (System.Net.Http.HttpRequestException exception)
        {
            HandleLoadPreviewFailure(exception, request, cancellationToken);
        }
        catch (NotSupportedException exception)
        {
            HandleLoadPreviewFailure(exception, request, cancellationToken);
        }
        catch (ArgumentException exception)
        {
            HandleLoadPreviewFailure(exception, request, cancellationToken);
        }
        catch (System.Runtime.InteropServices.ExternalException exception)
        {
            HandleLoadPreviewFailure(exception, request, cancellationToken);
        }
        catch (OutOfMemoryException exception)
        {
            HandleLoadPreviewFailure(exception, request, cancellationToken);
        }
        finally
        {
            if (ReferenceEquals(_previewCancellationSource, previewCancellationSource))
            {
                _previewCancellationSource = null;
                previewCancellationSource.Dispose();
            }
        }
    }

    private void LocalAssetList_SelectionChanged(object sender, SelectionChangedEventArgs e)
    {
        if (_shouldSuppressLocalSelectionChanged)
        {
            return;
        }

        if (LocalAssetList.SelectedItem is LocalAssetViewModel)
        {
            _shouldSuppressRemoteSelectionChanged = true;
            RemoteFileList.SelectedItem = null;
            _shouldSuppressRemoteSelectionChanged = false;
            RemoteSelectedText.Text = string.Empty;
        }

        LoadPreview((LocalAssetList.SelectedItem as LocalAssetViewModel)?.Asset);
        UpdateButtons();
    }

    private async void LocalThumbnail_Loaded(object sender, RoutedEventArgs e)
    {
        if (sender is FrameworkElement { DataContext: LocalAssetViewModel asset })
        {
            await asset.EnsureThumbnailAsync(_thumbnailLoader, CancellationToken.None);
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

    private void LocalThumbnail_DataContextChanged(object sender, DependencyPropertyChangedEventArgs e) =>
        _ = LocalThumbnailChangedAsync(e, CancellationToken.None);

    private async Task LocalThumbnailChangedAsync(DependencyPropertyChangedEventArgs e, CancellationToken cancellationToken)
    {
        if (e.OldValue is LocalAssetViewModel oldAsset)
        {
            oldAsset.StopThumbnailAnimation();
        }

        if (e.NewValue is LocalAssetViewModel asset)
        {
            await asset.EnsureThumbnailAsync(_thumbnailLoader, cancellationToken);
            asset.StartThumbnailAnimation();
        }
    }

    private async void RemoteThumbnail_Loaded(object sender, RoutedEventArgs e)
    {
        if (sender is FrameworkElement { DataContext: RemoteInventoryViewModel item })
        {
            await item.EnsureThumbnailAsync(_thumbnailLoader, CancellationToken.None);
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

    private void RemoteThumbnail_DataContextChanged(object sender, DependencyPropertyChangedEventArgs e) =>
        _ = RemoteThumbnailChangedAsync(e, CancellationToken.None);

    private async Task RemoteThumbnailChangedAsync(DependencyPropertyChangedEventArgs e, CancellationToken cancellationToken)
    {
        if (e.OldValue is RemoteInventoryViewModel oldItem)
        {
            oldItem.StopThumbnailAnimation();
        }

        if (e.NewValue is RemoteInventoryViewModel item)
        {
            await item.EnsureThumbnailAsync(_thumbnailLoader, cancellationToken);
            item.StartThumbnailAnimation();
        }
    }

    private void ApplyPreviewRender(PreviewRender render)
    {
        if (render.Animation is not null)
        {
            ApplyAnimatedPreview(render.Animation);
        }
        else
        {
            ApplyStaticPreview(render.Image!);
        }
    }

    private async Task<PreviewRender?> CreatePreviewRenderAsync(PreviewRequest request, CancellationToken cancellationToken)
    {
        int decodeWidth = GetPreviewDecodeWidth();
        if (request.LocalAsset is { } asset)
        {
            return await Task.Run(() => CreateLocalPreviewRender(asset, decodeWidth, cancellationToken), cancellationToken);
        }

        if (request.RemoteItem is not { } item)
        {
            return null;
        }

        if (string.IsNullOrWhiteSpace(item.PreviewUrl))
        {
            throw new InvalidOperationException("Remote file does not have a preview URL.");
        }

        byte[] bytes = await _thumbnailLoader.DownloadRemoteBytesAsync(item.PreviewUrl, cancellationToken);
        return await Task.Run(() => LoadRemotePreviewRender(bytes, item, decodeWidth, cancellationToken), cancellationToken);
    }

    private static PreviewRender CreateLocalPreviewRender(LocalAsset asset, int decodeWidth, CancellationToken cancellationToken)
    {
        return asset.IsGif
            ? PreviewRender.FromAnimation(LoadAnimatedGifPreview(asset.Path, decodeWidth, cancellationToken))
            : asset.HasSpriteSheetAnimation
            ? PreviewRender.FromAnimation(LoadLocalSpriteSheetPreview(asset, decodeWidth, cancellationToken))
            : PreviewRender.FromImage(LoadBitmap(asset.Path, decodeWidth, cancellationToken));
    }

    private void ClearPreview()
    {
        _pendingPreviewRequest = null;
        _previewSelectionTimer.Stop();
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
        _previewTimer.Stop();
        _previewFrames = [];
        _previewFrameDelays = [];
        _previewFrameIndex = 0;
    }

    private void CancelPreviewLoad()
    {
        if (_previewCancellationSource is null)
        {
            return;
        }

        _previewCancellationSource.Cancel();
        _previewCancellationSource.Dispose();
        _previewCancellationSource = null;
    }

    private static AnimatedImageFrames LoadAnimatedGifPreview(string path, int decodeWidth, CancellationToken cancellationToken)
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

        return frames.Count == 0
            ? throw new InvalidOperationException("GIF did not contain preview frames.")
            : new AnimatedImageFrames(frames, delays);
    }

    private static AnimatedImageFrames LoadAnimatedGifPreview(byte[] bytes, int decodeWidth, CancellationToken cancellationToken)
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

        return frames.Count == 0
            ? throw new InvalidOperationException("GIF did not contain preview frames.")
            : new AnimatedImageFrames(frames, delays);
    }

    private static void LogRemotePreviewFallback(RemoteInventoryItem item, Exception exception) =>
        App.Log.Warning($"Animated remote preview fell back to static for '{item.Id}': {exception.Message}");

    private static PreviewRender LoadRemotePreviewRender(
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
            catch (IOException exception)
            {
                LogRemotePreviewFallback(item, exception);
            }
            catch (NotSupportedException exception)
            {
                LogRemotePreviewFallback(item, exception);
            }
            catch (InvalidOperationException exception)
            {
                LogRemotePreviewFallback(item, exception);
            }
            catch (ArgumentException exception)
            {
                LogRemotePreviewFallback(item, exception);
            }
            catch (System.Runtime.InteropServices.ExternalException exception)
            {
                LogRemotePreviewFallback(item, exception);
            }
        }

        return PreviewRender.FromImage(LoadBitmap(bytes, decodeWidth, cancellationToken));
    }

    private static AnimatedImageFrames LoadLocalSpriteSheetPreview(
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
        _previewFrames = [.. preview.Frames];
        _previewFrameDelays = [.. preview.Delays];
        _previewFrameIndex = 0;
        PreviewImage.Source = _previewFrames[0];
        PreviewImage.Visibility = Visibility.Visible;
        PreviewEmptyText.Visibility = Visibility.Collapsed;
        if (_previewFrames.Count > 1)
        {
            _previewTimer.Interval = _previewFrameDelays[0];
            _previewTimer.Start();
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
        if (_previewFrames.Count == 0)
        {
            _previewTimer.Stop();
            return;
        }

        _previewFrameIndex = (_previewFrameIndex + 1) % _previewFrames.Count;
        PreviewImage.Source = _previewFrames[_previewFrameIndex];
        _previewTimer.Interval = _previewFrameDelays[Math.Min(_previewFrameIndex, _previewFrameDelays.Count - 1)];
    }

    private static BitmapImage LoadBitmap(string path, int decodeWidth, CancellationToken cancellationToken)
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

    private static BitmapImage LoadBitmap(byte[] bytes, int decodeWidth, CancellationToken cancellationToken)
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
        string.Equals(_pendingPreviewRequest?.Key, request.Key, StringComparison.OrdinalIgnoreCase);

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
        const string DelayQuery = "/grctlext/Delay";
        try
        {
            if (frame.Metadata is BitmapMetadata metadata && metadata.ContainsQuery(DelayQuery))
            {
                int hundredths = Convert.ToInt32(metadata.GetQuery(DelayQuery), System.Globalization.CultureInfo.InvariantCulture);
                if (hundredths > 1)
                {
                    return TimeSpan.FromMilliseconds(hundredths * 10);
                }
            }
        }
        catch (InvalidOperationException)
        {
            return TimeSpan.FromMilliseconds(100);
        }
        catch (NotSupportedException)
        {
            return TimeSpan.FromMilliseconds(100);
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
            new($"local:{asset.Path}", asset.Name, $"{asset.SizeText}  style {asset.AnimationStyle}", asset, RemoteItem: null);

        public static PreviewRequest FromRemote(RemoteInventoryItem item)
        {
            string title = string.IsNullOrWhiteSpace(item.Name) ? item.DisplayType : item.Name;
            string status = string.IsNullOrWhiteSpace(item.Status) ? item.Summary : $"{item.Summary}  {item.Status}";
            return new($"remote:{item.Id}:{item.PreviewUrl}", title, status, LocalAsset: null, item);
        }
    }

    private sealed record PreviewRender(BitmapSource? Image, AnimatedImageFrames? Animation)
    {
        public static PreviewRender FromImage(BitmapSource image) => new(image, Animation: null);

        public static PreviewRender FromAnimation(AnimatedImageFrames animation) => new(Image: null, animation);
    }
}

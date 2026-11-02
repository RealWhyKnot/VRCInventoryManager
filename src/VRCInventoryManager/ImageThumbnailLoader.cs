using System.IO;
using System.Net.Http;
using System.Windows.Media.Imaging;
using VRCInventoryManager.Core;

namespace VRCInventoryManager;

internal sealed partial class ImageThumbnailLoader : IDisposable
{
    private const int ThumbnailWidth = 120;
    private const int MaxLocalCacheEntries = 512;
    private const int MaxRemoteCacheEntries = 128;
    private readonly Dictionary<string, ThumbnailCacheEntry<BitmapSource?>> _localCache = new(StringComparer.OrdinalIgnoreCase);
    private readonly Dictionary<string, ThumbnailCacheEntry<AnimatedImageFrames?>> _localAnimationCache = new(StringComparer.OrdinalIgnoreCase);
    private readonly Dictionary<string, ThumbnailCacheEntry<BitmapSource?>> _remoteCache = new(StringComparer.OrdinalIgnoreCase);
    private readonly Dictionary<string, ThumbnailCacheEntry<AnimatedImageFrames?>> _remoteAnimationCache = new(StringComparer.OrdinalIgnoreCase);
    private readonly object _localCacheGate = new();
    private readonly object _remoteCacheGate = new();
    private readonly SemaphoreSlim _localGate = new(2);
    private readonly SemaphoreSlim _remoteGate = new(4);
    private readonly HttpClient _httpClient;
    private readonly Lock _operationGate = new();
    private int _activeOperations;
    private bool _isDisposed;
    private readonly Lock _authGate = new();
    private VrcxAuthCookies? _remoteCookies;
    private string _remoteUserAgent = "VRCInventoryManager";
    private long _cacheStamp;

    public ImageThumbnailLoader(HttpMessageHandler? handler = null)
    {
        _httpClient = handler is null ? new HttpClient() : new HttpClient(handler);
    }

    public Task<BitmapSource?> LoadLocalAsync(LocalAsset asset, CancellationToken cancellationToken = default) =>
        GetOrAddCacheAsync(_localCache, _localCacheGate, asset.Path, () => RunOperationAsync(() => LoadLocalCoreAsync(asset, CancellationToken.None), CancellationToken.None), MaxLocalCacheEntries, cancellationToken);

    public Task<AnimatedImageFrames?> LoadLocalAnimationAsync(LocalAsset asset, CancellationToken cancellationToken = default) =>
        GetOrAddCacheAsync(_localAnimationCache, _localCacheGate, asset.Path, () => RunOperationAsync(() => LoadLocalAnimationCoreAsync(asset, CancellationToken.None), CancellationToken.None), MaxLocalCacheEntries, cancellationToken);

    public Task<BitmapSource?> LoadRemoteAsync(string url, CancellationToken cancellationToken = default) =>
        GetOrAddCacheAsync(_remoteCache, _remoteCacheGate, url, () => RunOperationAsync(() => LoadRemoteCoreAsync(url, CancellationToken.None), CancellationToken.None), MaxRemoteCacheEntries, cancellationToken);

    public Task<AnimatedImageFrames?> LoadRemoteAnimationAsync(RemoteInventoryItem item, CancellationToken cancellationToken = default) =>
        GetOrAddCacheAsync(
            _remoteAnimationCache,
            _remoteCacheGate,
            $"{item.PreviewUrl}|{item.Frames}|{item.FramesOverTime}",
            () => RunOperationAsync(() => LoadRemoteAnimationCoreAsync(item, CancellationToken.None), CancellationToken.None),
            MaxRemoteCacheEntries, cancellationToken);

    public Task<byte[]> DownloadRemoteBytesAsync(string url, CancellationToken cancellationToken) =>
        RunOperationAsync(() => DownloadRemoteBytesWithGateAsync(url, cancellationToken), cancellationToken);

    private async Task<byte[]> DownloadRemoteBytesWithGateAsync(string url, CancellationToken cancellationToken)
    {
        await _remoteGate.WaitAsync(cancellationToken);
        try
        {
            return await DownloadRemoteBytesCoreAsync(url, cancellationToken);
        }
        finally
        {
            _remoteGate.Release();
        }
    }

    public void ClearLocal()
    {
        ClearCache(_localCache, _localCacheGate);
        ClearCache(_localAnimationCache, _localCacheGate);
    }

    public void ConfigureRemoteAuth(VrcxAuthCookies cookies, string userAgent)
    {
        lock (_authGate)
        {
            _remoteCookies = cookies;
            _remoteUserAgent = string.IsNullOrWhiteSpace(userAgent) ? "VRCInventoryManager" : userAgent;
        }

        ClearCache(_remoteCache, _remoteCacheGate);
        ClearCache(_remoteAnimationCache, _remoteCacheGate);
    }

    public void ClearRemoteAuth()
    {
        lock (_authGate)
        {
            _remoteCookies = null;
        }

        ClearCache(_remoteCache, _remoteCacheGate);
        ClearCache(_remoteAnimationCache, _remoteCacheGate);
    }

    private Task<T> GetOrAddCacheAsync<T>(
        Dictionary<string, ThumbnailCacheEntry<T>> cache,
        object gate,
        string key,
        Func<Task<T>> factory,
        int maxEntries, CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();
        lock (gate)
        {
            long stamp = Interlocked.Increment(ref _cacheStamp);
            if (cache.TryGetValue(key, out ThumbnailCacheEntry<T>? existing))
            {
                existing.LastAccess = stamp;
                return existing.Task.WaitAsync(cancellationToken);
            }

            Task<T> task = factory();
            cache[key] = new ThumbnailCacheEntry<T>(task, stamp);
            TrimCache(cache, maxEntries);
            return task.WaitAsync(cancellationToken);
        }
    }

    private static void ClearCache<T>(Dictionary<string, ThumbnailCacheEntry<T>> cache, object gate)
    {
        lock (gate)
        {
            cache.Clear();
        }
    }

    private static void TrimCache<T>(Dictionary<string, ThumbnailCacheEntry<T>> cache, int maxEntries)
    {
        int removeCount = cache.Count - maxEntries;
        if (removeCount <= 0)
        {
            return;
        }

        string[] keysToRemove = [.. cache
            .OrderBy(entry => entry.Value.LastAccess)
            .Take(removeCount)
            .Select(entry => entry.Key)];
        foreach (string key in keysToRemove)
        {
            cache.Remove(key);
        }
    }

    private async Task<BitmapSource?> LoadLocalCoreAsync(LocalAsset asset, CancellationToken cancellationToken = default)
    {
        await _localGate.WaitAsync(cancellationToken);
        try
        {
            return await Task.Run(() => asset.IsGif
                ? LoadGifFrame(asset.Path, ThumbnailWidth)
                : LoadBitmap(asset.Path, ThumbnailWidth), cancellationToken);
        }
        catch (IOException exception)
        {
            HandleLoadLocalCoreFailure(exception, asset); return null;
        }
        catch (UnauthorizedAccessException exception)
        {
            HandleLoadLocalCoreFailure(exception, asset); return null;
        }
        catch (InvalidOperationException exception)
        {
            HandleLoadLocalCoreFailure(exception, asset); return null;
        }
        catch (HttpRequestException exception)
        {
            HandleLoadLocalCoreFailure(exception, asset); return null;
        }
        catch (System.Text.Json.JsonException exception)
        {
            HandleLoadLocalCoreFailure(exception, asset); return null;
        }
        catch (NotSupportedException exception)
        {
            HandleLoadLocalCoreFailure(exception, asset); return null;
        }
        catch (ArgumentException exception)
        {
            HandleLoadLocalCoreFailure(exception, asset); return null;
        }
        catch (System.Runtime.InteropServices.ExternalException exception)
        {
            HandleLoadLocalCoreFailure(exception, asset); return null;
        }
        catch (OutOfMemoryException exception)
        {
            HandleLoadLocalCoreFailure(exception, asset); return null;
        }
        catch (OperationCanceledException exception)
        {
            HandleLoadLocalCoreFailure(exception, asset); return null;
        }
        finally
        {
            _localGate.Release();
        }
    }

    private async Task<AnimatedImageFrames?> LoadLocalAnimationCoreAsync(LocalAsset asset, CancellationToken cancellationToken = default)
    {
        if (!asset.IsGif && !asset.HasSpriteSheetAnimation)
        {
            return null;
        }

        await _localGate.WaitAsync(cancellationToken);
        try
        {
            return await Task.Run(() => asset.IsGif
                ? LoadGifAnimation(asset.Path, ThumbnailWidth)
                : LoadSpriteSheetAnimation(asset.Path, asset.Frames!.Value, asset.FramesOverTime!.Value, ThumbnailWidth), cancellationToken);
        }
        catch (IOException exception)
        {
            HandleLoadLocalAnimationCoreFailure(exception, asset); return null;
        }
        catch (UnauthorizedAccessException exception)
        {
            HandleLoadLocalAnimationCoreFailure(exception, asset); return null;
        }
        catch (InvalidOperationException exception)
        {
            HandleLoadLocalAnimationCoreFailure(exception, asset); return null;
        }
        catch (HttpRequestException exception)
        {
            HandleLoadLocalAnimationCoreFailure(exception, asset); return null;
        }
        catch (System.Text.Json.JsonException exception)
        {
            HandleLoadLocalAnimationCoreFailure(exception, asset); return null;
        }
        catch (NotSupportedException exception)
        {
            HandleLoadLocalAnimationCoreFailure(exception, asset); return null;
        }
        catch (ArgumentException exception)
        {
            HandleLoadLocalAnimationCoreFailure(exception, asset); return null;
        }
        catch (System.Runtime.InteropServices.ExternalException exception)
        {
            HandleLoadLocalAnimationCoreFailure(exception, asset); return null;
        }
        catch (OutOfMemoryException exception)
        {
            HandleLoadLocalAnimationCoreFailure(exception, asset); return null;
        }
        catch (OperationCanceledException exception)
        {
            HandleLoadLocalAnimationCoreFailure(exception, asset); return null;
        }
        finally
        {
            _localGate.Release();
        }
    }

    private async Task<BitmapSource?> LoadRemoteCoreAsync(string url, CancellationToken cancellationToken = default)
    {
        await _remoteGate.WaitAsync(cancellationToken);
        try
        {
            byte[] bytes = await DownloadRemoteBytesCoreAsync(url, cancellationToken);
            return await Task.Run(() => LoadBitmap(bytes, ThumbnailWidth), cancellationToken);
        }
        catch (IOException exception)
        {
            HandleLoadRemoteCoreFailure(exception, url); return null;
        }
        catch (UnauthorizedAccessException exception)
        {
            HandleLoadRemoteCoreFailure(exception, url); return null;
        }
        catch (InvalidOperationException exception)
        {
            HandleLoadRemoteCoreFailure(exception, url); return null;
        }
        catch (HttpRequestException exception)
        {
            HandleLoadRemoteCoreFailure(exception, url); return null;
        }
        catch (System.Text.Json.JsonException exception)
        {
            HandleLoadRemoteCoreFailure(exception, url); return null;
        }
        catch (NotSupportedException exception)
        {
            HandleLoadRemoteCoreFailure(exception, url); return null;
        }
        catch (ArgumentException exception)
        {
            HandleLoadRemoteCoreFailure(exception, url); return null;
        }
        catch (System.Runtime.InteropServices.ExternalException exception)
        {
            HandleLoadRemoteCoreFailure(exception, url); return null;
        }
        catch (OutOfMemoryException exception)
        {
            HandleLoadRemoteCoreFailure(exception, url); return null;
        }
        catch (OperationCanceledException exception)
        {
            HandleLoadRemoteCoreFailure(exception, url); return null;
        }
        finally
        {
            _remoteGate.Release();
        }
    }

    private async Task<AnimatedImageFrames?> LoadRemoteAnimationCoreAsync(RemoteInventoryItem item, CancellationToken cancellationToken = default)
    {
        if (!SpriteSheetFrameExtractor.CanAnimate(item.Frames, item.FramesOverTime) ||
            string.IsNullOrWhiteSpace(item.PreviewUrl))
        {
            return null;
        }

        await _remoteGate.WaitAsync(cancellationToken);
        try
        {
            byte[] bytes = await DownloadRemoteBytesCoreAsync(item.PreviewUrl, cancellationToken);
            int frames = item.Frames!.Value;
            int framesOverTime = item.FramesOverTime!.Value;
            return await Task.Run(() => LoadSpriteSheetAnimation(bytes, frames, framesOverTime, ThumbnailWidth), cancellationToken);
        }
        catch (IOException exception)
        {
            HandleLoadRemoteAnimationCoreFailure(exception, item); return null;
        }
        catch (UnauthorizedAccessException exception)
        {
            HandleLoadRemoteAnimationCoreFailure(exception, item); return null;
        }
        catch (InvalidOperationException exception)
        {
            HandleLoadRemoteAnimationCoreFailure(exception, item); return null;
        }
        catch (HttpRequestException exception)
        {
            HandleLoadRemoteAnimationCoreFailure(exception, item); return null;
        }
        catch (System.Text.Json.JsonException exception)
        {
            HandleLoadRemoteAnimationCoreFailure(exception, item); return null;
        }
        catch (NotSupportedException exception)
        {
            HandleLoadRemoteAnimationCoreFailure(exception, item); return null;
        }
        catch (ArgumentException exception)
        {
            HandleLoadRemoteAnimationCoreFailure(exception, item); return null;
        }
        catch (System.Runtime.InteropServices.ExternalException exception)
        {
            HandleLoadRemoteAnimationCoreFailure(exception, item); return null;
        }
        catch (OutOfMemoryException exception)
        {
            HandleLoadRemoteAnimationCoreFailure(exception, item); return null;
        }
        catch (OperationCanceledException exception)
        {
            HandleLoadRemoteAnimationCoreFailure(exception, item); return null;
        }
        finally
        {
            _remoteGate.Release();
        }
    }

    private async Task<byte[]> DownloadRemoteBytesCoreAsync(string url, CancellationToken cancellationToken)
    {
        using HttpRequestMessage request = new(HttpMethod.Get, url);
        request.Headers.TryAddWithoutValidation("User-Agent", _remoteUserAgent);
        if (ShouldSendVrchatCookies(url))
        {
            VrcxAuthCookies? cookies;
            lock (_authGate)
            {
                cookies = _remoteCookies;
            }

            if (cookies is not null)
            {
                request.Headers.TryAddWithoutValidation("Cookie", cookies.ToCookieHeader());
            }
        }

        using HttpResponseMessage response = await _httpClient.SendAsync(request, HttpCompletionOption.ResponseHeadersRead, cancellationToken);
        response.EnsureSuccessStatusCode();
        return await response.Content.ReadAsByteArrayAsync(cancellationToken);
    }

    private static BitmapSource LoadGifFrame(string path, int decodeWidth)
    {
        GifBitmapDecoder decoder = new(
            new Uri(path, UriKind.Absolute),
            BitmapCreateOptions.PreservePixelFormat,
            BitmapCacheOption.OnLoad);

        return decoder.Frames.Count == 0
            ? throw new InvalidOperationException("GIF did not contain frames.")
            : ResizeFrame(decoder.Frames[0], decodeWidth);
    }

    private static AnimatedImageFrames LoadGifAnimation(string path, int decodeWidth)
    {
        GifBitmapDecoder decoder = new(
            new Uri(path, UriKind.Absolute),
            BitmapCreateOptions.PreservePixelFormat,
            BitmapCacheOption.OnLoad);

        if (decoder.Frames.Count == 0)
        {
            throw new InvalidOperationException("GIF did not contain frames.");
        }

        int outputFrameCount = Math.Min(decoder.Frames.Count, GifSpriteSheetConverter.MaxFrames);
        int[] frameIndexes = ChooseFrameIndexes(decoder.Frames.Count, outputFrameCount);
        List<BitmapSource> frames = [];
        List<TimeSpan> delays = [];
        foreach (int frameIndex in frameIndexes)
        {
            BitmapFrame frame = decoder.Frames[frameIndex];
            frames.Add(ResizeFrame(frame, decodeWidth));
            delays.Add(GetGifFrameDelay(frame));
        }

        return new AnimatedImageFrames(frames, delays);
    }

    private static AnimatedImageFrames LoadSpriteSheetAnimation(byte[] bytes, int frames, int framesOverTime, int maxFrameWidth)
    {
        int grid = SpriteSheetFrameExtractor.CalculateGrid(frames);
        int sheetDecodeWidth = Math.Clamp(maxFrameWidth * grid, 256, 2048);
        BitmapImage spriteSheet = LoadBitmap(bytes, sheetDecodeWidth);
        return SpriteSheetFrameExtractor.Extract(spriteSheet, frames, framesOverTime, maxFrameWidth);
    }

    private static AnimatedImageFrames LoadSpriteSheetAnimation(string path, int frames, int framesOverTime, int maxFrameWidth)
    {
        int grid = SpriteSheetFrameExtractor.CalculateGrid(frames);
        int sheetDecodeWidth = Math.Clamp(maxFrameWidth * grid, 256, 2048);
        BitmapImage spriteSheet = LoadBitmap(path, sheetDecodeWidth);
        return SpriteSheetFrameExtractor.Extract(spriteSheet, frames, framesOverTime, maxFrameWidth);
    }

    private static BitmapImage LoadBitmap(string path, int decodeWidth)
    {
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

    private static BitmapImage LoadBitmap(byte[] bytes, int decodeWidth)
    {
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

    private static BitmapSource ResizeFrame(BitmapSource source, int decodeWidth)
    {
        double scale = source.PixelWidth <= 0 || source.PixelWidth <= decodeWidth
            ? 1.0
            : decodeWidth / (double)source.PixelWidth;
        if (scale >= 1.0)
        {
            source.Freeze();
            return source;
        }

        TransformedBitmap bitmap = new(source, new System.Windows.Media.ScaleTransform(scale, scale));
        bitmap.Freeze();
        return bitmap;
    }

    private static int[] ChooseFrameIndexes(int sourceFrameCount, int outputFrameCount)
    {
        if (sourceFrameCount == outputFrameCount)
        {
            return [.. Enumerable.Range(0, sourceFrameCount)];
        }

        int[] indexes = new int[outputFrameCount];
        for (int i = 0; i < outputFrameCount; i++)
        {
            indexes[i] = (int)Math.Round(i * (sourceFrameCount - 1) / (double)(outputFrameCount - 1));
        }

        return indexes;
    }

    private static TimeSpan GetGifFrameDelay(BitmapFrame frame)
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

    private static bool ShouldSendVrchatCookies(string url) =>
        Uri.TryCreate(url, UriKind.Absolute, out Uri? uri) &&
        uri.Host.EndsWith("vrchat.cloud", StringComparison.OrdinalIgnoreCase);

    private sealed class ThumbnailCacheEntry<T>
    {
        public ThumbnailCacheEntry(Task<T> task, long lastAccess)
        {
            Task = task;
            LastAccess = lastAccess;
        }

        public Task<T> Task { get; }

        public long LastAccess { get; set; }
    }
}

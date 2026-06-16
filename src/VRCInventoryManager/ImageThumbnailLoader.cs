using System.IO;
using System.Net.Http;
using System.Windows.Media.Imaging;
using VRCInventoryManager.Core;

namespace VRCInventoryManager;

internal sealed class ImageThumbnailLoader : IDisposable
{
    private const int ThumbnailWidth = 120;
    private const int MaxLocalCacheEntries = 512;
    private const int MaxRemoteCacheEntries = 128;
    private readonly Dictionary<string, ThumbnailCacheEntry<BitmapSource?>> localCache = new(StringComparer.OrdinalIgnoreCase);
    private readonly Dictionary<string, ThumbnailCacheEntry<AnimatedImageFrames?>> localAnimationCache = new(StringComparer.OrdinalIgnoreCase);
    private readonly Dictionary<string, ThumbnailCacheEntry<BitmapSource?>> remoteCache = new(StringComparer.OrdinalIgnoreCase);
    private readonly Dictionary<string, ThumbnailCacheEntry<AnimatedImageFrames?>> remoteAnimationCache = new(StringComparer.OrdinalIgnoreCase);
    private readonly object localCacheGate = new();
    private readonly object remoteCacheGate = new();
    private readonly SemaphoreSlim localGate = new(2);
    private readonly SemaphoreSlim remoteGate = new(4);
    private readonly HttpClient httpClient = new();
    private readonly object authGate = new();
    private VrcxAuthCookies? remoteCookies;
    private string remoteUserAgent = "VRCInventoryManager";
    private long cacheStamp;

    public Task<BitmapSource?> LoadLocalAsync(LocalAsset asset) =>
        GetOrAddCache(localCache, localCacheGate, asset.Path, () => LoadLocalCoreAsync(asset), MaxLocalCacheEntries);

    public Task<AnimatedImageFrames?> LoadLocalAnimationAsync(LocalAsset asset) =>
        GetOrAddCache(localAnimationCache, localCacheGate, asset.Path, () => LoadLocalAnimationCoreAsync(asset), MaxLocalCacheEntries);

    public Task<BitmapSource?> LoadRemoteAsync(string url) =>
        GetOrAddCache(remoteCache, remoteCacheGate, url, () => LoadRemoteCoreAsync(url), MaxRemoteCacheEntries);

    public Task<AnimatedImageFrames?> LoadRemoteAnimationAsync(RemoteInventoryItem item) =>
        GetOrAddCache(
            remoteAnimationCache,
            remoteCacheGate,
            $"{item.PreviewUrl}|{item.Frames}|{item.FramesOverTime}",
            () => LoadRemoteAnimationCoreAsync(item),
            MaxRemoteCacheEntries);

    public async Task<byte[]> DownloadRemoteBytesAsync(string url, CancellationToken cancellationToken)
    {
        await remoteGate.WaitAsync(cancellationToken);
        try
        {
            return await DownloadRemoteBytesCoreAsync(url, cancellationToken);
        }
        finally
        {
            remoteGate.Release();
        }
    }

    public void ClearLocal()
    {
        ClearCache(localCache, localCacheGate);
        ClearCache(localAnimationCache, localCacheGate);
    }

    public void ConfigureRemoteAuth(VrcxAuthCookies cookies, string userAgent)
    {
        lock (authGate)
        {
            remoteCookies = cookies;
            remoteUserAgent = string.IsNullOrWhiteSpace(userAgent) ? "VRCInventoryManager" : userAgent;
        }

        ClearCache(remoteCache, remoteCacheGate);
        ClearCache(remoteAnimationCache, remoteCacheGate);
    }

    public void ClearRemoteAuth()
    {
        lock (authGate)
        {
            remoteCookies = null;
        }

        ClearCache(remoteCache, remoteCacheGate);
        ClearCache(remoteAnimationCache, remoteCacheGate);
    }

    public void Dispose()
    {
        localGate.Dispose();
        remoteGate.Dispose();
        httpClient.Dispose();
    }

    private Task<T> GetOrAddCache<T>(
        Dictionary<string, ThumbnailCacheEntry<T>> cache,
        object gate,
        string key,
        Func<Task<T>> factory,
        int maxEntries)
    {
        lock (gate)
        {
            long stamp = Interlocked.Increment(ref cacheStamp);
            if (cache.TryGetValue(key, out ThumbnailCacheEntry<T>? existing))
            {
                existing.LastAccess = stamp;
                return existing.Task;
            }

            Task<T> task = factory();
            cache[key] = new ThumbnailCacheEntry<T>(task, stamp);
            TrimCache(cache, maxEntries);
            return task;
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

        string[] keysToRemove = cache
            .OrderBy(entry => entry.Value.LastAccess)
            .Take(removeCount)
            .Select(entry => entry.Key)
            .ToArray();
        foreach (string key in keysToRemove)
        {
            cache.Remove(key);
        }
    }

    private async Task<BitmapSource?> LoadLocalCoreAsync(LocalAsset asset)
    {
        await localGate.WaitAsync();
        try
        {
            return await Task.Run(() => asset.IsGif
                ? LoadGifFrame(asset.Path, ThumbnailWidth)
                : LoadBitmap(asset.Path, ThumbnailWidth));
        }
        catch (Exception ex)
        {
            App.Log.Warning($"Local thumbnail failed for '{asset.Path}': {ex.Message}");
            return null;
        }
        finally
        {
            localGate.Release();
        }
    }

    private async Task<AnimatedImageFrames?> LoadLocalAnimationCoreAsync(LocalAsset asset)
    {
        if (!asset.IsGif && !asset.HasSpriteSheetAnimation)
        {
            return null;
        }

        await localGate.WaitAsync();
        try
        {
            return await Task.Run(() => asset.IsGif
                ? LoadGifAnimation(asset.Path, ThumbnailWidth)
                : LoadSpriteSheetAnimation(asset.Path, asset.Frames!.Value, asset.FramesOverTime!.Value, ThumbnailWidth));
        }
        catch (Exception ex)
        {
            App.Log.Warning($"Local animated thumbnail failed for '{asset.Path}': {ex.Message}");
            return null;
        }
        finally
        {
            localGate.Release();
        }
    }

    private async Task<BitmapSource?> LoadRemoteCoreAsync(string url)
    {
        await remoteGate.WaitAsync();
        try
        {
            byte[] bytes = await DownloadRemoteBytesCoreAsync(url, CancellationToken.None);
            return await Task.Run(() => LoadBitmap(bytes, ThumbnailWidth));
        }
        catch (Exception ex)
        {
            App.Log.Warning($"Remote thumbnail failed for '{url}': {ex.Message}");
            return null;
        }
        finally
        {
            remoteGate.Release();
        }
    }

    private async Task<AnimatedImageFrames?> LoadRemoteAnimationCoreAsync(RemoteInventoryItem item)
    {
        if (!SpriteSheetFrameExtractor.CanAnimate(item.Frames, item.FramesOverTime) ||
            string.IsNullOrWhiteSpace(item.PreviewUrl))
        {
            return null;
        }

        await remoteGate.WaitAsync();
        try
        {
            byte[] bytes = await DownloadRemoteBytesCoreAsync(item.PreviewUrl, CancellationToken.None);
            int frames = item.Frames!.Value;
            int framesOverTime = item.FramesOverTime!.Value;
            return await Task.Run(() => LoadSpriteSheetAnimation(bytes, frames, framesOverTime, ThumbnailWidth));
        }
        catch (Exception ex)
        {
            App.Log.Warning($"Remote animated thumbnail failed for '{item.Id}': {ex.Message}");
            return null;
        }
        finally
        {
            remoteGate.Release();
        }
    }

    private async Task<byte[]> DownloadRemoteBytesCoreAsync(string url, CancellationToken cancellationToken)
    {
        using HttpRequestMessage request = new(HttpMethod.Get, url);
        request.Headers.TryAddWithoutValidation("User-Agent", remoteUserAgent);
        if (ShouldSendVrchatCookies(url))
        {
            VrcxAuthCookies? cookies;
            lock (authGate)
            {
                cookies = remoteCookies;
            }

            if (cookies is not null)
            {
                request.Headers.TryAddWithoutValidation("Cookie", cookies.ToCookieHeader());
            }
        }

        using HttpResponseMessage response = await httpClient.SendAsync(request, HttpCompletionOption.ResponseHeadersRead, cancellationToken);
        response.EnsureSuccessStatusCode();
        return await response.Content.ReadAsByteArrayAsync(cancellationToken);
    }

    private static BitmapSource LoadGifFrame(string path, int decodeWidth)
    {
        GifBitmapDecoder decoder = new(
            new Uri(path, UriKind.Absolute),
            BitmapCreateOptions.PreservePixelFormat,
            BitmapCacheOption.OnLoad);

        if (decoder.Frames.Count == 0)
        {
            throw new InvalidOperationException("GIF did not contain frames.");
        }

        return ResizeFrame(decoder.Frames[0], decodeWidth);
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
            return Enumerable.Range(0, sourceFrameCount).ToArray();
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

    private static bool ShouldSendVrchatCookies(string url) =>
        Uri.TryCreate(url, UriKind.Absolute, out Uri? uri) &&
        uri.Host.EndsWith("vrchat.cloud", StringComparison.OrdinalIgnoreCase);

    private sealed class ThumbnailCacheEntry<T>(Task<T> task, long lastAccess)
    {
        public Task<T> Task { get; } = task;

        public long LastAccess { get; set; } = lastAccess;
    }
}

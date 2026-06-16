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
    private readonly Dictionary<string, ThumbnailCacheEntry> localCache = new(StringComparer.OrdinalIgnoreCase);
    private readonly Dictionary<string, ThumbnailCacheEntry> remoteCache = new(StringComparer.OrdinalIgnoreCase);
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

    public Task<BitmapSource?> LoadRemoteAsync(string url) =>
        GetOrAddCache(remoteCache, remoteCacheGate, url, () => LoadRemoteCoreAsync(url), MaxRemoteCacheEntries);

    public void ClearLocal() => ClearCache(localCache, localCacheGate);

    public void ConfigureRemoteAuth(VrcxAuthCookies cookies, string userAgent)
    {
        lock (authGate)
        {
            remoteCookies = cookies;
            remoteUserAgent = string.IsNullOrWhiteSpace(userAgent) ? "VRCInventoryManager" : userAgent;
        }

        ClearCache(remoteCache, remoteCacheGate);
    }

    public void ClearRemoteAuth()
    {
        lock (authGate)
        {
            remoteCookies = null;
        }

        ClearCache(remoteCache, remoteCacheGate);
    }

    public void Dispose()
    {
        localGate.Dispose();
        remoteGate.Dispose();
        httpClient.Dispose();
    }

    private Task<BitmapSource?> GetOrAddCache(
        Dictionary<string, ThumbnailCacheEntry> cache,
        object gate,
        string key,
        Func<Task<BitmapSource?>> factory,
        int maxEntries)
    {
        lock (gate)
        {
            long stamp = Interlocked.Increment(ref cacheStamp);
            if (cache.TryGetValue(key, out ThumbnailCacheEntry? existing))
            {
                existing.LastAccess = stamp;
                return existing.Task;
            }

            Task<BitmapSource?> task = factory();
            cache[key] = new ThumbnailCacheEntry(task, stamp);
            TrimCache(cache, maxEntries);
            return task;
        }
    }

    private static void ClearCache(Dictionary<string, ThumbnailCacheEntry> cache, object gate)
    {
        lock (gate)
        {
            cache.Clear();
        }
    }

    private static void TrimCache(Dictionary<string, ThumbnailCacheEntry> cache, int maxEntries)
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

    private async Task<BitmapSource?> LoadRemoteCoreAsync(string url)
    {
        await remoteGate.WaitAsync();
        try
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

            using HttpResponseMessage response = await httpClient.SendAsync(request, HttpCompletionOption.ResponseHeadersRead);
            response.EnsureSuccessStatusCode();
            byte[] bytes = await response.Content.ReadAsByteArrayAsync();
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

    private static bool ShouldSendVrchatCookies(string url) =>
        Uri.TryCreate(url, UriKind.Absolute, out Uri? uri) &&
        uri.Host.EndsWith("vrchat.cloud", StringComparison.OrdinalIgnoreCase);

    private sealed class ThumbnailCacheEntry(Task<BitmapSource?> task, long lastAccess)
    {
        public Task<BitmapSource?> Task { get; } = task;

        public long LastAccess { get; set; } = lastAccess;
    }
}

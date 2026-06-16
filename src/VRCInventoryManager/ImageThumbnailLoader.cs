using System.Collections.Concurrent;
using System.IO;
using System.Net.Http;
using System.Windows.Media.Imaging;
using VRCInventoryManager.Core;

namespace VRCInventoryManager;

internal sealed class ImageThumbnailLoader : IDisposable
{
    private const int ThumbnailWidth = 120;
    private readonly ConcurrentDictionary<string, Task<BitmapSource?>> localCache = new(StringComparer.OrdinalIgnoreCase);
    private readonly ConcurrentDictionary<string, Task<BitmapSource?>> remoteCache = new(StringComparer.OrdinalIgnoreCase);
    private readonly SemaphoreSlim localGate = new(2);
    private readonly SemaphoreSlim remoteGate = new(4);
    private readonly HttpClient httpClient = new();
    private readonly object authGate = new();
    private VrcxAuthCookies? remoteCookies;
    private string remoteUserAgent = "VRCInventoryManager";

    public Task<BitmapSource?> LoadLocalAsync(LocalAsset asset) =>
        localCache.GetOrAdd(asset.Path, _ => LoadLocalCoreAsync(asset));

    public Task<BitmapSource?> LoadRemoteAsync(string url) =>
        remoteCache.GetOrAdd(url, _ => LoadRemoteCoreAsync(url));

    public void ConfigureRemoteAuth(VrcxAuthCookies cookies, string userAgent)
    {
        lock (authGate)
        {
            remoteCookies = cookies;
            remoteUserAgent = string.IsNullOrWhiteSpace(userAgent) ? "VRCInventoryManager" : userAgent;
            remoteCache.Clear();
        }
    }

    public void ClearRemoteAuth()
    {
        lock (authGate)
        {
            remoteCookies = null;
            remoteCache.Clear();
        }
    }

    public void Dispose()
    {
        localGate.Dispose();
        remoteGate.Dispose();
        httpClient.Dispose();
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
}

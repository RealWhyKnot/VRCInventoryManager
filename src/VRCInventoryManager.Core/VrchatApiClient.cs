using System.Net.Http;
using System.Net.Http.Headers;
using System.Text.Json;

namespace VRCInventoryManager.Core;

public sealed class VrchatApiClient
{
    private static readonly Uri DefaultBaseUri = new("https://api.vrchat.cloud/api/1/");

    private readonly HttpClient httpClient;
    private readonly VrcxAuthCookies cookies;
    private readonly string userAgent;
    private readonly GifSpriteSheetConverter spriteSheetConverter;

    public VrchatApiClient(
        HttpClient httpClient,
        VrcxAuthCookies cookies,
        string userAgent,
        GifSpriteSheetConverter? spriteSheetConverter = null)
    {
        this.httpClient = httpClient;
        this.cookies = cookies;
        this.userAgent = userAgent;
        this.spriteSheetConverter = spriteSheetConverter ?? new GifSpriteSheetConverter();

        if (httpClient.BaseAddress is null)
        {
            httpClient.BaseAddress = DefaultBaseUri;
        }
    }

    public async Task<bool> CheckAuthAsync(CancellationToken cancellationToken = default)
    {
        using HttpResponseMessage response = await SendAsync(HttpMethod.Get, "auth/user", null, cancellationToken);
        return response.IsSuccessStatusCode;
    }

    public async Task FetchConfigAsync(CancellationToken cancellationToken = default)
    {
        using HttpResponseMessage response = await SendAsync(HttpMethod.Get, "config", null, cancellationToken);
        await EnsureSuccessAsync(response, "Fetch config", cancellationToken);
    }

    public async Task<RemoteInventorySnapshot> GetInventorySnapshotAsync(CancellationToken cancellationToken = default)
    {
        IReadOnlyList<RemoteInventoryItem> stickers = await ListFilesAsync(VrchatFileTags.Sticker, cancellationToken);
        IReadOnlyList<RemoteInventoryItem> emojis = await ListFilesAsync(VrchatFileTags.Emoji, cancellationToken);
        IReadOnlyList<RemoteInventoryItem> animatedEmojis = await ListFilesAsync(VrchatFileTags.AnimatedEmoji, cancellationToken);
        return new RemoteInventorySnapshot(stickers, emojis, animatedEmojis);
    }

    public async Task<IReadOnlyList<RemoteInventoryItem>> ListFilesAsync(string tag, CancellationToken cancellationToken = default)
    {
        string path = $"files?n=100&tag={Uri.EscapeDataString(tag)}";
        using HttpResponseMessage response = await SendAsync(HttpMethod.Get, path, null, cancellationToken);
        await EnsureSuccessAsync(response, $"List {tag}", cancellationToken);

        await using Stream stream = await response.Content.ReadAsStreamAsync(cancellationToken);
        using JsonDocument document = await JsonDocument.ParseAsync(stream, cancellationToken: cancellationToken);
        List<RemoteInventoryItem> items = [];
        foreach (JsonElement element in document.RootElement.EnumerateArray())
        {
            items.Add(ParseFileItem(element, tag));
        }

        return items;
    }

    public async Task<UploadResult> UploadStickerAsync(string path, CancellationToken cancellationToken = default)
    {
        byte[] bytes = ImagePayloadFactory.GetPngPayload(path);
        return await UploadImageAsync(bytes, VrchatFileTags.Sticker, null, null, null, null, cancellationToken);
    }

    public async Task<UploadResult> UploadStaticEmojiAsync(string path, string animationStyle, CancellationToken cancellationToken = default)
    {
        if (!LocalAsset.CanUploadPathAsStaticEmoji(path))
        {
            throw new InvalidOperationException("Animated emoji sources must be uploaded with Upload Animated Emoji.");
        }

        byte[] bytes = ImagePayloadFactory.GetPngPayload(path);
        return await UploadImageAsync(bytes, VrchatFileTags.Emoji, animationStyle, null, null, null, cancellationToken);
    }

    public async Task<UploadResult> UploadEmojiAsync(string path, string animationStyle, CancellationToken cancellationToken = default)
    {
        if (string.Equals(Path.GetExtension(path), ".gif", StringComparison.OrdinalIgnoreCase))
        {
            return await UploadAnimatedEmojiAsync(path, animationStyle, cancellationToken);
        }

        if (LocalAsset.TryGetSpriteSheetAnimationMetadata(path, out int frames, out int framesOverTime))
        {
            return await UploadAnimatedEmojiSpriteSheetAsync(path, animationStyle, frames, framesOverTime, cancellationToken);
        }

        return await UploadStaticEmojiAsync(path, animationStyle, cancellationToken);
    }

    public async Task<UploadResult> UploadAnimatedEmojiAsync(string gifPath, string animationStyle, CancellationToken cancellationToken = default)
    {
        SpriteSheetResult spriteSheet = spriteSheetConverter.Convert(gifPath);
        return await UploadImageAsync(
            spriteSheet.PngBytes,
            VrchatFileTags.AnimatedEmoji,
            animationStyle,
            LoopStyle.FromFileName(Path.GetFileName(gifPath)),
            spriteSheet.Frames,
            spriteSheet.FramesOverTime,
            cancellationToken);
    }

    public async Task<UploadResult> UploadAnimatedEmojiSpriteSheetAsync(
        string path,
        string animationStyle,
        int frames,
        int framesOverTime,
        CancellationToken cancellationToken = default)
    {
        if (frames <= 1)
        {
            throw new ArgumentOutOfRangeException(nameof(frames), "Animated emoji upload requires at least two frames.");
        }

        if (frames > GifSpriteSheetConverter.MaxFrames)
        {
            throw new ArgumentOutOfRangeException(nameof(frames), "Animated emoji upload supports at most 64 frames.");
        }

        if (framesOverTime <= 0)
        {
            throw new ArgumentOutOfRangeException(nameof(framesOverTime), "Animated emoji upload requires a positive frame rate.");
        }

        byte[] bytes = ImagePayloadFactory.GetSpriteSheetPngPayload(path);
        return await UploadImageAsync(
            bytes,
            VrchatFileTags.AnimatedEmoji,
            animationStyle,
            LoopStyle.FromFileName(Path.GetFileName(path)),
            frames,
            Math.Clamp(framesOverTime, 1, GifSpriteSheetConverter.MaxFramesPerSecond),
            cancellationToken);
    }

    public async Task DeleteFileAsync(string fileId, CancellationToken cancellationToken = default)
    {
        if (string.IsNullOrWhiteSpace(fileId))
        {
            throw new ArgumentException("File id is required.", nameof(fileId));
        }

        using HttpResponseMessage response = await SendAsync(HttpMethod.Delete, $"file/{Uri.EscapeDataString(fileId)}", null, cancellationToken);
        await EnsureSuccessAsync(response, "Delete file", cancellationToken);
    }

    private async Task<UploadResult> UploadImageAsync(
        byte[] pngBytes,
        string tag,
        string? animationStyle,
        string? loopStyle,
        int? frames,
        int? framesOverTime,
        CancellationToken cancellationToken)
    {
        using MultipartFormDataContent content = new();
        content.Add(new StringContent(tag), "tag");
        content.Add(new StringContent("square"), "maskTag");
        if (!string.IsNullOrWhiteSpace(animationStyle))
        {
            content.Add(new StringContent(animationStyle), "animationStyle");
        }

        if (frames.HasValue)
        {
            content.Add(new StringContent(frames.Value.ToString()), "frames");
        }

        if (framesOverTime.HasValue)
        {
            content.Add(new StringContent(framesOverTime.Value.ToString()), "framesOverTime");
        }

        if (!string.IsNullOrWhiteSpace(loopStyle))
        {
            content.Add(new StringContent(loopStyle), "loopStyle");
        }

        ByteArrayContent fileContent = new(pngBytes);
        fileContent.Headers.ContentType = new MediaTypeHeaderValue("image/png");
        content.Add(fileContent, "file", "blob");

        using HttpResponseMessage response = await SendAsync(HttpMethod.Post, "file/image", content, cancellationToken);
        await EnsureSuccessAsync(response, "Upload image", cancellationToken);

        await using Stream stream = await response.Content.ReadAsStreamAsync(cancellationToken);
        using JsonDocument document = await JsonDocument.ParseAsync(stream, cancellationToken: cancellationToken);
        string id = document.RootElement.TryGetProperty("id", out JsonElement idElement)
            ? idElement.GetString() ?? string.Empty
            : string.Empty;
        return new UploadResult(id, tag);
    }

    private async Task<HttpResponseMessage> SendAsync(
        HttpMethod method,
        string path,
        HttpContent? content,
        CancellationToken cancellationToken)
    {
        HttpRequestMessage request = new(method, path);
        request.Headers.TryAddWithoutValidation("User-Agent", userAgent);
        request.Headers.TryAddWithoutValidation("Cookie", cookies.ToCookieHeader());
        request.Headers.Accept.Add(new MediaTypeWithQualityHeaderValue("application/json"));
        request.Content = content;
        return await httpClient.SendAsync(request, cancellationToken);
    }

    private static async Task EnsureSuccessAsync(HttpResponseMessage response, string action, CancellationToken cancellationToken)
    {
        if (response.IsSuccessStatusCode)
        {
            return;
        }

        string body = await response.Content.ReadAsStringAsync(cancellationToken);
        if (body.Length > 500)
        {
            body = body[..500];
        }

        throw new HttpRequestException($"{action} failed with HTTP {(int)response.StatusCode}: {body}");
    }

    private static RemoteInventoryItem ParseFileItem(JsonElement element, string fallbackTag)
    {
        string tag = ReadTag(element, fallbackTag);
        DateTimeOffset? createdAt = null;
        string status = string.Empty;
        string previewUrl = string.Empty;
        if (element.TryGetProperty("versions", out JsonElement versions) && versions.ValueKind == JsonValueKind.Array)
        {
            foreach (JsonElement version in versions.EnumerateArray())
            {
                if (!createdAt.HasValue && version.TryGetProperty("created_at", out JsonElement createdElement) &&
                    DateTimeOffset.TryParse(createdElement.GetString(), out DateTimeOffset parsed))
                {
                    createdAt = parsed;
                }

                if (version.TryGetProperty("status", out JsonElement statusElement))
                {
                    status = statusElement.GetString() ?? string.Empty;
                }

                if (version.TryGetProperty("file", out JsonElement fileElement) &&
                    fileElement.TryGetProperty("url", out JsonElement urlElement) &&
                    urlElement.ValueKind == JsonValueKind.String)
                {
                    previewUrl = urlElement.GetString() ?? string.Empty;
                }
            }
        }

        return new RemoteInventoryItem
        {
            Id = GetString(element, "id"),
            Name = GetString(element, "name"),
            Tag = tag,
            MimeType = GetString(element, "mimeType"),
            AnimationStyle = GetString(element, "animationStyle"),
            LoopStyle = GetString(element, "loopStyle"),
            MaskTag = GetString(element, "maskTag"),
            PreviewUrl = previewUrl,
            Frames = GetInt(element, "frames"),
            FramesOverTime = GetInt(element, "framesOverTime"),
            CreatedAt = createdAt,
            Status = status
        };
    }

    private static string ReadTag(JsonElement element, string fallbackTag)
    {
        if (!element.TryGetProperty("tags", out JsonElement tags) || tags.ValueKind != JsonValueKind.Array)
        {
            return fallbackTag;
        }

        foreach (JsonElement tag in tags.EnumerateArray())
        {
            string? value = tag.GetString();
            if (value is VrchatFileTags.Sticker or VrchatFileTags.Emoji or VrchatFileTags.AnimatedEmoji)
            {
                return value;
            }
        }

        return fallbackTag;
    }

    private static string GetString(JsonElement element, string propertyName)
    {
        return element.TryGetProperty(propertyName, out JsonElement property) && property.ValueKind == JsonValueKind.String
            ? property.GetString() ?? string.Empty
            : string.Empty;
    }

    private static int? GetInt(JsonElement element, string propertyName)
    {
        if (!element.TryGetProperty(propertyName, out JsonElement property))
        {
            return null;
        }

        return property.ValueKind == JsonValueKind.Number && property.TryGetInt32(out int value) ? value : null;
    }
}

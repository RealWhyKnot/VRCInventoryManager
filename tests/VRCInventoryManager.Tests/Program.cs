using System.Drawing;
using System.Drawing.Imaging;
using System.IO;
using System.Net;
using System.Net.Http;
using System.Text;
using System.Text.Json;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using Microsoft.Data.Sqlite;
using VRCInventoryManager.Core;
using DrawingColor = System.Drawing.Color;
using MediaColors = System.Windows.Media.Colors;

List<(string Name, Func<Task> Test)> tests =
[
    ("scan recursive image files", TestScannerAsync),
    ("format local asset details", TestLocalAssetDetailsAsync),
    ("write debug log file", TestDebugLogAsync),
    ("parse animation style from file name", TestAnimationStyleAsync),
    ("parse VRCX cookie payload", TestCookieParsingAsync),
    ("load VRCX cookies from sqlite copy", TestCookieProviderSqliteAsync),
    ("convert animated GIF to sprite sheet", TestGifSpriteSheetAsync),
    ("construct VRChat API requests", TestVrchatApiClientAsync)
];

int failures = 0;
foreach ((string name, Func<Task> test) in tests)
{
    try
    {
        await test();
        Console.WriteLine($"PASS {name}");
    }
    catch (Exception ex)
    {
        failures++;
        Console.WriteLine($"FAIL {name}: {ex.GetType().Name}: {ex.Message}");
    }
}

if (failures > 0)
{
    Environment.ExitCode = 1;
}

static Task TestScannerAsync()
{
    string root = CreateTempDirectory();
    try
    {
        Directory.CreateDirectory(Path.Combine(root, "nested"));
        Directory.CreateDirectory(Path.Combine(root, "Emoji"));
        WritePng(Path.Combine(root, "first.png"), DrawingColor.Red);
        WritePng(Path.Combine(root, "nested", "second.jpg"), DrawingColor.Blue);
        WritePng(Path.Combine(root, "Emoji", "skip.png"), DrawingColor.Green);
        File.WriteAllText(Path.Combine(root, "notes.txt"), "ignore");

        LocalAssetScanner scanner = new();
        IReadOnlyList<LocalAsset> assets = scanner.Scan(root);

        AssertEqual(3, assets.Count, "asset count");
        AssertTrue(assets.Any(asset => asset.Name == "first.png"), "first image found");
        AssertTrue(assets.Any(asset => asset.Name == "second.jpg"), "nested image found");
        AssertTrue(assets.Any(asset => asset.Bucket == "nested"), "bucket from directory");

        IReadOnlyList<LocalAsset> filtered = scanner.Scan(root, new HashSet<string>(StringComparer.OrdinalIgnoreCase) { "Emoji" });
        AssertEqual(2, filtered.Count, "excluded top-level folder");
        return Task.CompletedTask;
    }
    finally
    {
        Directory.Delete(root, recursive: true);
    }
}

static Task TestDebugLogAsync()
{
    string root = CreateTempDirectory();
    try
    {
        string processPath = Path.Combine(root, "VRCInventoryManager.exe");
        string expectedPath = Path.Combine(root, "VRCInventoryManager.debug.log");
        AssertEqual(expectedPath, DebugLog.GetExecutableLogPath(processPath), "exe log path");

        using (DebugLog log = DebugLog.TryCreate(expectedPath, reset: true) ?? throw new InvalidOperationException("Could not create debug log."))
        {
            log.Info("manual entry");
        }

        string content = File.ReadAllText(expectedPath);
        AssertTrue(content.Contains("manual entry", StringComparison.Ordinal), "log entry written");
        AssertTrue(content.Contains("[INFO]", StringComparison.Ordinal), "log level written");
        return Task.CompletedTask;
    }
    finally
    {
        Directory.Delete(root, recursive: true);
    }
}

static Task TestLocalAssetDetailsAsync()
{
    LocalAsset asset = new(
        @"C:\tmp\likeanimationStyle.png",
        "likeanimationStyle.png",
        @"C:\tmp",
        ".png",
        2048,
        DateTimeOffset.UnixEpoch);

    AssertEqual("2.0 KB", asset.SizeText, "size text");
    AssertEqual(".png  2.0 KB  style like", asset.DetailsText, "details text");
    AssertEqual("tmp", asset.Bucket, "bucket");
    return Task.CompletedTask;
}

static Task TestAnimationStyleAsync()
{
    AssertEqual("snow", AnimationStyle.FromFileName("avatar_snowanimationStyle_64frames.png"), "snow style");
    AssertEqual("stop", AnimationStyle.FromFileName("avatar_unknownanimationStyle.png"), "unknown style fallback");
    AssertEqual("stop", AnimationStyle.FromFileName("plain.png"), "missing style fallback");
    return Task.CompletedTask;
}

static Task TestCookieParsingAsync()
{
    string encoded = EncodeCookieJson([
        new CookieShape("auth", "auth_cookie"),
        new CookieShape("twoFactorAuth", "two_factor_cookie")
    ]);

    VrcxAuthCookies cookies = VrcxCookieProvider.ParseCookies(encoded);
    AssertEqual("auth_cookie", cookies.Auth, "auth cookie");
    AssertEqual("two_factor_cookie", cookies.TwoFactorAuth, "two factor cookie");

    string electronShape = EncodeElectronCookieJson();
    VrcxAuthCookies electronCookies = VrcxCookieProvider.ParseCookies(electronShape);
    AssertEqual("api_auth_cookie", electronCookies.Auth, "lower-case auth cookie");
    AssertEqual("api_two_factor_cookie", electronCookies.TwoFactorAuth, "lower-case two factor cookie");

    string missing = EncodeCookieJson([new CookieShape("auth", "do_not_leak")]);
    try
    {
        _ = VrcxCookieProvider.ParseCookies(missing);
        throw new InvalidOperationException("Expected missing twoFactorAuth to fail.");
    }
    catch (InvalidOperationException ex)
    {
        AssertFalse(ex.Message.Contains("do_not_leak", StringComparison.Ordinal), "secret not in exception");
    }

    return Task.CompletedTask;
}

static Task TestCookieProviderSqliteAsync()
{
    string root = CreateTempDirectory();
    string databasePath = Path.Combine(root, "VRCX.sqlite3");
    string versionPath = Path.Combine(root, "Version");
    try
    {
        string encoded = EncodeCookieJson([
            new CookieShape("auth", "auth_from_db"),
            new CookieShape("twoFactorAuth", "two_factor_from_db")
        ]);

        using (SqliteConnection connection = new($"Data Source={databasePath}"))
        {
            connection.Open();
            using SqliteCommand create = connection.CreateCommand();
            create.CommandText = "create table cookies(key text primary key, value text);";
            create.ExecuteNonQuery();

            using SqliteCommand insert = connection.CreateCommand();
            insert.CommandText = "insert into cookies(key, value) values ($key, $value);";
            insert.Parameters.AddWithValue("$key", "default");
            insert.Parameters.AddWithValue("$value", encoded);
            insert.ExecuteNonQuery();
        }

        File.WriteAllText(versionPath, "TestVRCX/1");
        SqliteConnection.ClearAllPools();
        VrcxCookieProvider provider = new(databasePath, versionPath);
        VrcxAuthSession session = provider.LoadDefaultSession();
        AssertEqual("auth_from_db", session.Cookies.Auth, "db auth cookie");
        AssertEqual("two_factor_from_db", session.Cookies.TwoFactorAuth, "db two factor cookie");
        AssertEqual("TestVRCX/1", session.UserAgent, "user agent");
        return Task.CompletedTask;
    }
    finally
    {
        SqliteConnection.ClearAllPools();
        Directory.Delete(root, recursive: true);
    }
}

static Task TestGifSpriteSheetAsync()
{
    string root = CreateTempDirectory();
    string gifPath = Path.Combine(root, "animated.gif");
    try
    {
        WriteAnimatedGif(gifPath);
        GifSpriteSheetConverter converter = new();
        SpriteSheetResult result = converter.Convert(gifPath);
        AssertEqual(2, result.Frames, "frame count");
        AssertTrue(result.FramesOverTime >= 1 && result.FramesOverTime <= 64, "fps clamp");
        AssertEqual(1024, result.CanvasSize, "canvas size");
        AssertTrue(result.PngBytes.Length > 8, "png bytes present");
        AssertEqual(0x89, result.PngBytes[0], "png signature 0");
        AssertEqual((byte)'P', result.PngBytes[1], "png signature 1");
        return Task.CompletedTask;
    }
    finally
    {
        Directory.Delete(root, recursive: true);
    }
}

static async Task TestVrchatApiClientAsync()
{
    string root = CreateTempDirectory();
    string pngPath = Path.Combine(root, "local_likeanimationStyle.png");
    WritePng(pngPath, DrawingColor.Green);

    FakeHttpHandler handler = new();
    HttpClient httpClient = new(handler)
    {
        BaseAddress = new Uri("https://api.vrchat.cloud/api/1/")
    };
    VrchatApiClient client = new(httpClient, new VrcxAuthCookies("auth_cookie", "two_factor_cookie"), "TestAgent/1");

    AssertTrue(await client.CheckAuthAsync(), "auth check");
    await client.FetchConfigAsync();
    RemoteInventorySnapshot snapshot = await client.GetInventorySnapshotAsync();
    AssertEqual(1, snapshot.StickerCount, "sticker count");
    AssertEqual(2, snapshot.EmojiCount, "emoji count");
    AssertEqual("https://files.example/file_emoji.png", snapshot.AllItems.Single(item => item.Id == "file_emoji").PreviewUrl, "preview url");

    UploadResult upload = await client.UploadStaticEmojiAsync(pngPath, "like");
    AssertEqual("file_uploaded", upload.Id, "upload id");
    await client.DeleteFileAsync("file_uploaded");

    CapturedRequest uploadRequest = handler.Requests.Single(request => request.Method == HttpMethod.Post);
    AssertTrue(uploadRequest.Body.Contains("name=tag", StringComparison.Ordinal), "multipart has tag name");
    AssertTrue(uploadRequest.Body.Contains("emoji", StringComparison.Ordinal), "multipart has emoji tag");
    AssertTrue(uploadRequest.Body.Contains("name=animationStyle", StringComparison.Ordinal), "multipart has animation style");
    AssertTrue(uploadRequest.Headers.TryGetValue("Cookie", out string? cookieHeader), "cookie header present");
    AssertEqual("auth=auth_cookie; twoFactorAuth=two_factor_cookie", cookieHeader, "cookie header");
    AssertTrue(handler.Requests.Any(request => request.Method == HttpMethod.Delete && request.Uri.AbsolutePath.EndsWith("/file/file_uploaded", StringComparison.Ordinal)), "delete path");

    Directory.Delete(root, recursive: true);
}

static string EncodeCookieJson(IEnumerable<CookieShape> cookies)
{
    object[] shaped = cookies
        .Select(cookie => new
        {
            Name = cookie.Name,
            Value = cookie.Value,
            Domain = "api.vrchat.cloud",
            Path = "/",
            HttpOnly = true,
            Secure = true
        })
        .ToArray();
    string json = JsonSerializer.Serialize(shaped);
    return Convert.ToBase64String(Encoding.UTF8.GetBytes(json));
}

static string EncodeElectronCookieJson()
{
    object[] shaped =
    [
        new
        {
            name = "auth",
            value = "wrong_auth_cookie",
            domain = "example.com"
        },
        new
        {
            name = "auth",
            value = "api_auth_cookie",
            domain = "api.vrchat.cloud"
        },
        new
        {
            name = "twoFactorAuth",
            value = "api_two_factor_cookie",
            domain = ".vrchat.cloud"
        }
    ];
    string json = JsonSerializer.Serialize(shaped);
    return Convert.ToBase64String(Encoding.UTF8.GetBytes(json));
}

static void WritePng(string path, DrawingColor color)
{
    using Bitmap bitmap = new(16, 16);
    using Graphics graphics = Graphics.FromImage(bitmap);
    graphics.Clear(color);
    bitmap.Save(path, ImageFormat.Png);
}

static void WriteAnimatedGif(string path)
{
    GifBitmapEncoder encoder = new();
    encoder.Frames.Add(BitmapFrame.Create(CreateBitmapSource(MediaColors.Red)));
    encoder.Frames.Add(BitmapFrame.Create(CreateBitmapSource(MediaColors.Blue)));
    using FileStream stream = File.Create(path);
    encoder.Save(stream);
}

static BitmapSource CreateBitmapSource(System.Windows.Media.Color color)
{
    int width = 8;
    int height = 8;
    int stride = width * 4;
    byte[] pixels = new byte[stride * height];
    for (int i = 0; i < pixels.Length; i += 4)
    {
        pixels[i] = color.B;
        pixels[i + 1] = color.G;
        pixels[i + 2] = color.R;
        pixels[i + 3] = color.A;
    }

    return BitmapSource.Create(width, height, 96, 96, PixelFormats.Bgra32, null, pixels, stride);
}

static string CreateTempDirectory()
{
    string path = Path.Combine(Path.GetTempPath(), "vrc-inventory-tests", Guid.NewGuid().ToString("N"));
    Directory.CreateDirectory(path);
    return path;
}

static void AssertEqual<T>(T expected, T actual, string label)
{
    if (!EqualityComparer<T>.Default.Equals(expected, actual))
    {
        throw new InvalidOperationException($"{label}: expected '{expected}', got '{actual}'.");
    }
}

static void AssertTrue(bool condition, string label)
{
    if (!condition)
    {
        throw new InvalidOperationException(label);
    }
}

static void AssertFalse(bool condition, string label) => AssertTrue(!condition, label);

sealed record CookieShape(string Name, string Value);

sealed record CapturedRequest(HttpMethod Method, Uri Uri, Dictionary<string, string> Headers, string Body);

sealed class FakeHttpHandler : HttpMessageHandler
{
    public List<CapturedRequest> Requests { get; } = [];

    protected override async Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
    {
        string body = request.Content is null
            ? string.Empty
            : await request.Content.ReadAsStringAsync(cancellationToken);
        Dictionary<string, string> headers = request.Headers.ToDictionary(
            header => header.Key,
            header => string.Join("; ", header.Value),
            StringComparer.OrdinalIgnoreCase);
        Requests.Add(new CapturedRequest(request.Method, request.RequestUri!, headers, body));

        string path = request.RequestUri?.AbsolutePath ?? string.Empty;
        string query = request.RequestUri?.Query ?? string.Empty;
        if (path.EndsWith("/auth/user", StringComparison.Ordinal))
        {
            return Json(HttpStatusCode.OK, "{}");
        }

        if (path.EndsWith("/config", StringComparison.Ordinal))
        {
            return Json(HttpStatusCode.OK, "{}");
        }

        if (path.EndsWith("/files", StringComparison.Ordinal))
        {
            if (query.Contains("tag=sticker", StringComparison.Ordinal))
            {
                return Json(HttpStatusCode.OK, """[{"id":"file_sticker","tags":["sticker"],"animationStyle":"stop","versions":[{"created_at":"2026-06-13T00:00:00Z","status":"complete","file":{"url":"https://files.example/file_sticker.png"}}]}]""");
            }

            if (query.Contains("tag=emojianimated", StringComparison.Ordinal))
            {
                return Json(HttpStatusCode.OK, """[{"id":"file_anim","tags":["emojianimated"],"animationStyle":"spin","frames":12,"framesOverTime":24,"versions":[{"created_at":"2026-06-13T00:00:00Z","status":"complete","file":{"url":"https://files.example/file_anim.png"}}]}]""");
            }

            if (query.Contains("tag=emoji", StringComparison.Ordinal))
            {
                return Json(HttpStatusCode.OK, """[{"id":"file_emoji","tags":["emoji"],"animationStyle":"like","versions":[{"created_at":"2026-06-13T00:00:00Z","status":"complete","file":{"url":"https://files.example/file_emoji.png"}}]}]""");
            }
        }

        if (path.EndsWith("/file/image", StringComparison.Ordinal) && request.Method == HttpMethod.Post)
        {
            return Json(HttpStatusCode.OK, """{"id":"file_uploaded"}""");
        }

        if (path.EndsWith("/file/file_uploaded", StringComparison.Ordinal) && request.Method == HttpMethod.Delete)
        {
            return Json(HttpStatusCode.OK, """{"id":"file_uploaded"}""");
        }

        return Json(HttpStatusCode.NotFound, """{"error":{"message":"not found"}}""");
    }

    private static HttpResponseMessage Json(HttpStatusCode statusCode, string json)
    {
        return new HttpResponseMessage(statusCode)
        {
            Content = new StringContent(json, Encoding.UTF8, "application/json")
        };
    }
}

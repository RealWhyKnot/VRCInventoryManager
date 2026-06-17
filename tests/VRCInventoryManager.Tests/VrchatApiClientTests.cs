using System.IO;
using System.Net;
using System.Net.Http;
using System.Text;
using VRCInventoryManager.Core;
using DrawingColor = System.Drawing.Color;

namespace VRCInventoryManager.Tests;

internal static class VrchatApiClientTests
{
    public static async Task ConstructRequestsAsync()
    {
        string root = TestFiles.CreateTempDirectory();
        try
        {
            string pngPath = Path.Combine(root, "local_likeanimationStyle.png");
            string animatedPngPath = Path.Combine(root, "local_stopanimationStyle_64frames_24fps_pingpongloopStyle.png");
            TestFiles.WritePng(pngPath, DrawingColor.Green);
            TestFiles.WritePng(animatedPngPath, DrawingColor.Blue);

            FakeHttpHandler handler = new();
            HttpClient httpClient = new(handler)
            {
                BaseAddress = new Uri("https://api.vrchat.cloud/api/1/")
            };
            VrchatApiClient client = new(httpClient, new VrcxAuthCookies("auth_cookie", "two_factor_cookie"), "TestAgent/1");

            TestAssert.True(await client.CheckAuthAsync(), "auth check");
            await client.FetchConfigAsync();
            RemoteInventorySnapshot snapshot = await client.GetInventorySnapshotAsync();
            TestAssert.Equal(1, snapshot.StickerCount, "sticker count");
            TestAssert.Equal(2, snapshot.EmojiCount, "emoji count");
            TestAssert.Equal("https://files.example/file_emoji.png", snapshot.AllItems.Single(item => item.Id == "file_emoji").PreviewUrl, "preview url");

            UploadResult upload = await client.UploadStaticEmojiAsync(pngPath, "like");
            TestAssert.Equal("file_uploaded", upload.Id, "upload id");
            UploadResult animatedUpload = await client.UploadAnimatedEmojiSpriteSheetAsync(animatedPngPath, "stop", 64, 24);
            TestAssert.Equal("file_uploaded", animatedUpload.Id, "animated upload id");
            await client.DeleteFileAsync("file_uploaded");

            List<CapturedRequest> uploadRequests = handler.Requests.Where(request => request.Method == HttpMethod.Post).ToList();
            TestAssert.Equal(2, uploadRequests.Count, "upload request count");
            CapturedRequest uploadRequest = uploadRequests[0];
            TestAssert.True(uploadRequest.Body.Contains("name=tag", StringComparison.Ordinal), "multipart has tag name");
            TestAssert.True(uploadRequest.Body.Contains("emoji", StringComparison.Ordinal), "multipart has emoji tag");
            TestAssert.True(uploadRequest.Body.Contains("name=animationStyle", StringComparison.Ordinal), "multipart has animation style");
            TestAssert.False(uploadRequest.Body.Contains("name=frames", StringComparison.Ordinal), "static multipart has no frame count");
            TestAssert.False(uploadRequest.Body.Contains("name=framesOverTime", StringComparison.Ordinal), "static multipart has no fps");
            TestAssert.False(uploadRequest.Body.Contains("name=loopStyle", StringComparison.Ordinal), "static multipart has no loop style");
            CapturedRequest animatedUploadRequest = uploadRequests[1];
            TestAssert.True(animatedUploadRequest.Body.Contains("emojianimated", StringComparison.Ordinal), "multipart has animated emoji tag");
            TestAssert.True(animatedUploadRequest.Body.Contains("name=frames", StringComparison.Ordinal), "multipart has frame count");
            TestAssert.True(animatedUploadRequest.Body.Contains("64", StringComparison.Ordinal), "multipart has frame count value");
            TestAssert.True(animatedUploadRequest.Body.Contains("name=framesOverTime", StringComparison.Ordinal), "multipart has fps");
            TestAssert.True(animatedUploadRequest.Body.Contains("24", StringComparison.Ordinal), "multipart has fps value");
            TestAssert.True(animatedUploadRequest.Body.Contains("name=loopStyle", StringComparison.Ordinal), "multipart has loop style");
            TestAssert.True(animatedUploadRequest.Body.Contains("pingpong", StringComparison.Ordinal), "multipart has loop style value");
            TestAssert.True(uploadRequest.Headers.TryGetValue("Cookie", out string? cookieHeader), "cookie header present");
            TestAssert.Equal("auth=auth_cookie; twoFactorAuth=two_factor_cookie", cookieHeader, "cookie header");
            TestAssert.True(handler.Requests.Any(request => request.Method == HttpMethod.Delete && request.Uri.AbsolutePath.EndsWith("/file/file_uploaded", StringComparison.Ordinal)), "delete path");
        }
        finally
        {
            Directory.Delete(root, recursive: true);
        }
    }

    public static async Task RejectAnimatedSourcesForStaticEmojiUploadsAsync()
    {
        string root = TestFiles.CreateTempDirectory();
        try
        {
            string gifPath = Path.Combine(root, "animated_stopanimationStyle.gif");
            string spriteSheetPath = Path.Combine(root, "sprite_stopanimationStyle_64frames_24fps.png");
            TestFiles.WriteAnimatedGif(gifPath);
            TestFiles.WritePng(spriteSheetPath, DrawingColor.Purple);

            FakeHttpHandler handler = new();
            HttpClient httpClient = new(handler)
            {
                BaseAddress = new Uri("https://api.vrchat.cloud/api/1/")
            };
            VrchatApiClient client = new(httpClient, new VrcxAuthCookies("auth_cookie", "two_factor_cookie"), "TestAgent/1");

            await TestAssert.ThrowsAsync<InvalidOperationException>(
                () => client.UploadStaticEmojiAsync(gifPath, "stop"),
                "static GIF upload rejected");
            await TestAssert.ThrowsAsync<InvalidOperationException>(
                () => client.UploadStaticEmojiAsync(spriteSheetPath, "stop"),
                "static sprite sheet upload rejected");

            TestAssert.Equal(0, handler.Requests.Count, "guard rejects before HTTP");
        }
        finally
        {
            Directory.Delete(root, recursive: true);
        }
    }

    private sealed record CapturedRequest(HttpMethod Method, Uri Uri, Dictionary<string, string> Headers, string Body);

    private sealed class FakeHttpHandler : HttpMessageHandler
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
}

using System.IO;
using System.Net.Http;
using System.Text.Json;
using VRCInventoryManager.Core;

namespace VRCInventoryManager.Tests;

public sealed class CompatibilityTests
{
    [Fact]
    public void ReadsAndWritesExistingSettingsKeys()
    {
        string root = TestFiles.CreateTempDirectory();
        string path = Path.Combine(root, "settings.json");
        try
        {
            File.WriteAllText(path, """{"LocalRoot":"folder","UpdateCheck":false,"SkippedUpdate":"v2026.6.13.0","Unknown":1}""");
            var store = new AppSettingsStore(path);
            AppSettings settings = store.Load();
            Assert.Equal("folder", settings.LocalRoot);
            Assert.False(settings.IsUpdateCheckEnabled);
            Assert.Equal("v2026.6.13.0", settings.SkippedUpdate);
            store.Save(settings);
            using var document = JsonDocument.Parse(File.ReadAllText(path));
            Assert.Equal("folder", document.RootElement.GetProperty("LocalRoot").GetString());
            Assert.False(document.RootElement.GetProperty("UpdateCheck").GetBoolean());
            Assert.Equal("v2026.6.13.0", document.RootElement.GetProperty("SkippedUpdate").GetString());
            Assert.Equal(settings, store.Load());
        }
        finally
        {
            Directory.Delete(root, recursive: true);
        }
    }

    [Fact]
    public void CorruptSettingsKeepExistingDefaults()
    {
        string root = TestFiles.CreateTempDirectory();
        string path = Path.Combine(root, "settings.json");
        try
        {
            File.WriteAllText(path, "{");
            var store = new AppSettingsStore(path);
            Assert.Equal(new AppSettings(), store.Load());
            Assert.Equal("{", File.ReadAllText(path));
        }
        finally
        {
            Directory.Delete(root, recursive: true);
        }
    }

    [Fact]
    public void ReleaseFlagsKeepTheirPublishedJsonNames()
    {
        GithubRelease? release = JsonSerializer.Deserialize<GithubRelease>("""{"tag_name":"v2026.6.13.0-beta","prerelease":true,"draft":true}""");
        Assert.NotNull(release);
        Assert.True(release.IsPrerelease);
        Assert.True(release.IsDraft);
        using var document = JsonDocument.Parse(JsonSerializer.Serialize(release));
        Assert.True(document.RootElement.GetProperty("prerelease").GetBoolean());
        Assert.True(document.RootElement.GetProperty("draft").GetBoolean());
    }

    [Fact]
    public void InvalidCookieEncodingKeepsTheExistingError()
    {
        InvalidOperationException exception = Assert.Throws<InvalidOperationException>(
            () => VrcxCookieProvider.ParseCookies("not base64!"));
        Assert.Equal("VRCX cookie payload was not valid base64.", exception.Message);
    }

    [Fact]
    public async Task CancelledInventoryRequestDoesNotSendAsync()
    {
        using var handler = new CancelledRequestHandler();
        using var httpClient = new HttpClient(handler);
        var client = new VrchatApiClient(httpClient, new VrcxAuthCookies("auth", "second"), "test/1");
        using var cancellation = new CancellationTokenSource();
        await cancellation.CancelAsync();
        await Assert.ThrowsAnyAsync<OperationCanceledException>(
            () => client.GetInventorySnapshotAsync(cancellation.Token));
        Assert.False(handler.HasSent);
    }

    private sealed class CancelledRequestHandler : HttpMessageHandler
    {
        public bool HasSent { get; private set; }

        protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
        {
            HasSent = true;
            throw new InvalidOperationException();
        }
    }
}

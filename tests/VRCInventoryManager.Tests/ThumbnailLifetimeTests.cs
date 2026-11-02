using System.Net.Http;
using System.Windows.Media.Imaging;

namespace VRCInventoryManager.Tests;

public sealed class ThumbnailLifetimeTests
{
    [Fact]
    public async Task DisposingAnActiveDownloadLetsItReleaseItsGateAsync()
    {
        using var handler = new PendingDownloadHandler();
        using var loader = new ImageThumbnailLoader(handler);
        Task<BitmapSource?> loading = loader.LoadRemoteAsync("https://example.invalid/preview.png");
        await handler.Started.Task.WaitAsync(TimeSpan.FromSeconds(10));
        loader.Dispose();
        Assert.Null(await loading.WaitAsync(TimeSpan.FromSeconds(10)));
    }

    private sealed class PendingDownloadHandler : HttpMessageHandler
    {
        public TaskCompletionSource Started { get; } = new(TaskCreationOptions.RunContinuationsAsynchronously);

        protected override async Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
        {
            TaskCompletionSource<HttpResponseMessage> response = new(TaskCreationOptions.RunContinuationsAsynchronously);
            using CancellationTokenRegistration registration = cancellationToken.Register(() => response.TrySetCanceled(cancellationToken));
            Started.TrySetResult();
            return await response.Task;
        }
    }
}

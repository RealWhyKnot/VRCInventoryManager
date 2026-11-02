using System.Net.Http;
using System.Windows;
using System.Windows.Controls;
using VRCInventoryManager.Core;
using WpfMessageBox = System.Windows.MessageBox;

namespace VRCInventoryManager;

internal sealed partial class MainWindow
{
    private async Task ConnectAsync(CancellationToken cancellationToken = default)
    {
        try
        {
            await ConnectCoreAsync(cancellationToken);
        }
        catch (System.IO.IOException exception)
        {
            HandleConnectFailure(exception);
        }
        catch (UnauthorizedAccessException exception)
        {
            HandleConnectFailure(exception);
        }
        catch (InvalidOperationException exception)
        {
            HandleConnectFailure(exception);
        }
        catch (HttpRequestException exception)
        {
            HandleConnectFailure(exception);
        }
        catch (System.Text.Json.JsonException exception)
        {
            HandleConnectFailure(exception);
        }
        catch (NotSupportedException exception)
        {
            HandleConnectFailure(exception);
        }
        catch (Microsoft.Data.Sqlite.SqliteException exception)
        {
            HandleConnectFailure(exception);
        }
        catch (OperationCanceledException exception)
        {
            HandleConnectFailure(exception);
        }
        finally
        {
            SetBusy(isBusy: false);
            UpdateButtons();
        }
    }

    private async Task RefreshRemoteAsync(CancellationToken cancellationToken = default)
    {
        if (_apiClient is null)
        {
            ConnectionStatusText.Text = "Disconnected. Reconnect to load VRChat inventory.";
            return;
        }

        SetBusy(isBusy: true);
        try
        {
            RemoteInventorySnapshot snapshot = await _apiClient.GetInventorySnapshotAsync(cancellationToken);
            _stickerCount = snapshot.StickerCount;
            _emojiCount = snapshot.EmojiCount;
            _remoteItems.Clear();
            foreach (RemoteInventoryItem item in snapshot.AllItems)
            {
                _remoteItems.Add(new RemoteInventoryViewModel(item));
            }

            RemoteCountText.Text = $"Stickers {snapshot.StickerCount}/{RemoteInventorySnapshot.StickerLimit}  Emojis {snapshot.EmojiCount}/{RemoteInventorySnapshot.EmojiLimit}";
            RemoteEmptyText.Visibility = _remoteItems.Count == 0 ? Visibility.Visible : Visibility.Collapsed;
            App.Log.Info($"Loaded remote inventory: {snapshot.StickerCount} stickers, {snapshot.EmojiCount} emojis.");
        }
        catch (System.IO.IOException exception)
        {
            HandleRefreshRemoteFailure(exception);
        }
        catch (UnauthorizedAccessException exception)
        {
            HandleRefreshRemoteFailure(exception);
        }
        catch (InvalidOperationException exception)
        {
            HandleRefreshRemoteFailure(exception);
        }
        catch (HttpRequestException exception)
        {
            HandleRefreshRemoteFailure(exception);
        }
        catch (System.Text.Json.JsonException exception)
        {
            HandleRefreshRemoteFailure(exception);
        }
        catch (NotSupportedException exception)
        {
            HandleRefreshRemoteFailure(exception);
        }
        catch (OperationCanceledException exception)
        {
            HandleRefreshRemoteFailure(exception);
        }
        finally
        {
            SetBusy(isBusy: false);
            UpdateButtons();
        }
    }

    private async Task UploadSelectedAsync(
        Func<VrchatApiClient, LocalAsset, CancellationToken, Task<UploadResult>> upload,
        Func<LocalAsset, bool>? squareStillPayload = null, CancellationToken cancellationToken = default)
    {
        LocalAsset? asset = GetSelectedUploadAsset();
        if (asset is null)
        {
            return;
        }

        SetBusy(isBusy: true);
        try
        {
            await UploadSelectedCoreAsync(asset, upload, squareStillPayload, cancellationToken);
        }
        catch (System.IO.IOException exception)
        {
            HandleUploadSelectedFailure(exception);
        }
        catch (UnauthorizedAccessException exception)
        {
            HandleUploadSelectedFailure(exception);
        }
        catch (InvalidOperationException exception)
        {
            HandleUploadSelectedFailure(exception);
        }
        catch (HttpRequestException exception)
        {
            HandleUploadSelectedFailure(exception);
        }
        catch (System.Text.Json.JsonException exception)
        {
            HandleUploadSelectedFailure(exception);
        }
        catch (NotSupportedException exception)
        {
            HandleUploadSelectedFailure(exception);
        }
        catch (ArgumentException exception)
        {
            HandleUploadSelectedFailure(exception);
        }
        catch (System.Runtime.InteropServices.ExternalException exception)
        {
            HandleUploadSelectedFailure(exception);
        }
        catch (OutOfMemoryException exception)
        {
            HandleUploadSelectedFailure(exception);
        }
        catch (OperationCanceledException exception)
        {
            HandleUploadSelectedFailure(exception);
        }
        finally
        {
            SetBusy(isBusy: false);
            UpdateButtons();
        }
    }

    private async void Reconnect_Click(object sender, RoutedEventArgs e)
    {
        if (_busyDepth == 0)
        {
            await ConnectAsync(CancellationToken.None);
        }
    }

    private async void RefreshRemote_Click(object sender, RoutedEventArgs e)
    {
        if (_busyDepth == 0)
        {
            await RefreshRemoteAsync(CancellationToken.None);
        }
    }

    private async void UploadSticker_Click(object sender, RoutedEventArgs e) => await UploadSelectedAsync((client, asset, cancellationToken) => client.UploadStickerAsync(asset.Path, cancellationToken), cancellationToken: CancellationToken.None);

    private async void UploadEmoji_Click(object sender, RoutedEventArgs e)
    {
        await UploadSelectedAsync(
            (client, asset, cancellationToken) => client.UploadEmojiAsync(asset.Path, asset.AnimationStyle, cancellationToken),
            asset => !asset.CanUploadAsAnimatedEmoji, CancellationToken.None);
    }

    private async void UploadAnimatedEmoji_Click(object sender, RoutedEventArgs e)
    {
        await UploadSelectedAsync(
            (client, asset, cancellationToken) => asset.IsGif
                ? client.UploadAnimatedEmojiAsync(asset.Path, asset.AnimationStyle, cancellationToken)
                : client.UploadAnimatedEmojiSpriteSheetAsync(
                    asset.Path,
                    asset.AnimationStyle,
                    asset.Frames!.Value,
                    asset.FramesOverTime!.Value, cancellationToken),
            _ => false, CancellationToken.None);
    }

    private void RemoteFileList_SelectionChanged(object sender, SelectionChangedEventArgs e)
    {
        if (_shouldSuppressRemoteSelectionChanged)
        {
            return;
        }

        if (RemoteFileList.SelectedItem is RemoteInventoryViewModel selected)
        {
            RemoteInventoryItem item = selected.Item;
            RemoteSelectedText.Text = $"{item.Summary}\n{item.Id}\n{item.Status}";
            _shouldSuppressLocalSelectionChanged = true;
            LocalAssetList.SelectedItem = null;
            _shouldSuppressLocalSelectionChanged = false;
            LoadRemotePreview(item);
        }
        else
        {
            RemoteSelectedText.Text = string.Empty;
            if (LocalAssetList.SelectedItem is null)
            {
                LoadRemotePreview(item: null);
            }
        }

        UpdateButtons();
    }

    private async void DeleteRemote_Click(object sender, RoutedEventArgs e)
    {
        if (_busyDepth > 0 || _apiClient is null || RemoteFileList.SelectedItem is not RemoteInventoryViewModel selected)
        {
            return;
        }

        RemoteInventoryItem item = selected.Item;
        MessageBoxResult confirm = WpfMessageBox.Show(
            this,
            $"Delete remote {item.DisplayType} file?\n\n{item.Id}\n\nThe local image file will not be deleted.",
            "Delete remote file",
            MessageBoxButton.YesNo,
            MessageBoxImage.Warning);
        if (confirm != MessageBoxResult.Yes)
        {
            return;
        }

        SetBusy(isBusy: true);
        try
        {
            await _apiClient.DeleteFileAsync(item.Id, CancellationToken.None);
            ActionStatusText.Text = $"Deleted {item.Id}";
            App.Log.Info($"Deleted remote file {item.Id}.");
            await RefreshRemoteAsync(CancellationToken.None);
        }
        catch (System.IO.IOException exception)
        {
            HandleDeleteRemoteClickFailure(exception, item);
        }
        catch (UnauthorizedAccessException exception)
        {
            HandleDeleteRemoteClickFailure(exception, item);
        }
        catch (InvalidOperationException exception)
        {
            HandleDeleteRemoteClickFailure(exception, item);
        }
        catch (HttpRequestException exception)
        {
            HandleDeleteRemoteClickFailure(exception, item);
        }
        catch (System.Text.Json.JsonException exception)
        {
            HandleDeleteRemoteClickFailure(exception, item);
        }
        catch (NotSupportedException exception)
        {
            HandleDeleteRemoteClickFailure(exception, item);
        }
        catch (OperationCanceledException exception)
        {
            HandleDeleteRemoteClickFailure(exception, item);
        }
        finally
        {
            SetBusy(isBusy: false);
            UpdateButtons();
        }
    }

    private void DisconnectRemote()
    {
        _apiClient = null;
        _thumbnailLoader.ClearRemoteAuth();
        _httpClient?.Dispose();
        _httpClient = null;
        RemoteCountText.Text = string.Empty;
        _remoteItems.Clear();
        _stickerCount = -1;
        _emojiCount = -1;
        RemoteEmptyText.Visibility = Visibility.Visible;
    }

    private static string GetUploadStatusText(LocalAsset asset, bool squareStillPayload)
    {
        if (!squareStillPayload)
        {
            return asset.IsGif ? "Converting GIF to animated emoji..." : "Uploading animated sprite sheet...";
        }

        try
        {
            return ImagePayloadFactory.NeedsStaticNormalization(asset.Path)
                ? "Preparing PNG for upload..."
                : "Uploading...";
        }
        catch (System.IO.IOException)
        {
            return "Uploading...";
        }
        catch (UnauthorizedAccessException)
        {
            return "Uploading...";
        }
        catch (ArgumentException)
        {
            return "Uploading...";
        }
        catch (OutOfMemoryException)
        {
            return "Uploading...";
        }
    }

    private async Task ConnectCoreAsync(CancellationToken cancellationToken)
    {
        SetBusy(isBusy: true);
        App.Log.Info("Connecting through VRCX cookie store.");
        ConnectionStatusText.Text = "Connecting through VRCX...";
        VrcxAuthSession session = await Task.Run(_cookieProvider.LoadDefaultSession, cancellationToken);
        _thumbnailLoader.ConfigureRemoteAuth(session.Cookies, session.UserAgent);
        _httpClient?.Dispose();
        _httpClient = new HttpClient();
        _apiClient = new VrchatApiClient(_httpClient, session.Cookies, session.UserAgent);

        bool authenticated = await _apiClient.CheckAuthAsync(cancellationToken);
        if (!authenticated)
        {
            DisconnectRemote();
            ConnectionStatusText.Text = "VRCX cookies were found, but VRChat rejected the session.";
            App.Log.Warning("VRChat rejected the VRCX session.");
            return;
        }

        await _apiClient.FetchConfigAsync(cancellationToken);
        ConnectionStatusText.Text = "Connected through VRCX.";
        App.Log.Info("Connected through VRCX.");
        await RefreshRemoteAsync(cancellationToken);

    }

    private async Task UploadSelectedCoreAsync(LocalAsset asset, Func<VrchatApiClient, LocalAsset, CancellationToken, Task<UploadResult>> upload, Func<LocalAsset, bool>? squareStillPayload, CancellationToken cancellationToken)
    {
        bool useSquareStillPayload = squareStillPayload?.Invoke(asset) ?? true;
        ActionStatusText.Text = GetUploadStatusText(asset, useSquareStillPayload);
        App.Log.Info($"Uploading '{asset.Name}'.");
        UploadResult result = await upload(_apiClient!, asset, cancellationToken);
        ActionStatusText.Text = $"Uploaded {result.Id}";
        App.Log.Info($"Upload completed: {result.Id}.");
        await RefreshRemoteAsync(cancellationToken);

    }

    private LocalAsset? GetSelectedUploadAsset()
    {
        if (_busyDepth > 0)
        {
            return null;
        }

        if (_apiClient is null)
        {
            WpfMessageBox.Show(this, "Connect to VRCX before uploading.", "Not connected", MessageBoxButton.OK, MessageBoxImage.Information);
            return null;
        }

        return LocalAssetList.SelectedItem is not LocalAssetViewModel selected ? null : selected.Asset;
    }
}

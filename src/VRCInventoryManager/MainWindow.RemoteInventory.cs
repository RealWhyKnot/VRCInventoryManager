using System.Net.Http;
using System.Windows;
using System.Windows.Controls;
using VRCInventoryManager.Core;
using WpfMessageBox = System.Windows.MessageBox;

namespace VRCInventoryManager;

public partial class MainWindow
{
    private async Task ConnectAsync()
    {
        try
        {
            SetBusy(true);
            App.Log.Info("Connecting through VRCX cookie store.");
            ConnectionStatusText.Text = "Connecting through VRCX...";
            VrcxAuthSession session = await Task.Run(cookieProvider.LoadDefaultSession);
            thumbnailLoader.ConfigureRemoteAuth(session.Cookies, session.UserAgent);
            httpClient?.Dispose();
            httpClient = new HttpClient();
            apiClient = new VrchatApiClient(httpClient, session.Cookies, session.UserAgent);

            bool authenticated = await apiClient.CheckAuthAsync();
            if (!authenticated)
            {
                DisconnectRemote();
                ConnectionStatusText.Text = "VRCX cookies were found, but VRChat rejected the session.";
                App.Log.Warning("VRChat rejected the VRCX session.");
                return;
            }

            await apiClient.FetchConfigAsync();
            ConnectionStatusText.Text = "Connected through VRCX.";
            App.Log.Info("Connected through VRCX.");
            await RefreshRemoteAsync();
        }
        catch (Exception ex)
        {
            DisconnectRemote();
            ConnectionStatusText.Text = $"Disconnected: {ex.Message}";
            App.Log.Error("VRCX connection failed.", ex);
        }
        finally
        {
            SetBusy(false);
            UpdateButtons();
        }
    }

    private async Task RefreshRemoteAsync()
    {
        if (apiClient is null)
        {
            ConnectionStatusText.Text = "Disconnected. Reconnect to load VRChat inventory.";
            return;
        }

        SetBusy(true);
        try
        {
            RemoteInventorySnapshot snapshot = await apiClient.GetInventorySnapshotAsync();
            stickerCount = snapshot.StickerCount;
            emojiCount = snapshot.EmojiCount;
            remoteItems.Clear();
            foreach (RemoteInventoryItem item in snapshot.AllItems)
            {
                remoteItems.Add(new RemoteInventoryViewModel(item));
            }

            RemoteCountText.Text = $"Stickers {snapshot.StickerCount}/{RemoteInventorySnapshot.StickerLimit}  Emojis {snapshot.EmojiCount}/{RemoteInventorySnapshot.EmojiLimit}";
            RemoteEmptyText.Visibility = remoteItems.Count == 0 ? Visibility.Visible : Visibility.Collapsed;
            App.Log.Info($"Loaded remote inventory: {snapshot.StickerCount} stickers, {snapshot.EmojiCount} emojis.");
        }
        catch (Exception ex)
        {
            ConnectionStatusText.Text = $"Inventory refresh failed: {ex.Message}";
            App.Log.Error("Remote inventory refresh failed.", ex);
        }
        finally
        {
            SetBusy(false);
            UpdateButtons();
        }
    }

    private async Task UploadSelectedAsync(
        Func<VrchatApiClient, LocalAsset, Task<UploadResult>> upload,
        bool squareStillPayload = true)
    {
        if (apiClient is null)
        {
            WpfMessageBox.Show(this, "Connect to VRCX before uploading.", "Not connected", MessageBoxButton.OK, MessageBoxImage.Information);
            return;
        }

        if (LocalAssetList.SelectedItem is not LocalAssetViewModel selected)
        {
            return;
        }

        LocalAsset asset = selected.Asset;
        SetBusy(true);
        try
        {
            ActionStatusText.Text = GetUploadStatusText(asset, squareStillPayload);
            App.Log.Info($"Uploading '{asset.Name}'.");
            UploadResult result = await upload(apiClient, asset);
            ActionStatusText.Text = $"Uploaded {result.Id}";
            App.Log.Info($"Upload completed: {result.Id}.");
            await RefreshRemoteAsync();
        }
        catch (Exception ex)
        {
            ActionStatusText.Text = "Upload failed.";
            App.Log.Error("Upload failed.", ex);
            WpfMessageBox.Show(this, ex.Message, "Upload failed", MessageBoxButton.OK, MessageBoxImage.Error);
        }
        finally
        {
            SetBusy(false);
            UpdateButtons();
        }
    }

    private async void Reconnect_Click(object sender, RoutedEventArgs e) => await ConnectAsync();

    private async void RefreshRemote_Click(object sender, RoutedEventArgs e) => await RefreshRemoteAsync();

    private async void UploadSticker_Click(object sender, RoutedEventArgs e)
    {
        await UploadSelectedAsync((client, asset) => client.UploadStickerAsync(asset.Path));
    }

    private async void UploadEmoji_Click(object sender, RoutedEventArgs e)
    {
        await UploadSelectedAsync((client, asset) => client.UploadStaticEmojiAsync(asset.Path, asset.AnimationStyle));
    }

    private async void UploadAnimatedEmoji_Click(object sender, RoutedEventArgs e)
    {
        await UploadSelectedAsync(
            (client, asset) => asset.IsGif
                ? client.UploadAnimatedEmojiAsync(asset.Path, asset.AnimationStyle)
                : client.UploadAnimatedEmojiSpriteSheetAsync(
                    asset.Path,
                    asset.AnimationStyle,
                    asset.Frames!.Value,
                    asset.FramesOverTime!.Value),
            squareStillPayload: false);
    }

    private void RemoteFileList_SelectionChanged(object sender, SelectionChangedEventArgs e)
    {
        if (suppressRemoteSelectionChanged)
        {
            return;
        }

        if (RemoteFileList.SelectedItem is RemoteInventoryViewModel selected)
        {
            RemoteInventoryItem item = selected.Item;
            RemoteSelectedText.Text = $"{item.Summary}\n{item.Id}\n{item.Status}";
            suppressLocalSelectionChanged = true;
            LocalAssetList.SelectedItem = null;
            suppressLocalSelectionChanged = false;
            LoadRemotePreview(item);
        }
        else
        {
            RemoteSelectedText.Text = string.Empty;
            if (LocalAssetList.SelectedItem is null)
            {
                LoadRemotePreview(null);
            }
        }

        UpdateButtons();
    }

    private async void DeleteRemote_Click(object sender, RoutedEventArgs e)
    {
        if (apiClient is null || RemoteFileList.SelectedItem is not RemoteInventoryViewModel selected)
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

        SetBusy(true);
        try
        {
            await apiClient.DeleteFileAsync(item.Id);
            ActionStatusText.Text = $"Deleted {item.Id}";
            App.Log.Info($"Deleted remote file {item.Id}.");
            await RefreshRemoteAsync();
        }
        catch (Exception ex)
        {
            App.Log.Error($"Delete failed for remote file {item.Id}.", ex);
            WpfMessageBox.Show(this, ex.Message, "Delete failed", MessageBoxButton.OK, MessageBoxImage.Error);
        }
        finally
        {
            SetBusy(false);
            UpdateButtons();
        }
    }

    private void DisconnectRemote()
    {
        apiClient = null;
        thumbnailLoader.ClearRemoteAuth();
        httpClient?.Dispose();
        httpClient = null;
        RemoteCountText.Text = string.Empty;
        remoteItems.Clear();
        stickerCount = -1;
        emojiCount = -1;
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
            return ImagePayloadFactory.NeedsSquarePadding(asset.Path)
                ? "Uploading square PNG with transparent padding..."
                : "Uploading...";
        }
        catch (Exception)
        {
            return "Uploading...";
        }
    }
}

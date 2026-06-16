using System.Runtime.InteropServices;
using System.Windows;
using System.Windows.Interop;
using VRCInventoryManager.Core;
using WpfCursors = System.Windows.Input.Cursors;

namespace VRCInventoryManager;

public partial class MainWindow
{
    private void UpdateButtons()
    {
        bool hasAsset = LocalAssetList.SelectedItem is LocalAssetViewModel;
        bool hasGif = LocalAssetList.SelectedItem is LocalAssetViewModel selected && selected.Asset.IsGif;
        bool connected = apiClient is not null;
        bool stickerHasCapacity = stickerCount < 0 || stickerCount < RemoteInventorySnapshot.StickerLimit;
        bool emojiHasCapacity = emojiCount < 0 || emojiCount < RemoteInventorySnapshot.EmojiLimit;
        UploadStickerButton.IsEnabled = hasAsset && connected && stickerHasCapacity;
        UploadEmojiButton.IsEnabled = hasAsset && connected && emojiHasCapacity;
        UploadAnimatedEmojiButton.IsEnabled = hasGif && connected && emojiHasCapacity;
        DeleteRemoteButton.IsEnabled = RemoteFileList.SelectedItem is RemoteInventoryViewModel && connected;
        RemoteEmptyText.Visibility = remoteItems.Count == 0 ? Visibility.Visible : Visibility.Collapsed;
        LocalEmptyText.Visibility = filteredAssets.Count == 0 ? Visibility.Visible : Visibility.Collapsed;
    }

    private void SetBusy(bool isBusy)
    {
        busyDepth = isBusy ? busyDepth + 1 : Math.Max(0, busyDepth - 1);
        bool active = busyDepth > 0;
        Cursor = active ? WpfCursors.Wait : WpfCursors.Arrow;
        BusyBar.Visibility = active ? Visibility.Visible : Visibility.Collapsed;
        HeaderStatusText.Text = active ? "Working..." : "Ready";
    }

    private void EnableDarkTitleBar()
    {
        try
        {
            IntPtr handle = new WindowInteropHelper(this).Handle;
            if (handle == IntPtr.Zero)
            {
                return;
            }

            int enabled = 1;
            _ = DwmSetWindowAttribute(handle, 20, ref enabled, sizeof(int));
        }
        catch (DllNotFoundException)
        {
        }
        catch (EntryPointNotFoundException)
        {
        }
    }

    [DllImport("dwmapi.dll")]
    private static extern int DwmSetWindowAttribute(IntPtr hwnd, int attribute, ref int value, int size);
}

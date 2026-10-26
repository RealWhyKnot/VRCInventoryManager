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
        LocalAsset? selectedAsset = LocalAssetList.SelectedItem is LocalAssetViewModel selected
            ? selected.Asset
            : null;
        InventoryActionState actions = InventoryActionState.For(
            selectedAsset,
            RemoteFileList.SelectedItem is RemoteInventoryViewModel,
            apiClient is not null,
            stickerCount,
            emojiCount,
            busyDepth > 0);
        UploadStickerButton.IsEnabled = actions.UploadSticker;
        UploadEmojiButton.IsEnabled = actions.UploadEmoji;
        UploadAnimatedEmojiButton.IsEnabled = actions.UploadAnimatedEmoji;
        DeleteRemoteButton.IsEnabled = actions.DeleteRemote;
        ReconnectButton.IsEnabled = actions.Reconnect;
        RefreshRemoteButton.IsEnabled = actions.RefreshRemote;
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
        UpdateButtons();
        if (!active && pendingUpdate is not null)
        {
            Dispatcher.BeginInvoke(OfferUpdate);
        }
    }

    internal static void EnableDarkTitleBar(Window window)
    {
        try
        {
            IntPtr handle = new WindowInteropHelper(window).Handle;
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

using System.Runtime.InteropServices;
using System.Windows;
using System.Windows.Interop;
using VRCInventoryManager.Core;
using WpfCursors = System.Windows.Input.Cursors;

namespace VRCInventoryManager;

internal sealed partial class MainWindow
{
    private void UpdateButtons()
    {
        LocalAsset? selectedAsset = LocalAssetList.SelectedItem is LocalAssetViewModel selected
            ? selected.Asset
            : null;
        var actions = InventoryActionState.For(
            selectedAsset,
            RemoteFileList.SelectedItem is RemoteInventoryViewModel,
            _apiClient is not null,
            _stickerCount,
            _emojiCount,
            _busyDepth > 0);
        UploadStickerButton.IsEnabled = actions.CanUploadSticker;
        UploadEmojiButton.IsEnabled = actions.CanUploadEmoji;
        UploadAnimatedEmojiButton.IsEnabled = actions.CanUploadAnimatedEmoji;
        DeleteRemoteButton.IsEnabled = actions.CanDeleteRemote;
        ReconnectButton.IsEnabled = actions.CanReconnect;
        RefreshRemoteButton.IsEnabled = actions.CanRefreshRemote;
        RemoteEmptyText.Visibility = _remoteItems.Count == 0 ? Visibility.Visible : Visibility.Collapsed;
        LocalEmptyText.Visibility = _filteredAssets.Count == 0 ? Visibility.Visible : Visibility.Collapsed;
    }

    private void SetBusy(bool isBusy)
    {
        _busyDepth = isBusy ? _busyDepth + 1 : Math.Max(0, _busyDepth - 1);
        bool active = _busyDepth > 0;
        Cursor = active ? WpfCursors.Wait : WpfCursors.Arrow;
        BusyBar.Visibility = active ? Visibility.Visible : Visibility.Collapsed;
        HeaderStatusText.Text = active ? "Working..." : "Ready";
        UpdateButtons();
        if (!active && _pendingUpdate is not null)
        {
            _ = OfferUpdateWhenIdleAsync(CancellationToken.None);
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
            return;
        }
        catch (EntryPointNotFoundException)
        {
            return;
        }
    }

    [DefaultDllImportSearchPaths(DllImportSearchPath.System32)]
    [LibraryImport("dwmapi.dll")]
    private static partial int DwmSetWindowAttribute(IntPtr hwnd, int attribute, ref int value, int size);
}

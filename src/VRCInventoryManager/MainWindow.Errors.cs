using System.Windows;
using VRCInventoryManager.Core;
using WpfMessageBox = System.Windows.MessageBox;

namespace VRCInventoryManager;

internal sealed partial class MainWindow
{
    private void HandleMainWindowLoadedFailure(Exception exception)
    {
        App.Log.Error("Startup failed.", exception);
        HeaderStatusText.Text = "Startup failed.";
        ActionStatusText.Text = exception.Message;
    }

    private static void HandleCheckForUpdateFailure(Exception exception) => App.Log.Error("Update check failed.", exception);

    private void HandleOfferUpdateFailure(Exception exception)
    {
        App.Log.Error("Update install failed.", exception);
        WpfMessageBox.Show(this, $"Could not install the update: {exception.Message}", "VRCInventoryManager",
            MessageBoxButton.OK, MessageBoxImage.Warning);
    }

    private void HandleRefreshLocalFailure(Exception exception, string folder)
    {
        _allAssets = [];
        ConfigureFolderTree(folder);
        ApplyFilter();
        ActionStatusText.Text = "Local scan failed.";
        App.Log.Error($"Local scan failed for '{folder}'.", exception);
    }

    private void HandleConnectFailure(Exception exception)
    {
        DisconnectRemote();
        ConnectionStatusText.Text = $"Disconnected: {exception.Message}";
        App.Log.Error("VRCX connection failed.", exception);
    }

    private void HandleRefreshRemoteFailure(Exception exception)
    {
        ConnectionStatusText.Text = $"Inventory refresh failed: {exception.Message}";
        App.Log.Error("Remote inventory refresh failed.", exception);
    }

    private void HandleUploadSelectedFailure(Exception exception)
    {
        ActionStatusText.Text = "Upload failed.";
        App.Log.Error("Upload failed.", exception);
        WpfMessageBox.Show(this, exception.Message, "Upload failed", MessageBoxButton.OK, MessageBoxImage.Error);
    }

    private void HandleDeleteRemoteClickFailure(Exception exception, RemoteInventoryItem item)
    {
        App.Log.Error($"Delete failed for remote file {item.Id}.", exception);
        WpfMessageBox.Show(this, exception.Message, "Delete failed", MessageBoxButton.OK, MessageBoxImage.Error);
    }

    private void HandleLoadPreviewFailure(Exception exception, PreviewRequest request, CancellationToken cancellationToken)
    {
        if (IsCurrentPreview(request, cancellationToken))
        {
            StopPreviewAnimation();
            PreviewImage.Source = null;
            PreviewImage.Visibility = Visibility.Collapsed;
            PreviewEmptyText.Text = "Preview failed.";
            PreviewEmptyText.Visibility = Visibility.Visible;
            SelectedFileTitle.Text = request.Title;
            SelectedFileText.Text = "Preview failed.";
            App.Log.Error($"Preview failed for '{request.Key}'.", exception);
        }
    }
}

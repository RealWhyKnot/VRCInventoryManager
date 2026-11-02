using VRCInventoryManager.Core;

namespace VRCInventoryManager;

internal sealed partial class ImageThumbnailLoader
{
    private static void HandleLoadLocalCoreFailure(Exception exception, LocalAsset asset) => App.Log.Warning($"Local thumbnail failed for '{asset.Path}': {exception.Message}");

    private static void HandleLoadLocalAnimationCoreFailure(Exception exception, LocalAsset asset) => App.Log.Warning($"Local animated thumbnail failed for '{asset.Path}': {exception.Message}");

    private static void HandleLoadRemoteCoreFailure(Exception exception, string url) => App.Log.Warning($"Remote thumbnail failed for '{url}': {exception.Message}");

    private static void HandleLoadRemoteAnimationCoreFailure(Exception exception, RemoteInventoryItem item) => App.Log.Warning($"Remote animated thumbnail failed for '{item.Id}': {exception.Message}");
}

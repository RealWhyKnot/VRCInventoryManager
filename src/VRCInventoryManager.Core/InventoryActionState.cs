namespace VRCInventoryManager.Core;

internal sealed record InventoryActionState(
    bool CanUploadSticker,
    bool CanUploadEmoji,
    bool CanUploadAnimatedEmoji,
    bool CanDeleteRemote,
    bool CanReconnect,
    bool CanRefreshRemote)
{
    public static InventoryActionState For(
        LocalAsset? selectedAsset,
        bool isRemoteSelected,
        bool isConnected,
        int stickerCount,
        int emojiCount,
        bool isBusy)
    {
        bool idle = !isBusy;
        bool canUpload = idle && isConnected && selectedAsset is not null;
        bool stickerHasCapacity = stickerCount is < 0 or < RemoteInventorySnapshot.StickerLimit;
        bool emojiHasCapacity = emojiCount is < 0 or < RemoteInventorySnapshot.EmojiLimit;
        return new InventoryActionState(
            canUpload && stickerHasCapacity,
            canUpload && emojiHasCapacity,
            canUpload && emojiHasCapacity && selectedAsset!.CanUploadAsAnimatedEmoji,
            idle && isConnected && isRemoteSelected,
            idle,
            idle);
    }
}

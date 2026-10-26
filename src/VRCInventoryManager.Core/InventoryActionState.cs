namespace VRCInventoryManager.Core;

public sealed record InventoryActionState(
    bool UploadSticker,
    bool UploadEmoji,
    bool UploadAnimatedEmoji,
    bool DeleteRemote,
    bool Reconnect,
    bool RefreshRemote)
{
    public static InventoryActionState For(
        LocalAsset? selectedAsset,
        bool remoteSelected,
        bool connected,
        int stickerCount,
        int emojiCount,
        bool busy)
    {
        bool idle = !busy;
        bool canUpload = idle && connected && selectedAsset is not null;
        bool stickerHasCapacity = stickerCount < 0 || stickerCount < RemoteInventorySnapshot.StickerLimit;
        bool emojiHasCapacity = emojiCount < 0 || emojiCount < RemoteInventorySnapshot.EmojiLimit;
        return new InventoryActionState(
            canUpload && stickerHasCapacity,
            canUpload && emojiHasCapacity,
            canUpload && emojiHasCapacity && selectedAsset!.CanUploadAsAnimatedEmoji,
            idle && connected && remoteSelected,
            idle,
            idle);
    }
}

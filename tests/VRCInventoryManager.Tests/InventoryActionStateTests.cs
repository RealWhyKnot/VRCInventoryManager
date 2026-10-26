using System.IO;
using VRCInventoryManager.Core;

namespace VRCInventoryManager.Tests;

internal static class InventoryActionStateTests
{
    public static Task DisableActionsWhileBusyAsync()
    {
        LocalAsset gif = CreateAsset("wave.gif", ".gif");

        InventoryActionState idle = InventoryActionState.For(gif, remoteSelected: true, connected: true, stickerCount: 0, emojiCount: 0, busy: false);
        TestAssert.True(idle.UploadSticker, "sticker upload when idle");
        TestAssert.True(idle.UploadEmoji, "emoji upload when idle");
        TestAssert.True(idle.UploadAnimatedEmoji, "animated emoji upload when idle");
        TestAssert.True(idle.DeleteRemote, "delete when idle");
        TestAssert.True(idle.Reconnect, "reconnect when idle");
        TestAssert.True(idle.RefreshRemote, "refresh when idle");

        InventoryActionState busy = InventoryActionState.For(gif, remoteSelected: true, connected: true, stickerCount: 0, emojiCount: 0, busy: true);
        TestAssert.Equal(new InventoryActionState(false, false, false, false, false, false), busy, "every action while busy");

        return Task.CompletedTask;
    }

    public static Task KeepUploadRulesWhenIdleAsync()
    {
        LocalAsset png = CreateAsset("still.png", ".png");

        InventoryActionState full = InventoryActionState.For(png, remoteSelected: false, connected: true, stickerCount: RemoteInventorySnapshot.StickerLimit, emojiCount: RemoteInventorySnapshot.EmojiLimit, busy: false);
        TestAssert.False(full.UploadSticker, "sticker upload at the limit");
        TestAssert.False(full.UploadEmoji, "emoji upload at the limit");
        TestAssert.False(full.DeleteRemote, "delete with nothing selected");

        InventoryActionState unknownCounts = InventoryActionState.For(png, remoteSelected: false, connected: true, stickerCount: -1, emojiCount: -1, busy: false);
        TestAssert.True(unknownCounts.UploadSticker, "sticker upload before counts load");
        TestAssert.True(unknownCounts.UploadEmoji, "emoji upload before counts load");
        TestAssert.False(unknownCounts.UploadAnimatedEmoji, "animated upload for a plain PNG");

        InventoryActionState offline = InventoryActionState.For(png, remoteSelected: true, connected: false, stickerCount: 0, emojiCount: 0, busy: false);
        TestAssert.Equal(new InventoryActionState(false, false, false, false, true, true), offline, "actions while disconnected");

        InventoryActionState noAsset = InventoryActionState.For(null, remoteSelected: false, connected: true, stickerCount: 0, emojiCount: 0, busy: false);
        TestAssert.False(noAsset.UploadSticker || noAsset.UploadEmoji || noAsset.UploadAnimatedEmoji, "uploads with no local selection");

        return Task.CompletedTask;
    }

    private static LocalAsset CreateAsset(string name, string extension)
    {
        string directory = @"C:\Photos";
        return new LocalAsset(Path.Combine(directory, name), name, directory, extension, 100, DateTimeOffset.UnixEpoch);
    }
}

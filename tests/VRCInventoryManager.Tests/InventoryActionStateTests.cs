using System.IO;
using VRCInventoryManager.Core;

namespace VRCInventoryManager.Tests;

public sealed class InventoryActionStateTests
{
    [Fact]
    public Task DisableActionsWhileBusyAsync()
    {
        LocalAsset gif = CreateAsset("wave.gif", ".gif");

        var idle = InventoryActionState.For(gif, isRemoteSelected: true, isConnected: true, stickerCount: 0, emojiCount: 0, isBusy: false);
        TestAssert.True(idle.CanUploadSticker, "sticker upload when idle");
        TestAssert.True(idle.CanUploadEmoji, "emoji upload when idle");
        TestAssert.True(idle.CanUploadAnimatedEmoji, "animated emoji upload when idle");
        TestAssert.True(idle.CanDeleteRemote, "delete when idle");
        TestAssert.True(idle.CanReconnect, "reconnect when idle");
        TestAssert.True(idle.CanRefreshRemote, "refresh when idle");

        var isBusy = InventoryActionState.For(gif, isRemoteSelected: true, isConnected: true, stickerCount: 0, emojiCount: 0, isBusy: true);
        TestAssert.Equal(new InventoryActionState(CanUploadSticker: false, CanUploadEmoji: false, CanUploadAnimatedEmoji: false, CanDeleteRemote: false, CanReconnect: false, CanRefreshRemote: false), isBusy, "every action while busy");

        return Task.CompletedTask;
    }

    [Fact]
    public Task KeepUploadRulesWhenIdleAsync()
    {
        LocalAsset png = CreateAsset("still.png", ".png");

        var full = InventoryActionState.For(png, isRemoteSelected: false, isConnected: true, stickerCount: RemoteInventorySnapshot.StickerLimit, emojiCount: RemoteInventorySnapshot.EmojiLimit, isBusy: false);
        TestAssert.False(full.CanUploadSticker, "sticker upload at the limit");
        TestAssert.False(full.CanUploadEmoji, "emoji upload at the limit");
        TestAssert.False(full.CanDeleteRemote, "delete with nothing selected");

        var unknownCounts = InventoryActionState.For(png, isRemoteSelected: false, isConnected: true, stickerCount: -1, emojiCount: -1, isBusy: false);
        TestAssert.True(unknownCounts.CanUploadSticker, "sticker upload before counts load");
        TestAssert.True(unknownCounts.CanUploadEmoji, "emoji upload before counts load");
        TestAssert.False(unknownCounts.CanUploadAnimatedEmoji, "animated upload for a plain PNG");

        var offline = InventoryActionState.For(png, isRemoteSelected: true, isConnected: false, stickerCount: 0, emojiCount: 0, isBusy: false);
        TestAssert.Equal(new InventoryActionState(CanUploadSticker: false, CanUploadEmoji: false, CanUploadAnimatedEmoji: false, CanDeleteRemote: false, CanReconnect: true, CanRefreshRemote: true), offline, "actions while disconnected");

        var noAsset = InventoryActionState.For(selectedAsset: null, isRemoteSelected: false, isConnected: true, stickerCount: 0, emojiCount: 0, isBusy: false);
        TestAssert.False(noAsset.CanUploadSticker || noAsset.CanUploadEmoji || noAsset.CanUploadAnimatedEmoji, "uploads with no local selection");

        return Task.CompletedTask;
    }

    private static LocalAsset CreateAsset(string name, string extension)
    {
        string directory = @"C:\Photos";
        return new LocalAsset(Path.Combine(directory, name), name, directory, extension, 100, DateTimeOffset.UnixEpoch);
    }
}

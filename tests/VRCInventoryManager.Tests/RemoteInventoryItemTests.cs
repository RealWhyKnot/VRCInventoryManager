using VRCInventoryManager.Core;

namespace VRCInventoryManager.Tests;

internal static class RemoteInventoryItemTests
{
    public static Task FormatSummaryWithoutMissingStyleAsync()
    {
        RemoteInventoryItem sticker = new()
        {
            Id = "file_sticker",
            Tag = VrchatFileTags.Sticker
        };
        RemoteInventoryItem emoji = new()
        {
            Id = "file_emoji",
            Tag = VrchatFileTags.Emoji,
            AnimationStyle = "like"
        };
        RemoteInventoryItem animated = new()
        {
            Id = "file_anim",
            Tag = VrchatFileTags.AnimatedEmoji,
            AnimationStyle = "spin",
            Frames = 12,
            FramesOverTime = 24
        };

        TestAssert.Equal("Sticker - static", sticker.Summary, "sticker summary");
        TestAssert.Equal("Emoji - like - static", emoji.Summary, "emoji summary");
        TestAssert.Equal("Animated Emoji - spin - 12 frames @ 24 fps", animated.Summary, "animated summary");
        TestAssert.False(sticker.Summary.Contains("n/a", StringComparison.OrdinalIgnoreCase), "sticker hides n/a");
        return Task.CompletedTask;
    }
}

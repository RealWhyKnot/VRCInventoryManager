namespace VRCInventoryManager.Core;

public sealed record RemoteInventorySnapshot(
    IReadOnlyList<RemoteInventoryItem> Stickers,
    IReadOnlyList<RemoteInventoryItem> Emojis,
    IReadOnlyList<RemoteInventoryItem> AnimatedEmojis)
{
    public const int StickerLimit = 18;
    public const int EmojiLimit = 18;

    public int StickerCount => Stickers.Count;
    public int EmojiCount => Emojis.Count + AnimatedEmojis.Count;

    public IReadOnlyList<RemoteInventoryItem> AllItems => Stickers
        .Concat(Emojis)
        .Concat(AnimatedEmojis)
        .OrderByDescending(item => item.CreatedAt)
        .ThenBy(item => item.Id, StringComparer.OrdinalIgnoreCase)
        .ToArray();
}

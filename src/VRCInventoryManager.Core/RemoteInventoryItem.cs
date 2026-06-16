namespace VRCInventoryManager.Core;

public sealed record RemoteInventoryItem
{
    public string Id { get; init; } = string.Empty;
    public string Tag { get; init; } = string.Empty;
    public string Name { get; init; } = string.Empty;
    public string MimeType { get; init; } = string.Empty;
    public string AnimationStyle { get; init; } = string.Empty;
    public string LoopStyle { get; init; } = string.Empty;
    public string MaskTag { get; init; } = string.Empty;
    public string PreviewUrl { get; init; } = string.Empty;
    public int? Frames { get; init; }
    public int? FramesOverTime { get; init; }
    public DateTimeOffset? CreatedAt { get; init; }
    public string Status { get; init; } = string.Empty;

    public string DisplayType => Tag switch
    {
        VrchatFileTags.Sticker => "Sticker",
        VrchatFileTags.Emoji => "Emoji",
        VrchatFileTags.AnimatedEmoji => "Animated Emoji",
        _ => Tag
    };

    public string Summary
    {
        get
        {
            string style = string.IsNullOrWhiteSpace(AnimationStyle) ? "style n/a" : AnimationStyle;
            string frames = Frames.HasValue && FramesOverTime.HasValue
                ? $"{Frames.Value} frames @ {FramesOverTime.Value} fps"
                : "static";
            return $"{DisplayType} - {style} - {frames}";
        }
    }
}

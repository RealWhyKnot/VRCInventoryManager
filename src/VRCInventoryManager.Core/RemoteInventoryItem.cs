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
            List<string> parts = [DisplayType];
            if (!string.IsNullOrWhiteSpace(AnimationStyle))
            {
                parts.Add(AnimationStyle);
            }

            int frames = Frames.GetValueOrDefault();
            int framesOverTime = FramesOverTime.GetValueOrDefault();
            if (frames > 0 && framesOverTime > 0)
            {
                parts.Add($"{frames} frames @ {framesOverTime} fps");
            }
            else if (!string.Equals(Tag, VrchatFileTags.AnimatedEmoji, StringComparison.OrdinalIgnoreCase))
            {
                parts.Add("static");
            }

            return string.Join(" - ", parts);
        }
    }
}

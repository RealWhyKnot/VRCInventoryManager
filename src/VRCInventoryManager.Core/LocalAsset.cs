using System.Text.RegularExpressions;

namespace VRCInventoryManager.Core;

public sealed record LocalAsset(
    string Path,
    string Name,
    string Directory,
    string Extension,
    long Length,
    DateTimeOffset LastWriteTime)
{
    public string RelativeDirectory { get; init; } = string.Empty;

    public bool IsGif => string.Equals(Extension, ".gif", StringComparison.OrdinalIgnoreCase);

    public int? Frames => ReadIntMetadata("frames");

    public int? FramesOverTime => ReadIntMetadata("fps");

    public bool HasSpriteSheetAnimation =>
        !IsGif &&
        Frames.GetValueOrDefault() > 1 &&
        FramesOverTime.GetValueOrDefault() > 0;

    public string AnimationStyle => VRCInventoryManager.Core.AnimationStyle.FromFileName(Name);

    public string Bucket
    {
        get
        {
            string directory = Directory.TrimEnd(System.IO.Path.DirectorySeparatorChar, System.IO.Path.AltDirectorySeparatorChar);
            string bucket = System.IO.Path.GetFileName(directory);
            return string.IsNullOrWhiteSpace(bucket) ? "Unfiled" : bucket;
        }
    }

    public string SizeText => Length < 1024
        ? $"{Length} B"
        : Length < 1024 * 1024
            ? $"{Length / 1024.0:N1} KB"
            : $"{Length / 1024.0 / 1024.0:N1} MB";

    public string DetailsText
    {
        get
        {
            string details = $"{Extension}  {SizeText}  style {AnimationStyle}";
            int frames = Frames.GetValueOrDefault();
            int framesOverTime = FramesOverTime.GetValueOrDefault();
            if (frames > 0 && framesOverTime > 0)
            {
                details += $"  {frames} frames @ {framesOverTime} fps";
            }

            return details;
        }
    }

    private int? ReadIntMetadata(string suffix)
    {
        Match match = Regex.Match(Name, @$"(?:^|_)(\d+){Regex.Escape(suffix)}(?:_|\.|$)", RegexOptions.IgnoreCase);
        return match.Success && int.TryParse(match.Groups[1].Value, out int value) ? value : null;
    }
}

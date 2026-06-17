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
        HasSpriteSheetAnimationMetadata(Name, Extension);

    public bool CanUploadAsStaticEmoji => CanUploadPathAsStaticEmoji(Name, Extension);

    public bool CanUploadAsAnimatedEmoji => CanUploadPathAsAnimatedEmoji(Name, Extension);

    public string AnimationStyle => VRCInventoryManager.Core.AnimationStyle.FromFileName(Name);

    public string LoopStyle => VRCInventoryManager.Core.LoopStyle.FromFileName(Name);

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

    public static bool CanUploadPathAsStaticEmoji(string path) =>
        CanUploadPathAsStaticEmoji(System.IO.Path.GetFileName(path), System.IO.Path.GetExtension(path));

    public static bool CanUploadPathAsAnimatedEmoji(string path) =>
        CanUploadPathAsAnimatedEmoji(System.IO.Path.GetFileName(path), System.IO.Path.GetExtension(path));

    public static bool TryGetSpriteSheetAnimationMetadata(string path, out int frames, out int framesOverTime) =>
        TryGetSpriteSheetAnimationMetadata(System.IO.Path.GetFileName(path), System.IO.Path.GetExtension(path), out frames, out framesOverTime);

    private static bool CanUploadPathAsStaticEmoji(string name, string extension) =>
        !IsGifExtension(extension) && !HasSpriteSheetAnimationMetadata(name, extension);

    private static bool CanUploadPathAsAnimatedEmoji(string name, string extension) =>
        IsGifExtension(extension) || HasSpriteSheetAnimationMetadata(name, extension);

    private static bool HasSpriteSheetAnimationMetadata(string name, string extension) =>
        TryGetSpriteSheetAnimationMetadata(name, extension, out _, out _);

    private static bool TryGetSpriteSheetAnimationMetadata(string name, string extension, out int frames, out int framesOverTime)
    {
        frames = ReadIntMetadata(name, "frames").GetValueOrDefault();
        framesOverTime = ReadIntMetadata(name, "fps").GetValueOrDefault();
        return !IsGifExtension(extension) && frames > 1 && framesOverTime > 0;
    }

    private static bool IsGifExtension(string extension) =>
        string.Equals(extension, ".gif", StringComparison.OrdinalIgnoreCase);

    private int? ReadIntMetadata(string suffix) => ReadIntMetadata(Name, suffix);

    private static int? ReadIntMetadata(string name, string suffix)
    {
        Match match = Regex.Match(name, @$"(?:^|_)(\d+){Regex.Escape(suffix)}(?:_|\.|$)", RegexOptions.IgnoreCase);
        return match.Success && int.TryParse(match.Groups[1].Value, out int value) ? value : null;
    }
}

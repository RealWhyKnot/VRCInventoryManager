namespace VRCInventoryManager.Core;

public sealed record LocalAsset(
    string Path,
    string Name,
    string Directory,
    string Extension,
    long Length,
    DateTimeOffset LastWriteTime)
{
    public bool IsGif => string.Equals(Extension, ".gif", StringComparison.OrdinalIgnoreCase);

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

    public string DetailsText => $"{Extension}  {SizeText}  style {AnimationStyle}";
}

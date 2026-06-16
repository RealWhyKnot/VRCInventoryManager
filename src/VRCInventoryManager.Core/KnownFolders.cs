namespace VRCInventoryManager.Core;

public static class KnownFolders
{
    public static string DefaultPhotosFolder => Resolve().PhotosFolder;
    public static string DefaultEmojiFolder => Resolve().EmojiFolder;
    public static string DefaultStickersFolder => Resolve().StickersFolder;
    public static string DefaultPrintsFolder => Resolve().PrintsFolder;
    public static string VrcxDatabasePath => Resolve().VrcxDatabasePath;
    public static string VrcxVersionPath => Resolve().VrcxVersionPath;

    public static readonly IReadOnlySet<string> InventoryFolderNames = new HashSet<string>(StringComparer.OrdinalIgnoreCase)
    {
        "Emoji",
        "Stickers",
        "Prints"
    };

    public static KnownFolderPaths Resolve(VrcxPathResolverOptions? options = null) =>
        VrcxPathResolver.Resolve(options);
}

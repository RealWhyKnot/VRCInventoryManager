namespace VRCInventoryManager.Core;

public static class KnownFolders
{
    public const string DefaultPhotosFolder = @"C:\Users\ADMIN\Pictures\VRChat";
    public const string DefaultEmojiFolder = @"C:\Users\ADMIN\Pictures\VRChat\Emoji";
    public const string DefaultStickersFolder = @"C:\Users\ADMIN\Pictures\VRChat\Stickers";
    public const string DefaultPrintsFolder = @"C:\Users\ADMIN\Pictures\VRChat\Prints";
    public const string VrcxDatabasePath = @"C:\Users\ADMIN\AppData\Roaming\VRCX\VRCX.sqlite3";
    public const string VrcxVersionPath = @"C:\Program Files\VRCX\Version";

    public static readonly IReadOnlySet<string> InventoryFolderNames = new HashSet<string>(StringComparer.OrdinalIgnoreCase)
    {
        "Emoji",
        "Stickers",
        "Prints"
    };
}

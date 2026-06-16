namespace VRCInventoryManager.Core;

public sealed record KnownFolderPaths(
    string PhotosFolder,
    string EmojiFolder,
    string StickersFolder,
    string PrintsFolder,
    string VrcxDataFolder,
    string VrcxDatabasePath,
    string VrcxVersionPath,
    string VrcxConfigPath,
    string VrchatConfigPath);

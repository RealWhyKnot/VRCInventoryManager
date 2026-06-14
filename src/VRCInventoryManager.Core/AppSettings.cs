namespace VRCInventoryManager.Core;

public sealed record AppSettings
{
    public string LocalRoot { get; init; } = KnownFolders.DefaultEmojiFolder;
}

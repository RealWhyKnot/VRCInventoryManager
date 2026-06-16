using System.IO;
using System.Text.Json;
using VRCInventoryManager.Core;

namespace VRCInventoryManager.Tests;

internal static class VrcxPathResolverTests
{
    public static Task ResolveConfiguredVrcxAndVrchatPathsAsync()
    {
        string root = TestFiles.CreateTempDirectory();
        try
        {
            TestLayout layout = CreateLayout(root);
            WriteJson(layout.VrcxConfigPath, new Dictionary<string, string>
            {
                ["VRCX_DatabaseLocation"] = @"%AppData%\VRCX\custom-store.sqlite3"
            });
            WriteJson(layout.VrchatConfigPath, new Dictionary<string, string>
            {
                ["picture_output_folder"] = @"%UserProfile%\VRChatMedia"
            });
            WriteFile(layout.LocalInstallVersionPath, "VRCX/Test");

            KnownFolderPaths folders = KnownFolders.Resolve(layout.Options);

            TestAssert.Equal(Full(Path.Combine(layout.UserProfilePath, "VRChatMedia")), folders.PhotosFolder, "configured picture root");
            TestAssert.Equal(Full(Path.Combine(layout.UserProfilePath, "VRChatMedia", "Emoji")), folders.EmojiFolder, "configured emoji folder");
            TestAssert.Equal(Full(Path.Combine(layout.UserProfilePath, "VRChatMedia", "Stickers")), folders.StickersFolder, "configured stickers folder");
            TestAssert.Equal(Full(Path.Combine(layout.UserProfilePath, "VRChatMedia", "Prints")), folders.PrintsFolder, "configured prints folder");
            TestAssert.Equal(Full(Path.Combine(layout.AppDataPath, "VRCX", "custom-store.sqlite3")), folders.VrcxDatabasePath, "configured VRCX database file");
            TestAssert.Equal(Full(layout.LocalInstallVersionPath), folders.VrcxVersionPath, "local VRCX version file");
            TestAssert.Equal(Full(layout.VrchatConfigPath), folders.VrchatConfigPath, "VRChat config path");
            return Task.CompletedTask;
        }
        finally
        {
            Directory.Delete(root, recursive: true);
        }
    }

    public static Task ResolveFallbacksFromCurrentUserFoldersAsync()
    {
        string root = TestFiles.CreateTempDirectory();
        try
        {
            TestLayout layout = CreateLayout(root);

            KnownFolderPaths folders = KnownFolders.Resolve(layout.Options);

            TestAssert.Equal(Full(Path.Combine(layout.MyPicturesPath, "VRChat")), folders.PhotosFolder, "fallback picture root");
            TestAssert.Equal(Full(Path.Combine(layout.MyPicturesPath, "VRChat", "Emoji")), folders.EmojiFolder, "fallback emoji folder");
            TestAssert.Equal(Full(Path.Combine(layout.AppDataPath, "VRCX")), folders.VrcxDataFolder, "fallback VRCX data folder");
            TestAssert.Equal(Full(Path.Combine(layout.AppDataPath, "VRCX", "VRCX.sqlite3")), folders.VrcxDatabasePath, "fallback VRCX database");
            TestAssert.Equal(Full(Path.Combine(layout.ProgramFilesPath, "VRCX", "Version")), folders.VrcxVersionPath, "fallback VRCX version path");
            return Task.CompletedTask;
        }
        finally
        {
            Directory.Delete(root, recursive: true);
        }
    }

    public static Task ResolveVrcxDatabaseDirectoryOverrideAsync()
    {
        string root = TestFiles.CreateTempDirectory();
        try
        {
            TestLayout layout = CreateLayout(root);
            WriteJson(layout.VrcxConfigPath, new Dictionary<string, string>
            {
                ["VRCX_DatabaseLocation"] = @"%AppData%\VRCX\PortableData"
            });

            KnownFolderPaths folders = KnownFolders.Resolve(layout.Options);

            TestAssert.Equal(
                Full(Path.Combine(layout.AppDataPath, "VRCX", "PortableData", "VRCX.sqlite3")),
                folders.VrcxDatabasePath,
                "configured VRCX database directory");
            return Task.CompletedTask;
        }
        finally
        {
            Directory.Delete(root, recursive: true);
        }
    }

    private static TestLayout CreateLayout(string root)
    {
        string userProfilePath = Path.Combine(root, "User");
        string appDataPath = Path.Combine(userProfilePath, "AppData", "Roaming");
        string localApplicationDataPath = Path.Combine(userProfilePath, "AppData", "Local");
        string myPicturesPath = Path.Combine(userProfilePath, "Pictures");
        string programFilesPath = Path.Combine(root, "ProgramFiles");
        string programFilesX86Path = Path.Combine(root, "ProgramFilesX86");
        string vrcxConfigPath = Path.Combine(appDataPath, "VRCX", "VRCX.json");
        string vrchatConfigPath = Full(Path.Combine(appDataPath, "..", "LocalLow", "VRChat", "VRChat", "config.json"));
        string localInstallVersionPath = Path.Combine(localApplicationDataPath, "Programs", "VRCX", "Version");
        VrcxPathResolverOptions options = new()
        {
            AppDataPath = appDataPath,
            LocalApplicationDataPath = localApplicationDataPath,
            UserProfilePath = userProfilePath,
            MyPicturesPath = myPicturesPath,
            ProgramFilesPath = programFilesPath,
            ProgramFilesX86Path = programFilesX86Path
        };

        return new TestLayout(
            userProfilePath,
            appDataPath,
            localApplicationDataPath,
            myPicturesPath,
            programFilesPath,
            vrcxConfigPath,
            vrchatConfigPath,
            localInstallVersionPath,
            options);
    }

    private static void WriteJson(string path, Dictionary<string, string> values) =>
        WriteFile(path, JsonSerializer.Serialize(values));

    private static void WriteFile(string path, string contents)
    {
        string? directory = Path.GetDirectoryName(path);
        if (!string.IsNullOrWhiteSpace(directory))
        {
            Directory.CreateDirectory(directory);
        }

        File.WriteAllText(path, contents);
    }

    private static string Full(string path) => Path.GetFullPath(path);

    private sealed record TestLayout(
        string UserProfilePath,
        string AppDataPath,
        string LocalApplicationDataPath,
        string MyPicturesPath,
        string ProgramFilesPath,
        string VrcxConfigPath,
        string VrchatConfigPath,
        string LocalInstallVersionPath,
        VrcxPathResolverOptions Options);
}

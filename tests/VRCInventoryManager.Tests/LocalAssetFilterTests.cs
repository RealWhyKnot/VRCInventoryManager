using System.IO;
using VRCInventoryManager.Core;

namespace VRCInventoryManager.Tests;

internal static class LocalAssetFilterTests
{
    public static Task MatchFolderSubfoldersAndQueryAsync()
    {
        LocalAsset direct = CreateAsset("direct.png", "2026-06");
        LocalAsset nested = CreateAsset("nested.png", Path.Combine("2026-06", "Quest"));
        LocalAsset other = CreateAsset("other.png", "2026-05");

        TestAssert.True(LocalAssetFilter.Matches(direct, "2026-06", includeSubfolders: false, null), "direct folder match");
        TestAssert.False(LocalAssetFilter.Matches(nested, "2026-06", includeSubfolders: false, null), "nested excluded when direct only");
        TestAssert.True(LocalAssetFilter.Matches(nested, "2026-06", includeSubfolders: true, null), "nested included with subfolders");
        TestAssert.False(LocalAssetFilter.Matches(other, "2026-06", includeSubfolders: true, null), "other folder excluded");
        TestAssert.True(LocalAssetFilter.Matches(other, string.Empty, includeSubfolders: false, "other"), "all node ignores subfolder toggle");
        TestAssert.True(LocalAssetFilter.Matches(nested, "2026-06", includeSubfolders: true, "Quest"), "query matches relative directory");
        TestAssert.False(LocalAssetFilter.Matches(direct, "2026-06", includeSubfolders: true, "missing"), "query filters selected folder");

        return Task.CompletedTask;
    }

    private static LocalAsset CreateAsset(string name, string relativeDirectory)
    {
        string directory = Path.Combine(@"C:\Photos", relativeDirectory);
        return new LocalAsset(
            Path.Combine(directory, name),
            name,
            directory,
            ".png",
            100,
            DateTimeOffset.UnixEpoch)
        {
            RelativeDirectory = relativeDirectory
        };
    }
}

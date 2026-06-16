using System.IO;
using VRCInventoryManager.Core;

namespace VRCInventoryManager.Tests;

internal static class FolderTreeBuilderTests
{
    public static Task BuildFolderTreeWithRollupCountsAsync()
    {
        string root = @"C:\Photos";
        LocalAsset[] assets =
        [
            CreateAsset(root, "root.png", string.Empty),
            CreateAsset(root, "june-a.png", "2026-06"),
            CreateAsset(root, "june-b.png", "2026-06"),
            CreateAsset(root, "quest.png", Path.Combine("2026-06", "Quest")),
            CreateAsset(root, "may.png", "2026-05")
        ];

        FolderNode tree = FolderTreeBuilder.Build(assets, root);

        TestAssert.Equal("All", tree.Name, "root name");
        TestAssert.Equal(string.Empty, tree.RelativePath, "root relative path");
        TestAssert.Equal(1, tree.DirectCount, "root direct count");
        TestAssert.Equal(5, tree.TotalCount, "root total count");
        TestAssert.Equal(2, tree.Children.Count, "root children count");

        FolderNode june = tree.Children[0];
        TestAssert.Equal("2026-06", june.Name, "first child is newest month");
        TestAssert.Equal("2026-06", june.RelativePath, "june relative path");
        TestAssert.Equal(Path.Combine(root, "2026-06"), june.FullPath, "june full path");
        TestAssert.Equal(2, june.DirectCount, "june direct count");
        TestAssert.Equal(3, june.TotalCount, "june total count");
        TestAssert.Equal(1, june.Children.Count, "june child count");

        FolderNode quest = june.Children[0];
        TestAssert.Equal("Quest", quest.Name, "nested child name");
        TestAssert.Equal(Path.Combine("2026-06", "Quest"), quest.RelativePath, "nested relative path");
        TestAssert.Equal(1, quest.DirectCount, "nested direct count");
        TestAssert.Equal(1, quest.TotalCount, "nested total count");

        return Task.CompletedTask;
    }

    private static LocalAsset CreateAsset(string root, string name, string relativeDirectory)
    {
        string directory = string.IsNullOrEmpty(relativeDirectory)
            ? root
            : Path.Combine(root, relativeDirectory);
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

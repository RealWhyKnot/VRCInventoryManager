using System.IO;
using VRCInventoryManager.Core;
using DrawingColor = System.Drawing.Color;

namespace VRCInventoryManager.Tests;

internal static class LocalAssetTests
{
    public static Task ScanRecursiveImageFilesAsync()
    {
        string root = TestFiles.CreateTempDirectory();
        try
        {
            Directory.CreateDirectory(Path.Combine(root, "nested"));
            Directory.CreateDirectory(Path.Combine(root, "Emoji"));
            TestFiles.WritePng(Path.Combine(root, "first.png"), DrawingColor.Red);
            TestFiles.WritePng(Path.Combine(root, "nested", "second.jpg"), DrawingColor.Blue);
            TestFiles.WritePng(Path.Combine(root, "Emoji", "skip.png"), DrawingColor.Green);
            File.WriteAllText(Path.Combine(root, "notes.txt"), "ignore");

            LocalAssetScanner scanner = new();
            IReadOnlyList<LocalAsset> assets = scanner.Scan(root);

            TestAssert.Equal(3, assets.Count, "asset count");
            TestAssert.True(assets.Any(asset => asset.Name == "first.png"), "first image found");
            TestAssert.True(assets.Any(asset => asset.Name == "second.jpg"), "nested image found");
            TestAssert.True(assets.Any(asset => asset.Bucket == "nested"), "bucket from directory");
            TestAssert.True(assets.Any(asset => asset.Name == "first.png" && asset.RelativeDirectory == string.Empty), "root relative directory");
            TestAssert.True(assets.Any(asset => asset.Name == "second.jpg" && asset.RelativeDirectory == "nested"), "nested relative directory");

            IReadOnlyList<LocalAsset> filtered = scanner.Scan(root, new HashSet<string>(StringComparer.OrdinalIgnoreCase) { "Emoji" });
            TestAssert.Equal(2, filtered.Count, "excluded top-level folder");
            TestAssert.False(filtered.Any(asset => asset.RelativeDirectory == "Emoji"), "excluded folder absent");
            return Task.CompletedTask;
        }
        finally
        {
            Directory.Delete(root, recursive: true);
        }
    }

    public static Task FormatLocalAssetDetailsAsync()
    {
        LocalAsset asset = new(
            @"C:\tmp\likeanimationStyle.png",
            "likeanimationStyle.png",
            @"C:\tmp",
            ".png",
            2048,
            DateTimeOffset.UnixEpoch);

        TestAssert.Equal("2.0 KB", asset.SizeText, "size text");
        TestAssert.Equal(".png  2.0 KB  style like", asset.DetailsText, "details text");
        TestAssert.Equal("tmp", asset.Bucket, "bucket");
        return Task.CompletedTask;
    }

    public static Task WriteDebugLogFileAsync()
    {
        string root = TestFiles.CreateTempDirectory();
        try
        {
            string processPath = Path.Combine(root, "VRCInventoryManager.exe");
            string expectedPath = Path.Combine(root, "VRCInventoryManager.debug.log");
            TestAssert.Equal(expectedPath, DebugLog.GetExecutableLogPath(processPath), "exe log path");

            using (DebugLog log = DebugLog.TryCreate(expectedPath, reset: true) ?? throw new InvalidOperationException("Could not create debug log."))
            {
                log.Info("manual entry");
            }

            string content = File.ReadAllText(expectedPath);
            TestAssert.True(content.Contains("manual entry", StringComparison.Ordinal), "log entry written");
            TestAssert.True(content.Contains("[INFO]", StringComparison.Ordinal), "log level written");
            return Task.CompletedTask;
        }
        finally
        {
            Directory.Delete(root, recursive: true);
        }
    }

    public static Task ParseAnimationStyleFromFileNameAsync()
    {
        TestAssert.Equal("snow", AnimationStyle.FromFileName("avatar_snowanimationStyle_64frames.png"), "snow style");
        TestAssert.Equal("stop", AnimationStyle.FromFileName("avatar_unknownanimationStyle.png"), "unknown style fallback");
        TestAssert.Equal("stop", AnimationStyle.FromFileName("plain.png"), "missing style fallback");
        return Task.CompletedTask;
    }

    public static Task ParseLocalSpriteSheetMetadataAsync()
    {
        LocalAsset asset = new(
            @"C:\tmp\avatar_inv_123_stopanimationStyle_64frames_24fps_linearloopStyle.png",
            "avatar_inv_123_stopanimationStyle_64frames_24fps_linearloopStyle.png",
            @"C:\tmp",
            ".png",
            4096,
            DateTimeOffset.UnixEpoch);

        TestAssert.Equal(64, asset.Frames, "frames");
        TestAssert.Equal(24, asset.FramesOverTime, "fps");
        TestAssert.True(asset.HasSpriteSheetAnimation, "sprite sheet animation");
        TestAssert.True(asset.DetailsText.Contains("64 frames @ 24 fps", StringComparison.Ordinal), "details includes animation");

        LocalAsset still = asset with { Name = "avatar_stopanimationStyle.png" };
        TestAssert.Equal(null, still.Frames, "still frames");
        TestAssert.False(still.HasSpriteSheetAnimation, "still image");
        return Task.CompletedTask;
    }
}

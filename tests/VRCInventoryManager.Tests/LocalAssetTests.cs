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
            @"C:\tmp\avatar_inv_123_stopanimationStyle_64frames_24fps_pingpongloopStyle.png",
            "avatar_inv_123_stopanimationStyle_64frames_24fps_pingpongloopStyle.png",
            @"C:\tmp",
            ".png",
            4096,
            DateTimeOffset.UnixEpoch);

        TestAssert.Equal(64, asset.Frames, "frames");
        TestAssert.Equal(24, asset.FramesOverTime, "fps");
        TestAssert.True(asset.HasSpriteSheetAnimation, "sprite sheet animation");
        TestAssert.Equal("pingpong", asset.LoopStyle, "loop style");
        TestAssert.True(asset.DetailsText.Contains("64 frames @ 24 fps", StringComparison.Ordinal), "details includes animation");

        LocalAsset still = asset with { Name = "avatar_stopanimationStyle.png" };
        TestAssert.Equal(null, still.Frames, "still frames");
        TestAssert.False(still.HasSpriteSheetAnimation, "still image");
        return Task.CompletedTask;
    }

    public static Task ClassifyEmojiUploadIntentAsync()
    {
        LocalAsset png = new(
            @"C:\tmp\plain_stopanimationStyle.png",
            "plain_stopanimationStyle.png",
            @"C:\tmp",
            ".png",
            2048,
            DateTimeOffset.UnixEpoch);
        TestAssert.True(png.CanUploadAsStaticEmoji, "plain png static upload");
        TestAssert.False(png.CanUploadAsAnimatedEmoji, "plain png animated upload");
        TestAssert.Equal("linear", png.LoopStyle, "default loop style");

        LocalAsset gif = png with
        {
            Path = @"C:\tmp\animated_stopanimationStyle.gif",
            Name = "animated_stopanimationStyle.gif",
            Extension = ".gif"
        };
        TestAssert.False(gif.CanUploadAsStaticEmoji, "gif static upload denied");
        TestAssert.True(gif.CanUploadAsAnimatedEmoji, "gif animated upload allowed");

        LocalAsset spriteSheet = png with
        {
            Path = @"C:\tmp\sheet_stopanimationStyle_64frames_24fps_linearloopStyle.png",
            Name = "sheet_stopanimationStyle_64frames_24fps_linearloopStyle.png"
        };
        TestAssert.False(spriteSheet.CanUploadAsStaticEmoji, "sprite sheet static upload denied");
        TestAssert.True(spriteSheet.CanUploadAsAnimatedEmoji, "sprite sheet animated upload allowed");
        TestAssert.True(
            LocalAsset.TryGetSpriteSheetAnimationMetadata(spriteSheet.Path, out int frames, out int framesOverTime),
            "sprite sheet metadata parsed");
        TestAssert.Equal(64, frames, "sprite sheet metadata frames");
        TestAssert.Equal(24, framesOverTime, "sprite sheet metadata fps");

        LocalAsset missingFps = png with
        {
            Path = @"C:\tmp\sheet_stopanimationStyle_64frames.png",
            Name = "sheet_stopanimationStyle_64frames.png"
        };
        TestAssert.True(missingFps.CanUploadAsStaticEmoji, "missing fps static upload");
        TestAssert.False(missingFps.CanUploadAsAnimatedEmoji, "missing fps animated upload");
        TestAssert.False(
            LocalAsset.TryGetSpriteSheetAnimationMetadata(missingFps.Path, out _, out _),
            "missing fps metadata not parsed");

        LocalAsset invalidFrameCount = png with { Name = "sheet_stopanimationStyle_1frames_24fps.png" };
        TestAssert.True(invalidFrameCount.CanUploadAsStaticEmoji, "one-frame static upload");
        TestAssert.False(invalidFrameCount.CanUploadAsAnimatedEmoji, "one-frame animated upload");

        return Task.CompletedTask;
    }
}

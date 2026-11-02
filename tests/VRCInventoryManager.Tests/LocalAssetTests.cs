using System.IO;
using VRCInventoryManager.Core;
using DrawingColor = System.Drawing.Color;

namespace VRCInventoryManager.Tests;

public sealed class LocalAssetTests
{
    [Fact]
    public async Task ScanRecursiveImageFilesAsync()
    {
        string root = TestFiles.CreateTempDirectory();
        try
        {
            Directory.CreateDirectory(Path.Combine(root, "nested"));
            Directory.CreateDirectory(Path.Combine(root, "Emoji"));
            TestFiles.WritePng(Path.Combine(root, "first.png"), DrawingColor.Red);
            TestFiles.WritePng(Path.Combine(root, "nested", "second.jpg"), DrawingColor.Blue);
            TestFiles.WritePng(Path.Combine(root, "Emoji", "skip.png"), DrawingColor.Green);
            await File.WriteAllTextAsync(Path.Combine(root, "notes.txt"), "ignore");

            IReadOnlyList<LocalAsset> assets = LocalAssetScanner.Scan(root);

            TestAssert.Equal(3, assets.Count, "asset count");
            TestAssert.True(assets.Any(asset => string.Equals(asset.Name, "first.png", StringComparison.Ordinal)), "first image found");
            TestAssert.True(assets.Any(asset => string.Equals(asset.Name, "second.jpg", StringComparison.Ordinal)), "nested image found");
            TestAssert.True(assets.Any(asset => string.Equals(asset.Bucket, "nested", StringComparison.Ordinal)), "bucket from directory");
            TestAssert.True(assets.Any(asset => string.Equals(asset.Name, "first.png", StringComparison.Ordinal) && string.IsNullOrEmpty(asset.RelativeDirectory)), "root relative directory");
            TestAssert.True(assets.Any(asset => string.Equals(asset.Name, "second.jpg", StringComparison.Ordinal) && string.Equals(asset.RelativeDirectory, "nested", StringComparison.Ordinal)), "nested relative directory");

            IReadOnlyList<LocalAsset> filtered = LocalAssetScanner.Scan(root, new HashSet<string>(StringComparer.OrdinalIgnoreCase) { "Emoji" });
            TestAssert.Equal(2, filtered.Count, "excluded top-level folder");
            TestAssert.False(filtered.Any(asset => string.Equals(asset.RelativeDirectory, "Emoji", StringComparison.Ordinal)), "excluded folder absent");
            return;
        }
        finally
        {
            Directory.Delete(root, recursive: true);
        }
    }

    [Fact]
    public Task FormatLocalAssetDetailsAsync()
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

    [Fact]
    public async Task WriteDebugLogFileAsync()
    {
        string root = TestFiles.CreateTempDirectory();
        try
        {
            string processPath = Path.Combine(root, "VRCInventoryManager.exe");
            string expectedPath = Path.Combine(root, "VRCInventoryManager.debug.log");
            TestAssert.Equal(expectedPath, DebugLog.GetExecutableLogPath(processPath), "exe log path");

            using (DebugLog log = DebugLog.TryCreate(expectedPath, reset: true, new FixedTimeProvider()) ?? throw new InvalidOperationException("Could not create debug log."))
            {
                log.Info("manual entry");
            }

            string content = await File.ReadAllTextAsync(expectedPath);
            TestAssert.True(content.Contains("manual entry", StringComparison.Ordinal), "log entry written");
            TestAssert.True(content.Contains("1970-01-01T00:00:00.0000000+00:00 [INFO] manual entry", StringComparison.Ordinal), "injected clock timestamp");
            return;
        }
        finally
        {
            Directory.Delete(root, recursive: true);
        }
    }

    [Fact]
    public Task ParseAnimationStyleFromFileNameAsync()
    {
        TestAssert.Equal("snow", AnimationStyle.FromFileName("avatar_snowanimationStyle_64frames.png"), "snow style");
        TestAssert.Equal("stop", AnimationStyle.FromFileName("avatar_unknownanimationStyle.png"), "unknown style fallback");
        TestAssert.Equal("stop", AnimationStyle.FromFileName("plain.png"), "missing style fallback");
        return Task.CompletedTask;
    }

    [Fact]
    public Task ParseLocalSpriteSheetMetadataAsync()
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
        TestAssert.Equal(expected: null, still.Frames, "still frames");
        TestAssert.False(still.HasSpriteSheetAnimation, "still image");
        return Task.CompletedTask;
    }

    [Fact]
    public Task ClassifyEmojiUploadIntentAsync()
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
            Extension = ".gif",
        };
        TestAssert.False(gif.CanUploadAsStaticEmoji, "gif static upload denied");
        TestAssert.True(gif.CanUploadAsAnimatedEmoji, "gif animated upload allowed");

        LocalAsset spriteSheet = png with
        {
            Path = @"C:\tmp\sheet_stopanimationStyle_64frames_24fps_linearloopStyle.png",
            Name = "sheet_stopanimationStyle_64frames_24fps_linearloopStyle.png",
        };
        TestAssert.False(spriteSheet.CanUploadAsStaticEmoji, "sprite sheet static upload denied");
        TestAssert.True(spriteSheet.CanUploadAsAnimatedEmoji, "sprite sheet animated upload allowed");
        TestAssert.True(
            LocalAsset.TryGetSpriteSheetAnimationMetadata(spriteSheet.Path, out int frames, out int framesOverTime),
            "sprite sheet metadata parsed");
        TestAssert.Equal(64, frames, "sprite sheet metadata frames");
        TestAssert.Equal(24, framesOverTime, "sprite sheet metadata fps");

        return Task.CompletedTask;
    }

    [Fact]
    public Task RejectIncompleteAnimationMetadataAsync()
    {
        LocalAsset png = new("plain.png", "plain.png", string.Empty, ".png", 2048, DateTimeOffset.UnixEpoch);
        LocalAsset missingFps = png with
        {
            Path = @"C:\tmp\sheet_stopanimationStyle_64frames.png",
            Name = "sheet_stopanimationStyle_64frames.png",
        };
        TestAssert.True(missingFps.CanUploadAsStaticEmoji, "missing fps static upload");
        TestAssert.False(missingFps.CanUploadAsAnimatedEmoji, "missing fps animated upload");
        TestAssert.False(
            LocalAsset.TryGetSpriteSheetAnimationMetadata(missingFps.Path, out _, out _),
            "missing fps metadata not parsed");

        LocalAsset invalidFrameCount = png with { Name = "sheet_stopanimationStyle_1frames_24fps.png" };
        TestAssert.True(invalidFrameCount.CanUploadAsStaticEmoji, "one-frame static upload");
        TestAssert.False(invalidFrameCount.CanUploadAsAnimatedEmoji, "one-frame animated upload");

        LocalAsset jpegWithSpriteName = png with
        {
            Path = @"C:\tmp\sheet_stopanimationStyle_64frames_24fps.jpg",
            Name = "sheet_stopanimationStyle_64frames_24fps.jpg",
            Extension = ".jpg",
        };
        TestAssert.True(jpegWithSpriteName.CanUploadAsStaticEmoji, "jpeg sprite name static upload");
        TestAssert.False(jpegWithSpriteName.CanUploadAsAnimatedEmoji, "jpeg sprite name animated upload");
        TestAssert.False(
            LocalAsset.TryGetSpriteSheetAnimationMetadata(jpegWithSpriteName.Path, out _, out _),
            "jpeg sprite metadata not parsed");

        return Task.CompletedTask;
    }
}

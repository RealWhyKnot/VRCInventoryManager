using VRCInventoryManager.Tests;

TestCase[] tests =
[
    new("scan recursive image files", LocalAssetTests.ScanRecursiveImageFilesAsync),
    new("format local asset details", LocalAssetTests.FormatLocalAssetDetailsAsync),
    new("write debug log file", LocalAssetTests.WriteDebugLogFileAsync),
    new("parse animation style from file name", LocalAssetTests.ParseAnimationStyleFromFileNameAsync),
    new("parse local sprite sheet metadata", LocalAssetTests.ParseLocalSpriteSheetMetadataAsync),
    new("classify emoji upload intent", LocalAssetTests.ClassifyEmojiUploadIntentAsync),
    new("build folder tree with rollup counts", FolderTreeBuilderTests.BuildFolderTreeWithRollupCountsAsync),
    new("match local assets by folder and query", LocalAssetFilterTests.MatchFolderSubfoldersAndQueryAsync),
    new("resolve configured VRCX and VRChat paths", VrcxPathResolverTests.ResolveConfiguredVrcxAndVrchatPathsAsync),
    new("resolve current-user path fallbacks", VrcxPathResolverTests.ResolveFallbacksFromCurrentUserFoldersAsync),
    new("resolve VRCX database directory override", VrcxPathResolverTests.ResolveVrcxDatabaseDirectoryOverrideAsync),
    new("parse VRCX cookie payload", VrcxCookieTests.ParseCookiePayloadAsync),
    new("load VRCX cookies from sqlite copy", VrcxCookieTests.LoadCookiesFromSqliteCopyAsync),
    new("preserve upload-ready static PNG payload", ImagePayloadFactoryTests.PreserveUploadReadyStaticPngAsync),
    new("convert JPEG payload to PNG without square padding", ImagePayloadFactoryTests.ConvertJpegPayloadToPngWithoutSquarePaddingAsync),
    new("downscale oversized static PNG payload", ImagePayloadFactoryTests.DownscaleOversizedStaticPayloadAsync),
    new("preserve square sprite sheet PNG payload", ImagePayloadFactoryTests.PreserveSquareSpriteSheetPngAsync),
    new("reject non-square sprite sheet PNG payload", ImagePayloadFactoryTests.RejectNonSquareSpriteSheetPngAsync),
    new("format remote summary without missing style", RemoteInventoryItemTests.FormatSummaryWithoutMissingStyleAsync),
    new("extract sprite sheet frames in row-major order", SpriteSheetFrameExtractorTests.ExtractSpriteSheetFramesInRowMajorOrderAsync),
    new("use power-of-two grid for sparse sprite sheets", SpriteSheetFrameExtractorTests.UsePowerOfTwoGridForSparseSpriteSheetsAsync),
    new("reject invalid sprite sheet metadata", SpriteSheetFrameExtractorTests.RejectInvalidSpriteSheetMetadataAsync),
    new("convert animated GIF to sprite sheet", GifSpriteSheetConverterTests.ConvertAnimatedGifToSpriteSheetAsync),
    new("use power-of-two GIF sprite sheet grid", GifSpriteSheetConverterTests.UsePowerOfTwoSpriteSheetGridAsync),
    new("preserve GIF duration when downsampling", GifSpriteSheetConverterTests.PreserveDurationWhenDownsamplingGifAsync),
    new("convert optimized GIF using composited frames", GifSpriteSheetConverterTests.ConvertOptimizedGifUsingCompositedFramesAsync),
    new("construct VRChat API requests", VrchatApiClientTests.ConstructRequestsAsync),
    new("upload GIF emoji through animated auto route", VrchatApiClientTests.UploadGifEmojiThroughAnimatedAutoRouteAsync),
    new("reject animated sources for static emoji uploads", VrchatApiClientTests.RejectAnimatedSourcesForStaticEmojiUploadsAsync),
    new("reject invalid sprite sheet uploads", VrchatApiClientTests.RejectInvalidSpriteSheetUploadsAsync)
];

int failures = await TestRunner.RunAsync(tests);
if (failures > 0)
{
    Environment.ExitCode = 1;
}

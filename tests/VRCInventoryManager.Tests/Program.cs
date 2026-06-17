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
    new("square pad non-square PNG payload", ImagePayloadFactoryTests.SquarePadNonSquarePngAsync),
    new("square encode JPEG payload", ImagePayloadFactoryTests.SquareEncodeJpegAsync),
    new("format remote summary without missing style", RemoteInventoryItemTests.FormatSummaryWithoutMissingStyleAsync),
    new("extract sprite sheet frames in row-major order", SpriteSheetFrameExtractorTests.ExtractSpriteSheetFramesInRowMajorOrderAsync),
    new("use power-of-two grid for sparse sprite sheets", SpriteSheetFrameExtractorTests.UsePowerOfTwoGridForSparseSpriteSheetsAsync),
    new("reject invalid sprite sheet metadata", SpriteSheetFrameExtractorTests.RejectInvalidSpriteSheetMetadataAsync),
    new("convert animated GIF to sprite sheet", GifSpriteSheetConverterTests.ConvertAnimatedGifToSpriteSheetAsync),
    new("use power-of-two GIF sprite sheet grid", GifSpriteSheetConverterTests.UsePowerOfTwoSpriteSheetGridAsync),
    new("construct VRChat API requests", VrchatApiClientTests.ConstructRequestsAsync),
    new("reject animated sources for static emoji uploads", VrchatApiClientTests.RejectAnimatedSourcesForStaticEmojiUploadsAsync)
];

int failures = await TestRunner.RunAsync(tests);
if (failures > 0)
{
    Environment.ExitCode = 1;
}

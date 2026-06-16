using VRCInventoryManager.Tests;

TestCase[] tests =
[
    new("scan recursive image files", LocalAssetTests.ScanRecursiveImageFilesAsync),
    new("format local asset details", LocalAssetTests.FormatLocalAssetDetailsAsync),
    new("write debug log file", LocalAssetTests.WriteDebugLogFileAsync),
    new("parse animation style from file name", LocalAssetTests.ParseAnimationStyleFromFileNameAsync),
    new("build folder tree with rollup counts", FolderTreeBuilderTests.BuildFolderTreeWithRollupCountsAsync),
    new("match local assets by folder and query", LocalAssetFilterTests.MatchFolderSubfoldersAndQueryAsync),
    new("resolve configured VRCX and VRChat paths", VrcxPathResolverTests.ResolveConfiguredVrcxAndVrchatPathsAsync),
    new("resolve current-user path fallbacks", VrcxPathResolverTests.ResolveFallbacksFromCurrentUserFoldersAsync),
    new("resolve VRCX database directory override", VrcxPathResolverTests.ResolveVrcxDatabaseDirectoryOverrideAsync),
    new("parse VRCX cookie payload", VrcxCookieTests.ParseCookiePayloadAsync),
    new("load VRCX cookies from sqlite copy", VrcxCookieTests.LoadCookiesFromSqliteCopyAsync),
    new("convert animated GIF to sprite sheet", GifSpriteSheetConverterTests.ConvertAnimatedGifToSpriteSheetAsync),
    new("construct VRChat API requests", VrchatApiClientTests.ConstructRequestsAsync)
];

int failures = await TestRunner.RunAsync(tests);
if (failures > 0)
{
    Environment.ExitCode = 1;
}

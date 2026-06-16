using VRCInventoryManager.Tests;

TestCase[] tests =
[
    new("scan recursive image files", LocalAssetTests.ScanRecursiveImageFilesAsync),
    new("format local asset details", LocalAssetTests.FormatLocalAssetDetailsAsync),
    new("write debug log file", LocalAssetTests.WriteDebugLogFileAsync),
    new("parse animation style from file name", LocalAssetTests.ParseAnimationStyleFromFileNameAsync),
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

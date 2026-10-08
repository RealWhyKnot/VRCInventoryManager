using System.IO;
using VRCInventoryManager.Core;

namespace VRCInventoryManager.Tests;

internal static class UpdateTests
{
    private const string Hash = "fddbb4de15dd180e37c2c8c36880d9f6a666f5744195bd612937fb1af0107052";

    public static Task ParseVersionChannelsAsync()
    {
        TestAssert.Equal(ReleaseChannel.Stable, Parse("v2026.10.8.0").Channel, "plain tag");
        TestAssert.Equal(ReleaseChannel.Stable, Parse("2026.10.8.0+5b7e9d9").Channel, "build metadata is ignored");
        TestAssert.Equal(ReleaseChannel.Beta, Parse("v2026.10.8.1-beta").Channel, "beta suffix");
        TestAssert.Equal(ReleaseChannel.Dev, Parse("2026.10.8.3-AB12").Channel, "local build suffix");
        foreach (string bad in new[] { "", "0.0.0", "v2026.10.8", "2026.10.8.x" })
        {
            TestAssert.False(ReleaseVersion.TryParse(bad, out _), $"rejects '{bad}'");
        }

        TestAssert.True(Parse("2026.10.9.0").CompareTo(Parse("2026.10.8.5")) > 0, "later day wins");
        TestAssert.True(Parse("2026.10.8.0").CompareTo(Parse("2026.10.8.0-beta")) > 0, "stable beats beta of the same numbers");
        return Task.CompletedTask;
    }

    public static Task SelectReleaseByChannelAsync()
    {
        GithubRelease[] releases =
        [
            Release("v2026.10.9.0-beta", prerelease: true),
            Release("v2026.10.10.0", draft: true),
            Release("v2026.10.8.1"),
            Release("v2026.6.18.0"),
            Release("not-a-version"),
        ];

        TestAssert.Equal("v2026.10.8.1", ReleaseSelector.Select(releases, Parse("2026.6.18.0"))?.TagName, "stable skips betas and drafts");
        TestAssert.Equal("v2026.10.9.0-beta", ReleaseSelector.Select(releases, Parse("2026.10.8.1-beta"))?.TagName, "beta takes the newest of either kind");
        TestAssert.Equal(null, ReleaseSelector.Select(releases, Parse("2026.10.8.1"))?.TagName, "nothing newer");
        TestAssert.Equal(null, ReleaseSelector.Select(releases, Parse("2026.6.18.0-AB12"))?.TagName, "dev builds never update");
        return Task.CompletedTask;
    }

    public static Task NameReleaseAssetsAsync()
    {
        TestAssert.Equal("VRCInventoryManager-v2026.6.18.0-Setup.exe", UpdateAssets.SetupName("v2026.6.18.0"), "setup");
        TestAssert.Equal("VRCInventoryManager-v2026.6.18.0-win-x64.zip", UpdateAssets.ZipName("v2026.6.18.0"), "zip");
        TestAssert.Equal("VRCInventoryManager-v2026.6.18.0.integrity.tsv", UpdateAssets.IntegrityName("v2026.6.18.0"), "integrity");
        return Task.CompletedTask;
    }

    public static Task ParseIntegrityManifestAsync()
    {
        string tsv = "\uFEFFname\tsha256\tbytes\r\n"
            + "VRCInventoryManager-v2026.6.18.0-Setup.exe\t" + Hash.ToUpperInvariant() + "\t52480148\r\n"
            + "VRCInventoryManager-v2026.6.18.0-win-x64.zip\t" + Hash + "\t53877591\r\n";

        (string sha256, long bytes) = UpdateAssets.ParseIntegrity(tsv, "VRCInventoryManager-v2026.6.18.0-Setup.exe");
        TestAssert.Equal(Hash, sha256, "hash is lowercased");
        TestAssert.Equal(52480148L, bytes, "size");
        TestAssert.Throws<InvalidDataException>(() => UpdateAssets.ParseIntegrity(tsv, "missing.exe"), "missing row");
        TestAssert.Throws<InvalidDataException>(() => UpdateAssets.ParseIntegrity("a.exe\tabc\t10\n", "a.exe"), "short hash");
        TestAssert.Throws<InvalidDataException>(() => UpdateAssets.ParseIntegrity($"a.exe\t{Hash}\t0\n", "a.exe"), "zero size");
        return Task.CompletedTask;
    }

    public static Task BuildSetupScriptAsync()
    {
        string script = UpdateScript.RunSetup(4242, @"C:\Users\O'Neil\update\Setup.exe", @"C:\Users\O'Neil\update",
            @"C:\Users\O'Neil\AppData\Local\Programs\VRCInventoryManager",
            @"C:\Users\O'Neil\AppData\Local\Programs\VRCInventoryManager\VRCInventoryManager.exe", @"C:\Users\O'Neil\update.log");

        TestAssert.True(script.Contains("Wait-Process -Id 4242 "), "waits for the app");
        TestAssert.True(script.Contains(@"-ArgumentList '/S /D=C:\Users\O''Neil\AppData\Local\Programs\VRCInventoryManager' -Wait"), "silent setup into the install folder");
        TestAssert.False(script.Contains("O'N"), "apostrophes are doubled");
        return Task.CompletedTask;
    }

    private static ReleaseVersion Parse(string text)
    {
        TestAssert.True(ReleaseVersion.TryParse(text, out ReleaseVersion version), $"parses '{text}'");
        return version;
    }

    private static GithubRelease Release(string tag, bool prerelease = false, bool draft = false) =>
        new() { TagName = tag, Prerelease = prerelease, Draft = draft };
}

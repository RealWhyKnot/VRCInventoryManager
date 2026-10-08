using System.Globalization;
using System.Text;
using System.Text.Json.Serialization;

namespace VRCInventoryManager.Core;

public enum ReleaseChannel
{
    Dev,
    Beta,
    Stable,
}

public readonly record struct ReleaseVersion(int Year, int Month, int Day, int Revision, ReleaseChannel Channel)
    : IComparable<ReleaseVersion>
{
    public static bool TryParse(string? text, out ReleaseVersion version)
    {
        version = default;
        if (string.IsNullOrWhiteSpace(text))
        {
            return false;
        }

        string trimmed = text.Trim();
        int plus = trimmed.IndexOf('+');
        if (plus >= 0)
        {
            trimmed = trimmed[..plus];
        }

        if (trimmed.StartsWith('v') || trimmed.StartsWith('V'))
        {
            trimmed = trimmed[1..];
        }

        int dash = trimmed.IndexOf('-');
        string numeric = dash < 0 ? trimmed : trimmed[..dash];
        string suffix = dash < 0 ? string.Empty : trimmed[(dash + 1)..];
        string[] parts = numeric.Split('.');
        if (parts.Length != 4)
        {
            return false;
        }

        int[] values = new int[4];
        for (int i = 0; i < 4; i++)
        {
            if (!int.TryParse(parts[i], NumberStyles.None, CultureInfo.InvariantCulture, out values[i]))
            {
                return false;
            }
        }

        ReleaseChannel channel = suffix.Length == 0 ? ReleaseChannel.Stable
            : suffix.Equals("beta", StringComparison.OrdinalIgnoreCase) ? ReleaseChannel.Beta
            : ReleaseChannel.Dev;
        version = new ReleaseVersion(values[0], values[1], values[2], values[3], channel);
        return true;
    }

    public int CompareTo(ReleaseVersion other)
    {
        int numeric = (Year, Month, Day, Revision).CompareTo((other.Year, other.Month, other.Day, other.Revision));
        return numeric != 0 ? numeric : Channel.CompareTo(other.Channel);
    }

    public override string ToString() => $"{Year}.{Month}.{Day}.{Revision}{(Channel == ReleaseChannel.Beta ? "-beta" : string.Empty)}";
}

public sealed class GithubReleaseAsset
{
    [JsonPropertyName("name")]
    public string Name { get; set; } = string.Empty;

    [JsonPropertyName("browser_download_url")]
    public string BrowserDownloadUrl { get; set; } = string.Empty;
}

public sealed class GithubRelease
{
    [JsonPropertyName("tag_name")]
    public string TagName { get; set; } = string.Empty;

    [JsonPropertyName("html_url")]
    public string HtmlUrl { get; set; } = string.Empty;

    [JsonPropertyName("prerelease")]
    public bool Prerelease { get; set; }

    [JsonPropertyName("draft")]
    public bool Draft { get; set; }

    [JsonPropertyName("assets")]
    public List<GithubReleaseAsset> Assets { get; set; } = [];
}

public static class ReleaseSelector
{
    public static GithubRelease? Select(IEnumerable<GithubRelease> releases, ReleaseVersion current)
    {
        if (current.Channel == ReleaseChannel.Dev)
        {
            return null;
        }

        GithubRelease? best = null;
        ReleaseVersion bestVersion = current;
        foreach (GithubRelease release in releases)
        {
            if (release.Draft || (current.Channel == ReleaseChannel.Stable && release.Prerelease))
            {
                continue;
            }

            if (!ReleaseVersion.TryParse(release.TagName, out ReleaseVersion version)
                || version.Channel == ReleaseChannel.Dev
                || version.CompareTo(bestVersion) <= 0)
            {
                continue;
            }

            best = release;
            bestVersion = version;
        }

        return best;
    }
}

public static class UpdateAssets
{
    public const string ExeName = "VRCInventoryManager.exe";

    public const string UninstallerName = "Uninstall.exe";

    public static string SetupName(string tag) => $"VRCInventoryManager-v{Bare(tag)}-Setup.exe";

    public static string ZipName(string tag) => $"VRCInventoryManager-v{Bare(tag)}-win-x64.zip";

    public static string IntegrityName(string tag) => $"VRCInventoryManager-v{Bare(tag)}.integrity.tsv";

    public static (string Sha256, long Bytes) ParseIntegrity(string tsv, string assetName)
    {
        foreach (string line in tsv.Split('\n'))
        {
            string[] fields = line.TrimEnd('\r').Split('\t');
            if (fields.Length != 3 || !string.Equals(fields[0], assetName, StringComparison.Ordinal))
            {
                continue;
            }

            string hash = fields[1].Trim().ToLowerInvariant();
            if (hash.Length != 64 || !hash.All(Uri.IsHexDigit))
            {
                throw new InvalidDataException($"The checksum for {assetName} is not 64 hex characters.");
            }

            if (!long.TryParse(fields[2], NumberStyles.None, CultureInfo.InvariantCulture, out long bytes) || bytes <= 0)
            {
                throw new InvalidDataException($"The size for {assetName} is not a positive number.");
            }

            return (hash, bytes);
        }

        throw new InvalidDataException($"The checksum file has no row for {assetName}.");
    }

    private static string Bare(string tag) => tag.StartsWith('v') || tag.StartsWith('V') ? tag[1..] : tag;
}

public static class UpdateScript
{
    public static string RunSetup(int processId, string setupPath, string stagingDir, string installDir, string exePath, string logPath)
    {
        var script = new StringBuilder();
        void Line(string text) => script.Append(text).Append("\r\n");

        Line("$ErrorActionPreference = 'Stop'");
        Line("$applied = $false");
        Line("try {");
        Line($"    Wait-Process -Id {processId} -Timeout 300 -ErrorAction SilentlyContinue");
        Line($"    $setup = Start-Process -FilePath {Quote(setupPath)} -ArgumentList {Quote("/S /D=" + installDir)} -Wait -PassThru");
        Line("    if ($setup.ExitCode -ne 0) { throw \"setup exited with code $($setup.ExitCode)\" }");
        Line("    $applied = $true");
        Line("} catch {");
        Line($"    \"$(Get-Date -Format s) update failed\" | Add-Content -LiteralPath {Quote(logPath)}");
        Line($"    $_ | Out-String | Add-Content -LiteralPath {Quote(logPath)}");
        Line("}");
        Line("try {");
        Line($"    Start-Process -FilePath {Quote(exePath)} -WorkingDirectory {Quote(installDir)}");
        Line("} catch {");
        Line($"    $_ | Out-String | Add-Content -LiteralPath {Quote(logPath)}");
        Line("}");
        Line($"Remove-Item -LiteralPath {Quote(stagingDir)} -Recurse -Force -ErrorAction SilentlyContinue");
        Line("if (-not $applied) { exit 1 }");
        return script.ToString();
    }

    private static string Quote(string value) => "'" + value.Replace("'", "''") + "'";
}

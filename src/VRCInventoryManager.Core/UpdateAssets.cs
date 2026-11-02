using System.Globalization;

namespace VRCInventoryManager.Core;

internal static class UpdateAssets
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

            string hash = fields[1].Trim();
            return hash.Length != 64 || !hash.All(Uri.IsHexDigit)
                ? throw new InvalidDataException($"The checksum for {assetName} is not 64 hex characters.")
                : !long.TryParse(fields[2], NumberStyles.None, CultureInfo.InvariantCulture, out long bytes) || bytes <= 0
                ? throw new InvalidDataException($"The size for {assetName} is not a positive number.")
                : ((string Sha256, long Bytes))(Convert.ToHexStringLower(Convert.FromHexString(hash)), bytes);
        }

        throw new InvalidDataException($"The checksum file has no row for {assetName}.");
    }

    private static string Bare(string tag) => tag.StartsWith('v') || tag.StartsWith('V') ? tag[1..] : tag;
}

namespace VRCInventoryManager.Core;

public static class LocalAssetFilter
{
    public static bool Matches(
        LocalAsset asset,
        string? selectedRelativeDirectory,
        bool includeSubfolders,
        string? query)
    {
        string selectedDirectory = NormalizeRelativeDirectory(selectedRelativeDirectory);
        if (!string.IsNullOrEmpty(selectedDirectory))
        {
            string assetDirectory = NormalizeRelativeDirectory(asset.RelativeDirectory);
            bool folderMatches = includeSubfolders
                ? IsSameOrDescendant(assetDirectory, selectedDirectory)
                : string.Equals(assetDirectory, selectedDirectory, StringComparison.OrdinalIgnoreCase);
            if (!folderMatches)
            {
                return false;
            }
        }

        if (string.IsNullOrWhiteSpace(query))
        {
            return true;
        }

        string trimmedQuery = query.Trim();
        return asset.Name.Contains(trimmedQuery, StringComparison.OrdinalIgnoreCase) ||
            asset.Directory.Contains(trimmedQuery, StringComparison.OrdinalIgnoreCase) ||
            asset.RelativeDirectory.Contains(trimmedQuery, StringComparison.OrdinalIgnoreCase);
    }

    public static string NormalizeRelativeDirectory(string? relativeDirectory)
    {
        if (string.IsNullOrWhiteSpace(relativeDirectory) ||
            string.Equals(relativeDirectory, ".", StringComparison.Ordinal))
        {
            return string.Empty;
        }

        return relativeDirectory
            .Replace(Path.AltDirectorySeparatorChar, Path.DirectorySeparatorChar)
            .Trim(Path.DirectorySeparatorChar);
    }

    private static bool IsSameOrDescendant(string assetDirectory, string selectedDirectory) =>
        string.Equals(assetDirectory, selectedDirectory, StringComparison.OrdinalIgnoreCase) ||
        assetDirectory.StartsWith(
            selectedDirectory + Path.DirectorySeparatorChar,
            StringComparison.OrdinalIgnoreCase);
}

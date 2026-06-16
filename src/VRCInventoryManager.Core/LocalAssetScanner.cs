namespace VRCInventoryManager.Core;

public sealed class LocalAssetScanner
{
    private static readonly HashSet<string> SupportedExtensions = new(StringComparer.OrdinalIgnoreCase)
    {
        ".png",
        ".gif",
        ".jpg",
        ".jpeg",
        ".bmp",
        ".tif",
        ".tiff"
    };

    public IReadOnlyList<LocalAsset> Scan(
        string root,
        IReadOnlySet<string>? excludedTopLevelDirectories = null)
    {
        if (string.IsNullOrWhiteSpace(root) || !Directory.Exists(root))
        {
            return [];
        }

        List<LocalAsset> assets = [];
        foreach (string file in EnumerateFiles(root, excludedTopLevelDirectories))
        {
            string extension = Path.GetExtension(file);
            if (!SupportedExtensions.Contains(extension))
            {
                continue;
            }

            try
            {
                FileInfo info = new(file);
                assets.Add(new LocalAsset(
                    info.FullName,
                    info.Name,
                    info.DirectoryName ?? string.Empty,
                    info.Extension,
                    info.Length,
                    info.LastWriteTimeUtc));
            }
            catch (IOException)
            {
            }
            catch (UnauthorizedAccessException)
            {
            }
        }

        return assets
            .OrderByDescending(asset => asset.LastWriteTime)
            .ThenBy(asset => asset.Name, StringComparer.OrdinalIgnoreCase)
            .ToArray();
    }

    private static IEnumerable<string> EnumerateFiles(
        string root,
        IReadOnlySet<string>? excludedTopLevelDirectories)
    {
        Stack<string> pending = new();
        string normalizedRoot = NormalizeDirectory(root);
        pending.Push(normalizedRoot);

        while (pending.Count > 0)
        {
            string directory = pending.Pop();
            string[] files;
            try
            {
                files = Directory.GetFiles(directory);
            }
            catch (IOException)
            {
                continue;
            }
            catch (UnauthorizedAccessException)
            {
                continue;
            }

            foreach (string file in files)
            {
                yield return file;
            }

            string[] children;
            try
            {
                children = Directory.GetDirectories(directory);
            }
            catch (IOException)
            {
                continue;
            }
            catch (UnauthorizedAccessException)
            {
                continue;
            }

            foreach (string child in children)
            {
                if (IsExcludedTopLevelDirectory(normalizedRoot, directory, child, excludedTopLevelDirectories))
                {
                    continue;
                }

                pending.Push(child);
            }
        }
    }

    private static bool IsExcludedTopLevelDirectory(
        string normalizedRoot,
        string directory,
        string child,
        IReadOnlySet<string>? excludedTopLevelDirectories)
    {
        if (excludedTopLevelDirectories is null || excludedTopLevelDirectories.Count == 0)
        {
            return false;
        }

        if (!string.Equals(NormalizeDirectory(directory), normalizedRoot, StringComparison.OrdinalIgnoreCase))
        {
            return false;
        }

        return excludedTopLevelDirectories.Contains(Path.GetFileName(child));
    }

    private static string NormalizeDirectory(string path) =>
        Path.GetFullPath(path).TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar);
}

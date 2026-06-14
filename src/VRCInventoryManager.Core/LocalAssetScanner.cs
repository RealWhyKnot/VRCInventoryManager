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

    public IReadOnlyList<LocalAsset> Scan(string root)
    {
        if (string.IsNullOrWhiteSpace(root) || !Directory.Exists(root))
        {
            return [];
        }

        List<LocalAsset> assets = [];
        foreach (string file in EnumerateFiles(root))
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

    private static IEnumerable<string> EnumerateFiles(string root)
    {
        Stack<string> pending = new();
        pending.Push(root);

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
                pending.Push(child);
            }
        }
    }
}

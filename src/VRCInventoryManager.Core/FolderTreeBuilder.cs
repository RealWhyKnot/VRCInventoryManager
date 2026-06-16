namespace VRCInventoryManager.Core;

public static class FolderTreeBuilder
{
    public static FolderNode Build(IReadOnlyList<LocalAsset> assets, string root)
    {
        FolderAccumulator rootNode = new("All", string.Empty, NormalizeRoot(root));
        foreach (LocalAsset asset in assets)
        {
            string relativeDirectory = LocalAssetFilter.NormalizeRelativeDirectory(asset.RelativeDirectory);
            FolderAccumulator node = rootNode;
            if (string.IsNullOrWhiteSpace(relativeDirectory))
            {
                node.DirectCount++;
                continue;
            }

            string currentRelativePath = string.Empty;
            foreach (string part in relativeDirectory.Split(Path.DirectorySeparatorChar, StringSplitOptions.RemoveEmptyEntries))
            {
                currentRelativePath = string.IsNullOrEmpty(currentRelativePath)
                    ? part
                    : Path.Combine(currentRelativePath, part);
                node = node.GetOrAdd(part, currentRelativePath, Path.Combine(rootNode.FullPath, currentRelativePath));
            }

            node.DirectCount++;
        }

        return rootNode.ToNode();
    }

    private static string NormalizeRoot(string root) =>
        string.IsNullOrWhiteSpace(root)
            ? string.Empty
            : Path.GetFullPath(root).TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar);

    private sealed class FolderAccumulator(string name, string relativePath, string fullPath)
    {
        private readonly Dictionary<string, FolderAccumulator> children = new(StringComparer.OrdinalIgnoreCase);

        public string Name { get; } = name;

        public string RelativePath { get; } = relativePath;

        public string FullPath { get; } = fullPath;

        public int DirectCount { get; set; }

        public FolderAccumulator GetOrAdd(string name, string relativePath, string fullPath)
        {
            if (children.TryGetValue(name, out FolderAccumulator? existing))
            {
                return existing;
            }

            FolderAccumulator created = new(name, relativePath, fullPath);
            children.Add(name, created);
            return created;
        }

        public FolderNode ToNode()
        {
            FolderNode[] childNodes = children.Values
                .OrderByDescending(child => child.Name, StringComparer.OrdinalIgnoreCase)
                .Select(child => child.ToNode())
                .ToArray();
            int totalCount = DirectCount + childNodes.Sum(child => child.TotalCount);
            return new FolderNode(Name, RelativePath, FullPath, DirectCount, totalCount, childNodes);
        }
    }
}

namespace VRCInventoryManager.Core;

internal sealed record FolderNode(
    string Name,
    string RelativePath,
    string FullPath,
    int DirectCount,
    int TotalCount,
    IReadOnlyList<FolderNode> Children);

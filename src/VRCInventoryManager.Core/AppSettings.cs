namespace VRCInventoryManager.Core;

public sealed record AppSettings
{
    public string LocalRoot { get; init; } = string.Empty;

    public bool UpdateCheck { get; init; } = true;

    public string? SkippedUpdate { get; init; }
}

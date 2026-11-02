using System.Text.Json.Serialization;

namespace VRCInventoryManager.Core;

internal sealed record AppSettings
{
    public string LocalRoot { get; init; } = string.Empty;

    [JsonPropertyName("UpdateCheck")]
    public bool IsUpdateCheckEnabled { get; init; } = true;

    public string? SkippedUpdate { get; init; }
}

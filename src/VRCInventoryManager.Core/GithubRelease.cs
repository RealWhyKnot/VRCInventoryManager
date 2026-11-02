using System.Text.Json.Serialization;

namespace VRCInventoryManager.Core;

internal sealed class GithubRelease
{
    [JsonPropertyName("tag_name")]
    public string TagName { get; set; } = string.Empty;

    [JsonPropertyName("html_url")]
    public string HtmlUrl { get; set; } = string.Empty;

    [JsonPropertyName("prerelease")]
    public bool IsPrerelease { get; set; }

    [JsonPropertyName("draft")]
    public bool IsDraft { get; set; }

    [JsonPropertyName("assets")]
    public List<GithubReleaseAsset> Assets { get; set; } = [];
}

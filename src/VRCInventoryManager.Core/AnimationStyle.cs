using System.Text.RegularExpressions;

namespace VRCInventoryManager.Core;

internal static partial class AnimationStyle
{
    public const string Default = "stop";

    private static readonly HashSet<string> AllowedStyles = new(StringComparer.OrdinalIgnoreCase)
    {
        "aura",
        "bats",
        "bees",
        "bounce",
        "cloud",
        "confetti",
        "crying",
        "dislike",
        "fire",
        "idea",
        "lasers",
        "like",
        "magnet",
        "mistletoe",
        "money",
        "noise",
        "orbit",
        "pizza",
        "rain",
        "rotate",
        "shake",
        "snow",
        "snowball",
        "spin",
        "splash",
        "stop",
        "zzz",
    };

    public static string FromFileName(string fileName)
    {
        Match match = FileNameStyleRegex().Match(fileName);
        return !match.Success ? Default : AllowedStyles.TryGetValue(match.Groups["style"].Value, out string? style) ? style : Default;
    }

    public static bool IsAllowed(string style) => AllowedStyles.Contains(style);

    public static IReadOnlyList<string> All => [.. AllowedStyles.Order(StringComparer.OrdinalIgnoreCase)];

    [GeneratedRegex(@"(?<style>[A-Za-z]+)animationStyle", RegexOptions.IgnoreCase | RegexOptions.CultureInvariant, 1000)]
    private static partial Regex FileNameStyleRegex();
}

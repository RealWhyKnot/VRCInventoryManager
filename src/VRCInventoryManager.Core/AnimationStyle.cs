using System.Text.RegularExpressions;

namespace VRCInventoryManager.Core;

public static partial class AnimationStyle
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
        "zzz"
    };

    public static string FromFileName(string fileName)
    {
        Match match = FileNameStyleRegex().Match(fileName);
        if (!match.Success)
        {
            return Default;
        }

        string style = match.Groups["style"].Value.ToLowerInvariant();
        return AllowedStyles.Contains(style) ? style : Default;
    }

    public static bool IsAllowed(string style) => AllowedStyles.Contains(style);

    public static IReadOnlyList<string> All => AllowedStyles.Order(StringComparer.OrdinalIgnoreCase).ToArray();

    [GeneratedRegex(@"(?<style>[A-Za-z]+)animationStyle", RegexOptions.IgnoreCase | RegexOptions.CultureInvariant)]
    private static partial Regex FileNameStyleRegex();
}

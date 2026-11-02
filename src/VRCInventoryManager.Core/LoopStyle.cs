using System.Text.RegularExpressions;

namespace VRCInventoryManager.Core;

internal static partial class LoopStyle
{
    public const string Default = "linear";

    private static readonly HashSet<string> AllowedStyles = new(StringComparer.OrdinalIgnoreCase)
    {
        "linear",
        "pingpong",
    };

    public static string FromFileName(string fileName)
    {
        Match match = FileNameStyleRegex().Match(fileName);
        return !match.Success ? Default : AllowedStyles.TryGetValue(match.Groups["style"].Value, out string? style) ? style : Default;
    }

    public static bool IsAllowed(string style) => AllowedStyles.Contains(style);

    [GeneratedRegex(@"(?<style>[A-Za-z]+)loopStyle", RegexOptions.IgnoreCase | RegexOptions.CultureInvariant, 1000)]
    private static partial Regex FileNameStyleRegex();
}

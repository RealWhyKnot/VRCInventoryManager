using System.Text.RegularExpressions;

namespace VRCInventoryManager.Core;

public static partial class LoopStyle
{
    public const string Default = "linear";

    private static readonly HashSet<string> AllowedStyles = new(StringComparer.OrdinalIgnoreCase)
    {
        "linear",
        "pingpong"
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

    [GeneratedRegex(@"(?<style>[A-Za-z]+)loopStyle", RegexOptions.IgnoreCase | RegexOptions.CultureInvariant)]
    private static partial Regex FileNameStyleRegex();
}

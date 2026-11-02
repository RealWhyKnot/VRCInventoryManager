using System.Globalization;
using System.Runtime.InteropServices;

namespace VRCInventoryManager.Core;

[StructLayout(LayoutKind.Auto)]
internal readonly record struct ReleaseVersion(int Year, int Month, int Day, int Revision, ReleaseChannel Channel)
    : IComparable<ReleaseVersion>
{
    public static bool TryParse(string? text, out ReleaseVersion version)
    {
        version = default;
        if (string.IsNullOrWhiteSpace(text))
        {
            return false;
        }

        string trimmed = text.Trim();
        int plus = trimmed.IndexOf('+', StringComparison.Ordinal);
        if (plus >= 0)
        {
            trimmed = trimmed[..plus];
        }

        if (trimmed.StartsWith('v') || trimmed.StartsWith('V'))
        {
            trimmed = trimmed[1..];
        }

        int dash = trimmed.IndexOf('-', StringComparison.Ordinal);
        string numeric = dash < 0 ? trimmed : trimmed[..dash];
        string suffix = dash < 0 ? string.Empty : trimmed[(dash + 1)..];
        string[] parts = numeric.Split('.');
        if (parts.Length != 4)
        {
            return false;
        }

        int[] values = new int[4];
        for (int i = 0; i < 4; i++)
        {
            if (!int.TryParse(parts[i], NumberStyles.None, CultureInfo.InvariantCulture, out values[i]))
            {
                return false;
            }
        }

        ReleaseChannel channel = suffix.Length == 0 ? ReleaseChannel.Stable
            : suffix.Equals("beta", StringComparison.OrdinalIgnoreCase) ? ReleaseChannel.Beta
            : ReleaseChannel.Dev;
        version = new ReleaseVersion(values[0], values[1], values[2], values[3], channel);
        return true;
    }

    public int CompareTo(ReleaseVersion other)
    {
        int numeric = (Year, Month, Day, Revision).CompareTo((other.Year, other.Month, other.Day, other.Revision));
        return numeric != 0 ? numeric : Channel.CompareTo(other.Channel);
    }

    public override string ToString() => $"{Year}.{Month}.{Day}.{Revision}{(Channel == ReleaseChannel.Beta ? "-beta" : string.Empty)}";

    public static bool operator <(ReleaseVersion left, ReleaseVersion right)
    {
        return Comparer<ReleaseVersion>.Default.Compare(left, right) < 0;
    }

    public static bool operator <=(ReleaseVersion left, ReleaseVersion right)
    {
        return Comparer<ReleaseVersion>.Default.Compare(left, right) <= 0;
    }

    public static bool operator >(ReleaseVersion left, ReleaseVersion right)
    {
        return Comparer<ReleaseVersion>.Default.Compare(left, right) > 0;
    }

    public static bool operator >=(ReleaseVersion left, ReleaseVersion right)
    {
        return Comparer<ReleaseVersion>.Default.Compare(left, right) >= 0;
    }
}

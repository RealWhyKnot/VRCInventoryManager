namespace VRCInventoryManager.Core;

internal static class ReleaseSelector
{
    public static GithubRelease? Select(IEnumerable<GithubRelease> releases, ReleaseVersion current)
    {
        if (current.Channel == ReleaseChannel.Dev)
        {
            return null;
        }

        GithubRelease? best = null;
        ReleaseVersion bestVersion = current;
        foreach (GithubRelease release in releases)
        {
            if (release.IsDraft || (current.Channel == ReleaseChannel.Stable && release.IsPrerelease))
            {
                continue;
            }

            if (!ReleaseVersion.TryParse(release.TagName, out ReleaseVersion version)
                || version.Channel == ReleaseChannel.Dev
                || version.CompareTo(bestVersion) <= 0)
            {
                continue;
            }

            best = release;
            bestVersion = version;
        }

        return best;
    }
}

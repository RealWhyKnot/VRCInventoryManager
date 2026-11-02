using System.Diagnostics;
using System.IO;
using System.IO.Compression;
using System.Net.Http;
using System.Net.Http.Headers;
using System.Reflection;
using System.Security.Cryptography;
using System.Text.Json;
using VRCInventoryManager.Core;

namespace VRCInventoryManager;

internal static class Updater
{
    private const string ReleasesUrl = "https://api.github.com/repos/RealWhyKnot/VRCInventoryManager/releases?per_page=20";

    private static readonly string DataDirectory = Path.Combine(
        Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "VRCInventoryManager");

    private static readonly string StagingDirectory = Path.Combine(DataDirectory, "update");

    private static readonly string LogPath = Path.Combine(DataDirectory, "update.log");

    private static readonly string ExePath = Environment.ProcessPath ?? Path.Combine(AppContext.BaseDirectory, UpdateAssets.ExeName);

    private static readonly string InstallDirectory = Path.GetDirectoryName(ExePath)!;

    public static string VersionText { get; } =
        Assembly.GetEntryAssembly()?.GetCustomAttribute<AssemblyInformationalVersionAttribute>()?.InformationalVersion ?? "0.0.0";

    private static readonly HttpClient Http = CreateClient();

    public static bool IsInstalled => File.Exists(Path.Combine(InstallDirectory, UpdateAssets.UninstallerName));

    public static string? RelaunchPath { get; private set; }

    public static async Task CleanUpAsync(CancellationToken cancellationToken = default)
    {
        for (int attempt = 0; attempt < 20; attempt++)
        {
            try
            {
                File.Delete(ExePath + ".old");
                if (Directory.Exists(StagingDirectory))
                {
                    Directory.Delete(StagingDirectory, recursive: true);
                }

                return;
            }
            catch (IOException)
            {
                await Task.Delay(500, cancellationToken).ConfigureAwait(false);
            }
            catch (UnauthorizedAccessException)
            {
                await Task.Delay(500, cancellationToken).ConfigureAwait(false);
            }
        }
    }

    public static async Task<GithubRelease?> FindAsync(string? skippedTag, CancellationToken cancellationToken = default)
    {
        if (!ReleaseVersion.TryParse(VersionText, out ReleaseVersion current) || current.Channel == ReleaseChannel.Dev)
        {
            return null;
        }

        using var timeout = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
        timeout.CancelAfter(TimeSpan.FromSeconds(30));
        string url = Environment.GetEnvironmentVariable("VRCINVENTORYMANAGER_RELEASES_URL") is { Length: > 0 } custom ? custom : ReleasesUrl;
        string json = await Http.GetStringAsync(new Uri(url), timeout.Token).ConfigureAwait(false);
        List<GithubRelease> releases = JsonSerializer.Deserialize<List<GithubRelease>>(json) ?? [];
        GithubRelease? release = ReleaseSelector.Select(releases, current);
        if (release is null || string.Equals(release.TagName, skippedTag, StringComparison.Ordinal))
        {
            return null;
        }

        string assetName = IsInstalled ? UpdateAssets.SetupName(release.TagName) : UpdateAssets.ZipName(release.TagName);
        bool complete = release.Assets.Any(a => string.Equals(a.Name, assetName, StringComparison.Ordinal))
            && release.Assets.Any(a => string.Equals(a.Name, UpdateAssets.IntegrityName(release.TagName), StringComparison.Ordinal));
        return complete ? release : null;
    }

    public static async Task<string> DownloadAsync(GithubRelease release, IProgress<double> progress, CancellationToken cancellationToken)
    {
        string assetName = IsInstalled ? UpdateAssets.SetupName(release.TagName) : UpdateAssets.ZipName(release.TagName);
        string integrityName = UpdateAssets.IntegrityName(release.TagName);
        GithubReleaseAsset asset = release.Assets.First(a => string.Equals(a.Name, assetName, StringComparison.Ordinal));
        GithubReleaseAsset integrity = release.Assets.First(a => string.Equals(a.Name, integrityName, StringComparison.Ordinal));

        if (Directory.Exists(StagingDirectory))
        {
            Directory.Delete(StagingDirectory, recursive: true);
        }

        Directory.CreateDirectory(StagingDirectory);
        (string sha256, long bytes) = UpdateAssets.ParseIntegrity(
            await Http.GetStringAsync(new Uri(integrity.BrowserDownloadUrl), cancellationToken).ConfigureAwait(false), assetName);

        string path = Path.Combine(StagingDirectory, assetName);
        using (HttpResponseMessage response = await Http.GetAsync(new Uri(asset.BrowserDownloadUrl), HttpCompletionOption.ResponseHeadersRead, cancellationToken).ConfigureAwait(false))
        {
            response.EnsureSuccessStatusCode();
            await using Stream source = await response.Content.ReadAsStreamAsync(cancellationToken).ConfigureAwait(false);
            await using FileStream target = File.Create(path);
            byte[] buffer = new byte[81920];
            long total = 0;
            int lastPercent = -1;
            int read;
            while ((read = await source.ReadAsync(buffer, cancellationToken).ConfigureAwait(false)) > 0)
            {
                await target.WriteAsync(buffer.AsMemory(0, read), cancellationToken).ConfigureAwait(false);
                total += read;
                int percent = (int)Math.Min(100, total * 100 / bytes);
                if (percent != lastPercent)
                {
                    lastPercent = percent;
                    progress.Report(percent / 100.0);
                }
            }
        }

        long actualBytes = new FileInfo(path).Length;
        if (actualBytes != bytes)
        {
            throw new InvalidDataException($"{assetName} is {actualBytes} bytes, expected {bytes}.");
        }

        string actualSha256;
        await using (FileStream stream = File.OpenRead(path))
        {
            actualSha256 = Convert.ToHexStringLower(await SHA256.HashDataAsync(stream, cancellationToken).ConfigureAwait(false));
        }

        return !string.Equals(actualSha256, sha256, StringComparison.Ordinal) ? throw new InvalidDataException($"{assetName} does not match its published checksum.") : path;
    }

    public static void Apply(string downloaded)
    {
        if (IsInstalled)
        {
            string script = Path.Combine(StagingDirectory, "apply.ps1");
            File.WriteAllText(script, UpdateScript.RunSetup(Environment.ProcessId, downloaded, StagingDirectory, InstallDirectory, ExePath, LogPath));
            var helper = new ProcessStartInfo("powershell.exe")
            {
                UseShellExecute = false,
                CreateNoWindow = true,
                WorkingDirectory = Path.GetTempPath(),
            };
            foreach (string argument in new[] { "-NoProfile", "-ExecutionPolicy", "Bypass", "-WindowStyle", "Hidden", "-File", script })
            {
                helper.ArgumentList.Add(argument);
            }

            Process.Start(helper);
            return;
        }

        string fresh = Path.Combine(StagingDirectory, UpdateAssets.ExeName);
        using (ZipArchive zip = ZipFile.OpenRead(downloaded))
        {
            ZipArchiveEntry entry = zip.GetEntry(UpdateAssets.ExeName)
                ?? throw new InvalidDataException($"{UpdateAssets.ExeName} is missing from {Path.GetFileName(downloaded)}.");
            entry.ExtractToFile(fresh, overwrite: true);
        }

        string old = ExePath + ".old";
        File.Move(ExePath, old, overwrite: true);
        try
        {
            File.Move(fresh, ExePath);
        }
        catch (IOException)
        {
            File.Move(old, ExePath);
            throw;
        }
        catch (UnauthorizedAccessException)
        {
            File.Move(old, ExePath);
            throw;
        }

        RelaunchPath = ExePath;
    }

    public static void Relaunch()
    {
        if (RelaunchPath is not null)
        {
            Process.Start(new ProcessStartInfo(RelaunchPath) { UseShellExecute = false, WorkingDirectory = InstallDirectory });
        }
    }

    private static HttpClient CreateClient()
    {
        var client = new HttpClient { Timeout = Timeout.InfiniteTimeSpan };
        client.DefaultRequestHeaders.UserAgent.Add(new ProductInfoHeaderValue("VRCInventoryManager", VersionText.Split('+')[0]));
        client.DefaultRequestHeaders.Accept.Add(new MediaTypeWithQualityHeaderValue("application/vnd.github+json"));
        return client;
    }
}

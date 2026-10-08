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

    private static readonly string DataDir = Path.Combine(
        Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "VRCInventoryManager");

    private static readonly string StagingDir = Path.Combine(DataDir, "update");

    private static readonly string LogPath = Path.Combine(DataDir, "update.log");

    private static readonly string ExePath = Environment.ProcessPath ?? Path.Combine(AppContext.BaseDirectory, UpdateAssets.ExeName);

    private static readonly string InstallDir = Path.GetDirectoryName(ExePath)!;

    public static string VersionText { get; } =
        Assembly.GetEntryAssembly()?.GetCustomAttribute<AssemblyInformationalVersionAttribute>()?.InformationalVersion ?? "0.0.0";

    private static readonly HttpClient Http = CreateClient();

    public static bool Installed => File.Exists(Path.Combine(InstallDir, UpdateAssets.UninstallerName));

    public static string? RelaunchPath { get; private set; }

    public static async Task CleanUpAsync()
    {
        for (int attempt = 0; attempt < 20; attempt++)
        {
            try
            {
                File.Delete(ExePath + ".old");
                if (Directory.Exists(StagingDir))
                {
                    Directory.Delete(StagingDir, recursive: true);
                }

                return;
            }
            catch (Exception)
            {
                await Task.Delay(500).ConfigureAwait(false);
            }
        }
    }

    public static async Task<GithubRelease?> FindAsync(string? skippedTag)
    {
        if (!ReleaseVersion.TryParse(VersionText, out ReleaseVersion current) || current.Channel == ReleaseChannel.Dev)
        {
            return null;
        }

        using var timeout = new CancellationTokenSource(TimeSpan.FromSeconds(30));
        string url = Environment.GetEnvironmentVariable("VRCINVENTORYMANAGER_RELEASES_URL") is { Length: > 0 } custom ? custom : ReleasesUrl;
        string json = await Http.GetStringAsync(url, timeout.Token).ConfigureAwait(false);
        List<GithubRelease> releases = JsonSerializer.Deserialize<List<GithubRelease>>(json) ?? [];
        GithubRelease? release = ReleaseSelector.Select(releases, current);
        if (release is null || string.Equals(release.TagName, skippedTag, StringComparison.Ordinal))
        {
            return null;
        }

        string assetName = Installed ? UpdateAssets.SetupName(release.TagName) : UpdateAssets.ZipName(release.TagName);
        bool complete = release.Assets.Any(a => a.Name == assetName)
            && release.Assets.Any(a => a.Name == UpdateAssets.IntegrityName(release.TagName));
        return complete ? release : null;
    }

    public static async Task<string> DownloadAsync(GithubRelease release, IProgress<double> progress, CancellationToken cancel)
    {
        string assetName = Installed ? UpdateAssets.SetupName(release.TagName) : UpdateAssets.ZipName(release.TagName);
        string integrityName = UpdateAssets.IntegrityName(release.TagName);
        GithubReleaseAsset asset = release.Assets.First(a => a.Name == assetName);
        GithubReleaseAsset integrity = release.Assets.First(a => a.Name == integrityName);

        if (Directory.Exists(StagingDir))
        {
            Directory.Delete(StagingDir, recursive: true);
        }

        Directory.CreateDirectory(StagingDir);
        (string sha256, long bytes) = UpdateAssets.ParseIntegrity(
            await Http.GetStringAsync(integrity.BrowserDownloadUrl, cancel).ConfigureAwait(false), assetName);

        string path = Path.Combine(StagingDir, assetName);
        using (HttpResponseMessage response = await Http.GetAsync(asset.BrowserDownloadUrl, HttpCompletionOption.ResponseHeadersRead, cancel).ConfigureAwait(false))
        {
            response.EnsureSuccessStatusCode();
            await using Stream source = await response.Content.ReadAsStreamAsync(cancel).ConfigureAwait(false);
            await using FileStream target = File.Create(path);
            byte[] buffer = new byte[81920];
            long total = 0;
            int lastPercent = -1;
            int read;
            while ((read = await source.ReadAsync(buffer, cancel).ConfigureAwait(false)) > 0)
            {
                await target.WriteAsync(buffer.AsMemory(0, read), cancel).ConfigureAwait(false);
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
            actualSha256 = Convert.ToHexStringLower(await SHA256.HashDataAsync(stream, cancel).ConfigureAwait(false));
        }

        if (actualSha256 != sha256)
        {
            throw new InvalidDataException($"{assetName} does not match its published checksum.");
        }

        return path;
    }

    public static void Apply(string downloaded)
    {
        if (Installed)
        {
            string script = Path.Combine(StagingDir, "apply.ps1");
            File.WriteAllText(script, UpdateScript.RunSetup(Environment.ProcessId, downloaded, StagingDir, InstallDir, ExePath, LogPath));
            var helper = new ProcessStartInfo("powershell.exe")
            {
                UseShellExecute = false,
                CreateNoWindow = true,
                WorkingDirectory = Path.GetTempPath(),
            };
            foreach (string arg in new[] { "-NoProfile", "-ExecutionPolicy", "Bypass", "-WindowStyle", "Hidden", "-File", script })
            {
                helper.ArgumentList.Add(arg);
            }

            Process.Start(helper);
            return;
        }

        string fresh = Path.Combine(StagingDir, UpdateAssets.ExeName);
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
        catch (Exception)
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
            Process.Start(new ProcessStartInfo(RelaunchPath) { UseShellExecute = false, WorkingDirectory = InstallDir });
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

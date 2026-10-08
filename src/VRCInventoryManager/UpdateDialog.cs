using System.Diagnostics;
using VRCInventoryManager.Core;
using TaskDialog = System.Windows.Forms.TaskDialog;
using TaskDialogButton = System.Windows.Forms.TaskDialogButton;
using TaskDialogFootnote = System.Windows.Forms.TaskDialogFootnote;
using TaskDialogIcon = System.Windows.Forms.TaskDialogIcon;
using TaskDialogPage = System.Windows.Forms.TaskDialogPage;
using TaskDialogProgressBar = System.Windows.Forms.TaskDialogProgressBar;

namespace VRCInventoryManager;

internal enum UpdateAnswer
{
    Later,
    Skip,
    Downloaded,
}

internal static class UpdateDialog
{
    public static UpdateAnswer Show(IntPtr owner, GithubRelease release, out string? downloaded)
    {
        System.Windows.Forms.Application.EnableVisualStyles();
        ReleaseVersion.TryParse(release.TagName, out ReleaseVersion version);
        ReleaseVersion.TryParse(Updater.VersionText, out ReleaseVersion current);

        var update = new TaskDialogButton("Update now") { AllowCloseDialog = false };
        var skip = new TaskDialogButton("Skip this version");
        var later = new TaskDialogButton("Later");
        var available = new TaskDialogPage
        {
            Caption = "VRCInventoryManager",
            Heading = $"Version {version} is available",
            Text = $"You have {current}. Updating closes VRCInventoryManager, installs the new version and opens it again.",
            Footnote = new TaskDialogFootnote($"<a href=\"{release.HtmlUrl}\">What's new in {version}</a>"),
            EnableLinks = true,
            Buttons = { update, skip, later },
            DefaultButton = update,
        };
        available.LinkClicked += (_, e) => Process.Start(new ProcessStartInfo(e.LinkHref) { UseShellExecute = true });

        var bar = new TaskDialogProgressBar { Minimum = 0, Maximum = 100 };
        var downloading = new TaskDialogPage
        {
            Caption = "VRCInventoryManager",
            Heading = $"Downloading {version}",
            Text = "VRCInventoryManager closes and opens again when the download finishes.",
            ProgressBar = bar,
            Buttons = { TaskDialogButton.Cancel },
        };

        var failed = new TaskDialogPage
        {
            Caption = "VRCInventoryManager",
            Heading = "The update didn't download",
            Icon = TaskDialogIcon.Warning,
            Buttons = { TaskDialogButton.Close },
        };

        string? path = null;
        using var cancel = new CancellationTokenSource();
        downloading.Destroyed += (_, _) => cancel.Cancel();
        update.Click += async (_, _) =>
        {
            available.Navigate(downloading);
            try
            {
                path = await Updater.DownloadAsync(release, new Progress<double>(f => bar.Value = (int)(f * 100)), cancel.Token);
                downloading.BoundDialog?.Close();
            }
            catch (Exception ex) when (!cancel.IsCancellationRequested)
            {
                App.Log.Error("Update download failed.", ex);
                failed.Text = $"{ex.Message}\n\nYou're still on {current}.";
                downloading.Navigate(failed);
            }
            catch (Exception)
            {
            }
        };

        TaskDialogButton answer = TaskDialog.ShowDialog(owner, available);
        downloaded = path;
        return path is not null ? UpdateAnswer.Downloaded
            : answer == skip ? UpdateAnswer.Skip
            : UpdateAnswer.Later;
    }
}

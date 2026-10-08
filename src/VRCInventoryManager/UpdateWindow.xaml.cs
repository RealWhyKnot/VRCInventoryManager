using System.Diagnostics;
using System.Windows;
using VRCInventoryManager.Core;

namespace VRCInventoryManager;

internal enum UpdateAnswer
{
    Later,
    Skip,
    Downloaded,
}

internal partial class UpdateWindow : Window
{
    private readonly GithubRelease release;
    private readonly ReleaseVersion current;
    private readonly CancellationTokenSource cancel = new();

    public UpdateWindow(GithubRelease release)
    {
        InitializeComponent();
        this.release = release;
        ReleaseVersion.TryParse(release.TagName, out ReleaseVersion version);
        ReleaseVersion.TryParse(Updater.VersionText, out current);
        HeadingText.Text = $"Version {version} is available";
        BodyText.Text = $"You have {current}. Updating closes VRCInventoryManager, installs the new version and opens it again.";
        NotesText.Text = $"What's new in {version}";
    }

    public UpdateAnswer Answer { get; private set; } = UpdateAnswer.Later;

    public string? Downloaded { get; private set; }

    protected override void OnSourceInitialized(EventArgs e)
    {
        base.OnSourceInitialized(e);
        MainWindow.EnableDarkTitleBar(this);
    }

    protected override void OnClosed(EventArgs e)
    {
        cancel.Cancel();
        base.OnClosed(e);
    }

    private async void Update_Click(object sender, RoutedEventArgs e)
    {
        ReleaseVersion.TryParse(release.TagName, out ReleaseVersion version);
        HeadingText.Text = $"Downloading {version}";
        BodyText.Text = "VRCInventoryManager closes and opens again when the download finishes.";
        DownloadBar.Visibility = Visibility.Visible;
        NotesLink.Visibility = Visibility.Collapsed;
        UpdateButton.Visibility = Visibility.Collapsed;
        SkipButton.Visibility = Visibility.Collapsed;
        LaterButton.Content = "Cancel";
        try
        {
            Downloaded = await Updater.DownloadAsync(release, new Progress<double>(f => DownloadBar.Value = f * 100), cancel.Token);
            Answer = UpdateAnswer.Downloaded;
            Close();
        }
        catch (Exception ex) when (!cancel.IsCancellationRequested)
        {
            App.Log.Error("Update download failed.", ex);
            HeadingText.Text = "The update didn't download";
            HeadingText.Foreground = (System.Windows.Media.Brush)FindResource("WarningBrush");
            BodyText.Text = $"{ex.Message}\n\nYou're still on {current}.";
            DownloadBar.Visibility = Visibility.Collapsed;
            LaterButton.Content = "Close";
        }
        catch (Exception)
        {
        }
    }

    private void Skip_Click(object sender, RoutedEventArgs e)
    {
        Answer = UpdateAnswer.Skip;
        Close();
    }

    private void Later_Click(object sender, RoutedEventArgs e) => Close();

    private void Notes_Click(object sender, RoutedEventArgs e) =>
        Process.Start(new ProcessStartInfo(release.HtmlUrl) { UseShellExecute = true });
}

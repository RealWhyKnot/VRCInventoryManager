using System.Diagnostics;
using System.Windows;
using VRCInventoryManager.Core;

namespace VRCInventoryManager;

internal sealed partial class UpdateWindow : Window, IDisposable
{
    private readonly GithubRelease _release;
    private readonly ReleaseVersion _current;
    private readonly CancellationTokenSource _cancel = new();

    public UpdateWindow(GithubRelease release)
    {
        InitializeComponent();
        _release = release;
        ReleaseVersion version = ReleaseVersion.TryParse(release.TagName, out ReleaseVersion parsed) ? parsed : default;
        _current = ReleaseVersion.TryParse(Updater.VersionText, out ReleaseVersion current) ? current : default;
        HeadingText.Text = $"Version {version} is available";
        BodyText.Text = $"You have {_current}. Updating closes VRCInventoryManager, installs the new version and opens it again.";
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
        _cancel.Cancel();
        base.OnClosed(e);
    }

    private async void Update_Click(object sender, RoutedEventArgs e)
    {
        ReleaseVersion version = ReleaseVersion.TryParse(_release.TagName, out ReleaseVersion parsed) ? parsed : default;
        HeadingText.Text = $"Downloading {version}";
        BodyText.Text = "VRCInventoryManager closes and opens again when the download finishes.";
        DownloadBar.Visibility = Visibility.Visible;
        NotesLink.Visibility = Visibility.Collapsed;
        UpdateButton.Visibility = Visibility.Collapsed;
        SkipButton.Visibility = Visibility.Collapsed;
        LaterButton.Content = "Cancel";
        try
        {
            Downloaded = await Updater.DownloadAsync(_release, new Progress<double>(f => DownloadBar.Value = f * 100), _cancel.Token);
            Answer = UpdateAnswer.Downloaded;
            Close();
        }
        catch (System.IO.IOException exception)
        {
            ShowDownloadError(exception);
        }
        catch (UnauthorizedAccessException exception)
        {
            ShowDownloadError(exception);
        }
        catch (System.Net.Http.HttpRequestException exception)
        {
            ShowDownloadError(exception);
        }
        catch (InvalidOperationException exception)
        {
            ShowDownloadError(exception);
        }
        catch (OperationCanceledException exception)
        {
            ShowDownloadError(exception);
        }
    }

    public void Dispose() => _cancel.Dispose();

    private void ShowDownloadError(Exception exception)
    {
        if (_cancel.IsCancellationRequested)
        {
            return;
        }

        App.Log.Error("Update download failed.", exception);
        HeadingText.Text = "The update didn't download";
        HeadingText.Foreground = (System.Windows.Media.Brush)FindResource("WarningBrush");
        BodyText.Text = $"{exception.Message}\n\nYou're still on {_current}.";
        DownloadBar.Visibility = Visibility.Collapsed;
        LaterButton.Content = "Close";

    }

    private void Skip_Click(object sender, RoutedEventArgs e)
    {
        Answer = UpdateAnswer.Skip;
        Close();
    }

    private void Later_Click(object sender, RoutedEventArgs e) => Close();

    private void Notes_Click(object sender, RoutedEventArgs e) =>
        Process.Start(new ProcessStartInfo(_release.HtmlUrl) { UseShellExecute = true });
}

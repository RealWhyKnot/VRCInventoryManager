using System.Collections.ObjectModel;
using System.Net.Http;
using System.Windows;
using System.Windows.Threading;
using VRCInventoryManager.Core;

namespace VRCInventoryManager;

public partial class MainWindow : Window
{
    private readonly ObservableCollection<RemoteInventoryViewModel> remoteItems = [];
    private readonly LocalAssetScanner scanner = new();
    private readonly AppSettingsStore settingsStore = new();
    private readonly VrcxCookieProvider cookieProvider = new();
    private readonly ImageThumbnailLoader thumbnailLoader = new();
    private readonly DispatcherTimer searchTimer = new() { Interval = TimeSpan.FromMilliseconds(250) };
    private KnownFolderPaths localFolders = KnownFolders.Resolve();

    private List<LocalAsset> allAssets = [];
    private List<LocalAssetViewModel> filteredAssets = [];
    private IReadOnlyList<FolderNodeViewModel> folderNodes = [];
    private FolderNodeViewModel? selectedFolderNode;
    private AppSettings settings = new();
    private VrchatApiClient? apiClient;
    private HttpClient? httpClient;
    private bool loaded;
    private bool updatingFolderTree;
    private bool suppressLocalSelectionChanged;
    private bool suppressRemoteSelectionChanged;
    private int busyDepth;
    private int stickerCount = -1;
    private int emojiCount = -1;
    private GithubRelease? pendingUpdate;

    public MainWindow()
    {
        InitializeComponent();
        LocalAssetList.ItemsSource = filteredAssets;
        RemoteFileList.ItemsSource = remoteItems;
        searchTimer.Tick += SearchTimer_Tick;
        previewTimer.Tick += PreviewTimer_Tick;
        previewSelectionTimer.Tick += PreviewSelectionTimer_Tick;
        Loaded += MainWindow_Loaded;
        Closing += (_, _) =>
        {
            searchTimer.Stop();
            previewTimer.Stop();
            previewSelectionTimer.Stop();
            CancelPreviewLoad();
            httpClient?.Dispose();
            thumbnailLoader.Dispose();
        };
    }

    protected override void OnSourceInitialized(EventArgs e)
    {
        base.OnSourceInitialized(e);
        EnableDarkTitleBar();
    }

    private async void MainWindow_Loaded(object sender, RoutedEventArgs e)
    {
        if (loaded)
        {
            return;
        }

        try
        {
            settings = settingsStore.Load();
            ConfigureFolders();
            SelectInitialFolder(GetInitialLocalRoot());
            loaded = true;
            ClearPreview();
            UpdateButtons();
            _ = CheckForUpdateAsync();
            await RefreshLocalAsync();
            await ConnectAsync();
        }
        catch (Exception ex)
        {
            App.Log.Error("Startup failed.", ex);
            HeaderStatusText.Text = "Startup failed.";
            ActionStatusText.Text = ex.Message;
        }
    }

    private async Task CheckForUpdateAsync()
    {
        try
        {
            await Updater.CleanUpAsync();
            if (!settings.UpdateCheck)
            {
                return;
            }

            pendingUpdate = await Updater.FindAsync(settings.SkippedUpdate);
        }
        catch (Exception ex)
        {
            App.Log.Error("Update check failed.", ex);
            return;
        }

        OfferUpdate();
    }

    private void OfferUpdate()
    {
        if (pendingUpdate is not { } release || busyDepth > 0 || !IsLoaded)
        {
            return;
        }

        pendingUpdate = null;
        IntPtr owner = new System.Windows.Interop.WindowInteropHelper(this).Handle;
        switch (UpdateDialog.Show(owner, release, out string? downloaded))
        {
            case UpdateAnswer.Skip:
                settings = settings with { SkippedUpdate = release.TagName };
                settingsStore.Save(settings);
                break;
            case UpdateAnswer.Downloaded:
                try
                {
                    Updater.Apply(downloaded!);
                }
                catch (Exception ex)
                {
                    App.Log.Error("Update install failed.", ex);
                    MessageBox.Show(this, $"Could not install the update: {ex.Message}", "VRCInventoryManager",
                        MessageBoxButton.OK, MessageBoxImage.Warning);
                    return;
                }

                Close();
                break;
        }
    }
}

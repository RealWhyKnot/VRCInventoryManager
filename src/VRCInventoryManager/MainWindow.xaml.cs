using System.Collections.ObjectModel;
using System.Net.Http;
using System.Windows;
using VRCInventoryManager.Core;

namespace VRCInventoryManager;

public partial class MainWindow : Window
{
    private readonly ObservableCollection<LocalAssetViewModel> filteredAssets = [];
    private readonly ObservableCollection<RemoteInventoryViewModel> remoteItems = [];
    private readonly LocalAssetScanner scanner = new();
    private readonly AppSettingsStore settingsStore = new();
    private readonly VrcxCookieProvider cookieProvider = new();
    private readonly ImageThumbnailLoader thumbnailLoader = new();

    private List<LocalAsset> allAssets = [];
    private AppSettings settings = new();
    private VrchatApiClient? apiClient;
    private HttpClient? httpClient;
    private bool loaded;
    private bool updatingBuckets;
    private int busyDepth;
    private int stickerCount = -1;
    private int emojiCount = -1;

    public MainWindow()
    {
        InitializeComponent();
        LocalAssetList.ItemsSource = filteredAssets;
        RemoteFileList.ItemsSource = remoteItems;
        previewTimer.Tick += PreviewTimer_Tick;
        Loaded += MainWindow_Loaded;
        Closing += (_, _) =>
        {
            previewTimer.Stop();
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
            SelectInitialFolder(settings.LocalRoot);
            loaded = true;
            ClearPreview();
            UpdateButtons();
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
}

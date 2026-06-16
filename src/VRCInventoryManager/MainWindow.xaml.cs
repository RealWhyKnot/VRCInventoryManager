using System.Collections.ObjectModel;
using System.Diagnostics;
using System.IO;
using System.Net.Http;
using System.Runtime.InteropServices;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Interop;
using System.Windows.Input;
using System.Windows.Media.Imaging;
using System.Windows.Threading;
using Microsoft.Win32;
using WpfCursors = System.Windows.Input.Cursors;
using WpfMessageBox = System.Windows.MessageBox;
using VRCInventoryManager.Core;

namespace VRCInventoryManager;

public partial class MainWindow : Window
{
    private readonly ObservableCollection<LocalAssetViewModel> filteredAssets = [];
    private readonly ObservableCollection<RemoteInventoryViewModel> remoteItems = [];
    private readonly LocalAssetScanner scanner = new();
    private readonly AppSettingsStore settingsStore = new();
    private readonly VrcxCookieProvider cookieProvider = new();
    private readonly DispatcherTimer previewTimer = new();
    private readonly ImageThumbnailLoader thumbnailLoader = new();

    private List<LocalAsset> allAssets = [];
    private List<BitmapSource> previewFrames = [];
    private List<TimeSpan> previewFrameDelays = [];
    private AppSettings settings = new();
    private VrchatApiClient? apiClient;
    private HttpClient? httpClient;
    private bool loaded;
    private bool updatingBuckets;
    private int busyDepth;
    private int previewFrameIndex;
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

    private void ConfigureFolders()
    {
        FolderCombo.ItemsSource = new[]
        {
            new FolderChoice("Photos", KnownFolders.DefaultPhotosFolder),
            new FolderChoice("Emoji", KnownFolders.DefaultEmojiFolder),
            new FolderChoice("Stickers", KnownFolders.DefaultStickersFolder),
            new FolderChoice("Prints", KnownFolders.DefaultPrintsFolder),
            new FolderChoice("Custom", settings.LocalRoot)
        };
        FolderCombo.DisplayMemberPath = nameof(FolderChoice.Name);
    }

    private void SelectInitialFolder(string folder)
    {
        foreach (FolderChoice choice in FolderCombo.Items.OfType<FolderChoice>())
        {
            if (string.Equals(choice.Path, folder, StringComparison.OrdinalIgnoreCase))
            {
                FolderCombo.SelectedItem = choice;
                return;
            }
        }

        FolderCombo.SelectedIndex = 0;
    }

    private async Task RefreshLocalAsync()
    {
        string folder = CurrentFolder;
        Stopwatch stopwatch = Stopwatch.StartNew();
        SetBusy(true);
        try
        {
            CurrentFolderText.Text = folder;
            ActionStatusText.Text = "Scanning...";
            IReadOnlySet<string>? excludedFolders = IsDefaultPhotosFolder(folder) ? KnownFolders.InventoryFolderNames : null;
            allAssets = await Task.Run(() => scanner.Scan(folder, excludedFolders).ToList());
            ConfigureBuckets();
            ApplyFilter();
            ActionStatusText.Text = allAssets.Count == 0 ? "No local images found." : "Ready.";
            settings = settings with { LocalRoot = folder };
            settingsStore.Save(settings);
            App.Log.Info($"Scanned local folder '{folder}' and found {allAssets.Count} images in {stopwatch.ElapsedMilliseconds} ms.");
        }
        catch (Exception ex)
        {
            allAssets = [];
            ApplyFilter();
            ActionStatusText.Text = "Local scan failed.";
            App.Log.Error($"Local scan failed for '{folder}'.", ex);
        }
        finally
        {
            SetBusy(false);
        }
    }

    private async Task ConnectAsync()
    {
        try
        {
            SetBusy(true);
            App.Log.Info("Connecting through VRCX cookie store.");
            ConnectionStatusText.Text = "Connecting through VRCX...";
            VrcxAuthSession session = await Task.Run(cookieProvider.LoadDefaultSession);
            thumbnailLoader.ConfigureRemoteAuth(session.Cookies, session.UserAgent);
            httpClient?.Dispose();
            httpClient = new HttpClient();
            apiClient = new VrchatApiClient(httpClient, session.Cookies, session.UserAgent);

            bool authenticated = await apiClient.CheckAuthAsync();
            if (!authenticated)
            {
                DisconnectRemote();
                ConnectionStatusText.Text = "VRCX cookies were found, but VRChat rejected the session.";
                App.Log.Warning("VRChat rejected the VRCX session.");
                return;
            }

            await apiClient.FetchConfigAsync();
            ConnectionStatusText.Text = "Connected through VRCX.";
            App.Log.Info("Connected through VRCX.");
            await RefreshRemoteAsync();
        }
        catch (Exception ex)
        {
            DisconnectRemote();
            ConnectionStatusText.Text = $"Disconnected: {ex.Message}";
            App.Log.Error("VRCX connection failed.", ex);
        }
        finally
        {
            SetBusy(false);
            UpdateButtons();
        }
    }

    private async Task RefreshRemoteAsync()
    {
        if (apiClient is null)
        {
            ConnectionStatusText.Text = "Disconnected. Reconnect to load VRChat inventory.";
            return;
        }

        SetBusy(true);
        try
        {
            RemoteInventorySnapshot snapshot = await apiClient.GetInventorySnapshotAsync();
            stickerCount = snapshot.StickerCount;
            emojiCount = snapshot.EmojiCount;
            remoteItems.Clear();
            foreach (RemoteInventoryItem item in snapshot.AllItems)
            {
                remoteItems.Add(new RemoteInventoryViewModel(item));
            }

            RemoteCountText.Text = $"Stickers {snapshot.StickerCount}/{RemoteInventorySnapshot.StickerLimit}  Emojis {snapshot.EmojiCount}/{RemoteInventorySnapshot.EmojiLimit}";
            RemoteEmptyText.Visibility = remoteItems.Count == 0 ? Visibility.Visible : Visibility.Collapsed;
            App.Log.Info($"Loaded remote inventory: {snapshot.StickerCount} stickers, {snapshot.EmojiCount} emojis.");
        }
        catch (Exception ex)
        {
            ConnectionStatusText.Text = $"Inventory refresh failed: {ex.Message}";
            App.Log.Error("Remote inventory refresh failed.", ex);
        }
        finally
        {
            SetBusy(false);
            UpdateButtons();
        }
    }

    private void ApplyFilter()
    {
        LocalAsset? previousSelection = (LocalAssetList.SelectedItem as LocalAssetViewModel)?.Asset;
        string query = SearchBox.Text.Trim();
        IEnumerable<LocalAsset> assets = allAssets;
        if (MonthCombo.SelectedItem is BucketChoice bucketChoice && !string.IsNullOrWhiteSpace(bucketChoice.Bucket))
        {
            assets = assets.Where(asset => string.Equals(asset.Bucket, bucketChoice.Bucket, StringComparison.OrdinalIgnoreCase));
        }

        if (!string.IsNullOrWhiteSpace(query))
        {
            assets = assets.Where(asset =>
                asset.Name.Contains(query, StringComparison.OrdinalIgnoreCase) ||
                asset.Directory.Contains(query, StringComparison.OrdinalIgnoreCase));
        }

        filteredAssets.Clear();
        foreach (LocalAsset asset in assets)
        {
            filteredAssets.Add(new LocalAssetViewModel(asset));
        }

        LocalCountText.Text = $"{filteredAssets.Count:N0} files";
        LocalEmptyText.Visibility = filteredAssets.Count == 0 ? Visibility.Visible : Visibility.Collapsed;

        if (filteredAssets.Count == 0)
        {
            ClearPreview();
            return;
        }

        LocalAssetViewModel? nextSelection = previousSelection is null
            ? null
            : filteredAssets.FirstOrDefault(asset => string.Equals(asset.Asset.Path, previousSelection.Path, StringComparison.OrdinalIgnoreCase));
        LocalAssetList.SelectedItem = nextSelection ?? filteredAssets[0];
    }

    private void LoadPreview(LocalAsset? asset)
    {
        if (asset is null)
        {
            ClearPreview();
            return;
        }

        StopPreviewAnimation();
        SelectedFileTitle.Text = asset.Name;
        SelectedFileText.Text = $"{asset.SizeText}  style {asset.AnimationStyle}";
        PreviewEmptyText.Visibility = Visibility.Collapsed;
        PreviewImage.Visibility = Visibility.Visible;

        try
        {
            if (asset.IsGif)
            {
                LoadAnimatedPreview(asset.Path);
            }
            else
            {
                PreviewImage.Source = LoadBitmap(asset.Path);
            }
        }
        catch (Exception ex)
        {
            ClearPreview();
            SelectedFileTitle.Text = asset.Name;
            SelectedFileText.Text = "Preview failed.";
            App.Log.Error($"Preview failed for '{asset.Path}'.", ex);
        }
    }

    private async Task UploadSelectedAsync(Func<VrchatApiClient, LocalAsset, Task<UploadResult>> upload)
    {
        if (apiClient is null)
        {
            WpfMessageBox.Show(this, "Connect to VRCX before uploading.", "Not connected", MessageBoxButton.OK, MessageBoxImage.Information);
            return;
        }

        if (LocalAssetList.SelectedItem is not LocalAssetViewModel selected)
        {
            return;
        }

        LocalAsset asset = selected.Asset;
        SetBusy(true);
        try
        {
            ActionStatusText.Text = "Uploading...";
            App.Log.Info($"Uploading '{asset.Name}'.");
            UploadResult result = await upload(apiClient, asset);
            ActionStatusText.Text = $"Uploaded {result.Id}";
            App.Log.Info($"Upload completed: {result.Id}.");
            await RefreshRemoteAsync();
        }
        catch (Exception ex)
        {
            ActionStatusText.Text = "Upload failed.";
            App.Log.Error("Upload failed.", ex);
            WpfMessageBox.Show(this, ex.Message, "Upload failed", MessageBoxButton.OK, MessageBoxImage.Error);
        }
        finally
        {
            SetBusy(false);
            UpdateButtons();
        }
    }

    private void UpdateButtons()
    {
        bool hasAsset = LocalAssetList.SelectedItem is LocalAssetViewModel;
        bool hasGif = LocalAssetList.SelectedItem is LocalAssetViewModel selected && selected.Asset.IsGif;
        bool connected = apiClient is not null;
        bool stickerHasCapacity = stickerCount < 0 || stickerCount < RemoteInventorySnapshot.StickerLimit;
        bool emojiHasCapacity = emojiCount < 0 || emojiCount < RemoteInventorySnapshot.EmojiLimit;
        UploadStickerButton.IsEnabled = hasAsset && connected && stickerHasCapacity;
        UploadEmojiButton.IsEnabled = hasAsset && connected && emojiHasCapacity;
        UploadAnimatedEmojiButton.IsEnabled = hasGif && connected && emojiHasCapacity;
        DeleteRemoteButton.IsEnabled = RemoteFileList.SelectedItem is RemoteInventoryViewModel && connected;
        RemoteEmptyText.Visibility = remoteItems.Count == 0 ? Visibility.Visible : Visibility.Collapsed;
        LocalEmptyText.Visibility = filteredAssets.Count == 0 ? Visibility.Visible : Visibility.Collapsed;
    }

    private void SetBusy(bool isBusy)
    {
        busyDepth = isBusy ? busyDepth + 1 : Math.Max(0, busyDepth - 1);
        bool active = busyDepth > 0;
        Cursor = active ? WpfCursors.Wait : WpfCursors.Arrow;
        BusyBar.Visibility = active ? Visibility.Visible : Visibility.Collapsed;
        HeaderStatusText.Text = active ? "Working..." : "Ready";
    }

    private string CurrentFolder => FolderCombo.SelectedItem is FolderChoice choice ? choice.Path : settings.LocalRoot;

    private async void FolderCombo_SelectionChanged(object sender, SelectionChangedEventArgs e)
    {
        if (loaded)
        {
            await RefreshLocalAsync();
        }
    }

    private async void Browse_Click(object sender, RoutedEventArgs e)
    {
        OpenFolderDialog dialog = new()
        {
            Title = "Choose a VRChat image folder",
            InitialDirectory = Directory.Exists(CurrentFolder) ? CurrentFolder : KnownFolders.DefaultEmojiFolder
        };

        if (dialog.ShowDialog(this) == true)
        {
            FolderChoice custom = new("Custom", dialog.FolderName);
            FolderCombo.ItemsSource = new[]
            {
                new FolderChoice("Photos", KnownFolders.DefaultPhotosFolder),
                new FolderChoice("Emoji", KnownFolders.DefaultEmojiFolder),
                new FolderChoice("Stickers", KnownFolders.DefaultStickersFolder),
                new FolderChoice("Prints", KnownFolders.DefaultPrintsFolder),
                custom
            };
            FolderCombo.DisplayMemberPath = nameof(FolderChoice.Name);
            FolderCombo.SelectedItem = custom;
            await RefreshLocalAsync();
        }
    }

    private async void RefreshLocal_Click(object sender, RoutedEventArgs e) => await RefreshLocalAsync();

    private void SearchBox_TextChanged(object sender, TextChangedEventArgs e) => ApplyFilter();

    private void MonthCombo_SelectionChanged(object sender, SelectionChangedEventArgs e)
    {
        if (loaded && !updatingBuckets)
        {
            ApplyFilter();
        }
    }

    private void LocalAssetList_SelectionChanged(object sender, SelectionChangedEventArgs e)
    {
        LoadPreview((LocalAssetList.SelectedItem as LocalAssetViewModel)?.Asset);
        UpdateButtons();
    }

    private async void LocalThumbnail_Loaded(object sender, RoutedEventArgs e)
    {
        if (sender is FrameworkElement { DataContext: LocalAssetViewModel asset })
        {
            await asset.EnsureThumbnailAsync(thumbnailLoader);
        }
    }

    private async void LocalThumbnail_DataContextChanged(object sender, DependencyPropertyChangedEventArgs e)
    {
        if (e.NewValue is LocalAssetViewModel asset)
        {
            await asset.EnsureThumbnailAsync(thumbnailLoader);
        }
    }

    private async void RemoteThumbnail_Loaded(object sender, RoutedEventArgs e)
    {
        if (sender is FrameworkElement { DataContext: RemoteInventoryViewModel item })
        {
            await item.EnsureThumbnailAsync(thumbnailLoader);
        }
    }

    private async void RemoteThumbnail_DataContextChanged(object sender, DependencyPropertyChangedEventArgs e)
    {
        if (e.NewValue is RemoteInventoryViewModel item)
        {
            await item.EnsureThumbnailAsync(thumbnailLoader);
        }
    }

    private async void Reconnect_Click(object sender, RoutedEventArgs e) => await ConnectAsync();

    private async void RefreshRemote_Click(object sender, RoutedEventArgs e) => await RefreshRemoteAsync();

    private async void UploadSticker_Click(object sender, RoutedEventArgs e)
    {
        await UploadSelectedAsync((client, asset) => client.UploadStickerAsync(asset.Path));
    }

    private async void UploadEmoji_Click(object sender, RoutedEventArgs e)
    {
        await UploadSelectedAsync((client, asset) => client.UploadStaticEmojiAsync(asset.Path, asset.AnimationStyle));
    }

    private async void UploadAnimatedEmoji_Click(object sender, RoutedEventArgs e)
    {
        await UploadSelectedAsync((client, asset) => client.UploadAnimatedEmojiAsync(asset.Path, asset.AnimationStyle));
    }

    private void RemoteFileList_SelectionChanged(object sender, SelectionChangedEventArgs e)
    {
        if (RemoteFileList.SelectedItem is RemoteInventoryViewModel selected)
        {
            RemoteInventoryItem item = selected.Item;
            RemoteSelectedText.Text = $"{item.Summary}\n{item.Id}\n{item.Status}";
        }
        else
        {
            RemoteSelectedText.Text = string.Empty;
        }

        UpdateButtons();
    }

    private async void DeleteRemote_Click(object sender, RoutedEventArgs e)
    {
        if (apiClient is null || RemoteFileList.SelectedItem is not RemoteInventoryViewModel selected)
        {
            return;
        }

        RemoteInventoryItem item = selected.Item;
        MessageBoxResult confirm = WpfMessageBox.Show(
            this,
            $"Delete remote {item.DisplayType} file?\n\n{item.Id}\n\nThe local image file will not be deleted.",
            "Delete remote file",
            MessageBoxButton.YesNo,
            MessageBoxImage.Warning);
        if (confirm != MessageBoxResult.Yes)
        {
            return;
        }

        SetBusy(true);
        try
        {
            await apiClient.DeleteFileAsync(item.Id);
            ActionStatusText.Text = $"Deleted {item.Id}";
            App.Log.Info($"Deleted remote file {item.Id}.");
            await RefreshRemoteAsync();
        }
        catch (Exception ex)
        {
            App.Log.Error($"Delete failed for remote file {item.Id}.", ex);
            WpfMessageBox.Show(this, ex.Message, "Delete failed", MessageBoxButton.OK, MessageBoxImage.Error);
        }
        finally
        {
            SetBusy(false);
            UpdateButtons();
        }
    }

    private void ConfigureBuckets()
    {
        updatingBuckets = true;
        try
        {
            List<BucketChoice> buckets = allAssets
                .GroupBy(asset => asset.Bucket, StringComparer.OrdinalIgnoreCase)
                .OrderByDescending(group => group.Key, StringComparer.OrdinalIgnoreCase)
                .Select(group => new BucketChoice($"{group.Key} ({group.Count():N0})", group.Key))
                .ToList();

            BucketChoice all = new($"All months ({allAssets.Count:N0})", null);
            MonthCombo.ItemsSource = new[] { all }.Concat(buckets).ToArray();
            MonthCombo.DisplayMemberPath = nameof(BucketChoice.Name);
            MonthCombo.SelectedIndex = 0;
        }
        finally
        {
            updatingBuckets = false;
        }
    }

    private void Window_KeyDown(object sender, System.Windows.Input.KeyEventArgs e)
    {
        if (filteredAssets.Count == 0)
        {
            return;
        }

        int index = LocalAssetList.SelectedIndex;
        if (e.Key == Key.Right)
        {
            LocalAssetList.SelectedIndex = Math.Min(filteredAssets.Count - 1, index + 1);
            LocalAssetList.ScrollIntoView(LocalAssetList.SelectedItem);
        }
        else if (e.Key == Key.Left)
        {
            LocalAssetList.SelectedIndex = Math.Max(0, index - 1);
            LocalAssetList.ScrollIntoView(LocalAssetList.SelectedItem);
        }
        else if (e.Key == Key.Home)
        {
            LocalAssetList.SelectedIndex = 0;
            LocalAssetList.ScrollIntoView(LocalAssetList.SelectedItem);
        }
        else if (e.Key == Key.End)
        {
            LocalAssetList.SelectedIndex = filteredAssets.Count - 1;
            LocalAssetList.ScrollIntoView(LocalAssetList.SelectedItem);
        }
    }

    private void DisconnectRemote()
    {
        apiClient = null;
        thumbnailLoader.ClearRemoteAuth();
        httpClient?.Dispose();
        httpClient = null;
        RemoteCountText.Text = string.Empty;
        remoteItems.Clear();
        stickerCount = -1;
        emojiCount = -1;
        RemoteEmptyText.Visibility = Visibility.Visible;
    }

    private void ClearPreview()
    {
        StopPreviewAnimation();
        PreviewImage.Source = null;
        PreviewImage.Visibility = Visibility.Collapsed;
        PreviewEmptyText.Visibility = Visibility.Visible;
        SelectedFileTitle.Text = "Preview";
        SelectedFileText.Text = string.Empty;
    }

    private void StopPreviewAnimation()
    {
        previewTimer.Stop();
        previewFrames = [];
        previewFrameDelays = [];
        previewFrameIndex = 0;
    }

    private void LoadAnimatedPreview(string path)
    {
        GifBitmapDecoder decoder = new(
            new Uri(path, UriKind.Absolute),
            BitmapCreateOptions.PreservePixelFormat,
            BitmapCacheOption.OnLoad);

        previewFrames = decoder.Frames.Select(frame =>
        {
            if (frame.CanFreeze)
            {
                frame.Freeze();
            }

            return (BitmapSource)frame;
        }).ToList();
        previewFrameDelays = decoder.Frames.Select(GetFrameDelay).ToList();
        previewFrameIndex = 0;

        if (previewFrames.Count == 0)
        {
            throw new InvalidOperationException("GIF did not contain preview frames.");
        }

        PreviewImage.Source = previewFrames[0];
        if (previewFrames.Count > 1)
        {
            previewTimer.Interval = previewFrameDelays[0];
            previewTimer.Start();
        }
    }

    private void PreviewTimer_Tick(object? sender, EventArgs e)
    {
        if (previewFrames.Count == 0)
        {
            previewTimer.Stop();
            return;
        }

        previewFrameIndex = (previewFrameIndex + 1) % previewFrames.Count;
        PreviewImage.Source = previewFrames[previewFrameIndex];
        previewTimer.Interval = previewFrameDelays[Math.Min(previewFrameIndex, previewFrameDelays.Count - 1)];
    }

    private static BitmapImage LoadBitmap(string path)
    {
        BitmapImage image = new();
        image.BeginInit();
        image.CacheOption = BitmapCacheOption.OnLoad;
        image.CreateOptions = BitmapCreateOptions.IgnoreColorProfile;
        image.UriSource = new Uri(path, UriKind.Absolute);
        image.EndInit();
        if (image.CanFreeze)
        {
            image.Freeze();
        }

        return image;
    }

    private static TimeSpan GetFrameDelay(BitmapFrame frame)
    {
        const string delayQuery = "/grctlext/Delay";
        try
        {
            if (frame.Metadata is BitmapMetadata metadata && metadata.ContainsQuery(delayQuery))
            {
                int hundredths = Convert.ToInt32(metadata.GetQuery(delayQuery));
                if (hundredths > 1)
                {
                    return TimeSpan.FromMilliseconds(hundredths * 10);
                }
            }
        }
        catch (InvalidOperationException)
        {
        }
        catch (NotSupportedException)
        {
        }

        return TimeSpan.FromMilliseconds(100);
    }

    private static bool IsDefaultPhotosFolder(string folder) =>
        string.Equals(
            Path.GetFullPath(folder).TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar),
            Path.GetFullPath(KnownFolders.DefaultPhotosFolder).TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar),
            StringComparison.OrdinalIgnoreCase);

    private void EnableDarkTitleBar()
    {
        try
        {
            IntPtr handle = new WindowInteropHelper(this).Handle;
            if (handle == IntPtr.Zero)
            {
                return;
            }

            int enabled = 1;
            _ = DwmSetWindowAttribute(handle, 20, ref enabled, sizeof(int));
        }
        catch (DllNotFoundException)
        {
        }
        catch (EntryPointNotFoundException)
        {
        }
    }

    [DllImport("dwmapi.dll")]
    private static extern int DwmSetWindowAttribute(IntPtr hwnd, int attribute, ref int value, int size);

    private sealed record FolderChoice(string Name, string Path)
    {
        public override string ToString() => Name;
    }

    private sealed record BucketChoice(string Name, string? Bucket)
    {
        public override string ToString() => Name;
    }
}

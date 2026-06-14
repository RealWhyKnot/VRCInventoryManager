using System.Collections.ObjectModel;
using System.IO;
using System.Net.Http;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using Forms = System.Windows.Forms;
using WpfCursors = System.Windows.Input.Cursors;
using WpfMessageBox = System.Windows.MessageBox;
using VRCInventoryManager.Core;

namespace VRCInventoryManager;

public partial class MainWindow : Window
{
    private readonly ObservableCollection<LocalAsset> filteredAssets = [];
    private readonly ObservableCollection<RemoteInventoryItem> remoteItems = [];
    private readonly LocalAssetScanner scanner = new();
    private readonly AppSettingsStore settingsStore = new();
    private readonly VrcxCookieProvider cookieProvider = new();
    private readonly Forms.PictureBox previewBox = new();

    private List<LocalAsset> allAssets = [];
    private AppSettings settings = new();
    private VrchatApiClient? apiClient;
    private bool loaded;
    private int stickerCount = -1;
    private int emojiCount = -1;

    public MainWindow()
    {
        InitializeComponent();
        LocalAssetList.ItemsSource = filteredAssets;
        RemoteFileList.ItemsSource = remoteItems;
        Loaded += MainWindow_Loaded;
        Closing += (_, _) => previewBox.Dispose();
    }

    private async void MainWindow_Loaded(object sender, RoutedEventArgs e)
    {
        if (loaded)
        {
            return;
        }

        loaded = true;
        ConfigurePreview();
        ConfigureFolders();
        settings = settingsStore.Load();
        SelectInitialFolder(settings.LocalRoot);
        await RefreshLocalAsync();
        await ConnectAsync();
    }

    private void ConfigurePreview()
    {
        previewBox.Dock = Forms.DockStyle.Fill;
        previewBox.SizeMode = Forms.PictureBoxSizeMode.Zoom;
        previewBox.BackColor = System.Drawing.Color.FromArgb(11, 13, 18);
        PreviewHost.Child = previewBox;
    }

    private void ConfigureFolders()
    {
        FolderCombo.ItemsSource = new[]
        {
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
        CurrentFolderText.Text = folder;
        ActionStatusText.Text = "Scanning...";
        allAssets = await Task.Run(() => scanner.Scan(folder).ToList());
        ApplyFilter();
        LocalCountText.Text = $"{filteredAssets.Count:N0} files";
        ActionStatusText.Text = string.Empty;
        settings = settings with { LocalRoot = folder };
        settingsStore.Save(settings);
    }

    private async Task ConnectAsync()
    {
        try
        {
            SetBusy(true);
            ConnectionStatusText.Text = "Connecting through VRCX...";
            VrcxAuthSession session = await Task.Run(cookieProvider.LoadDefaultSession);
            apiClient = new VrchatApiClient(new HttpClient(), session.Cookies, session.UserAgent);

            bool authenticated = await apiClient.CheckAuthAsync();
            if (!authenticated)
            {
                apiClient = null;
                ConnectionStatusText.Text = "VRCX cookies were found, but VRChat rejected the session.";
                RemoteCountText.Text = string.Empty;
                remoteItems.Clear();
                stickerCount = -1;
                emojiCount = -1;
                return;
            }

            await apiClient.FetchConfigAsync();
            ConnectionStatusText.Text = "Connected through VRCX.";
            await RefreshRemoteAsync();
        }
        catch (Exception ex)
        {
            apiClient = null;
            ConnectionStatusText.Text = $"Disconnected: {ex.Message}";
            RemoteCountText.Text = string.Empty;
            remoteItems.Clear();
            stickerCount = -1;
            emojiCount = -1;
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
                remoteItems.Add(item);
            }

            RemoteCountText.Text = $"Stickers {snapshot.StickerCount}/{RemoteInventorySnapshot.StickerLimit}  Emojis {snapshot.EmojiCount}/{RemoteInventorySnapshot.EmojiLimit}";
        }
        catch (Exception ex)
        {
            ConnectionStatusText.Text = $"Inventory refresh failed: {ex.Message}";
        }
        finally
        {
            SetBusy(false);
            UpdateButtons();
        }
    }

    private void ApplyFilter()
    {
        string query = SearchBox.Text.Trim();
        IEnumerable<LocalAsset> assets = allAssets;
        if (!string.IsNullOrWhiteSpace(query))
        {
            assets = assets.Where(asset =>
                asset.Name.Contains(query, StringComparison.OrdinalIgnoreCase) ||
                asset.Directory.Contains(query, StringComparison.OrdinalIgnoreCase));
        }

        filteredAssets.Clear();
        foreach (LocalAsset asset in assets)
        {
            filteredAssets.Add(asset);
        }

        LocalCountText.Text = $"{filteredAssets.Count:N0} files";
    }

    private void LoadPreview(LocalAsset? asset)
    {
        previewBox.ImageLocation = null;
        if (asset is null)
        {
            SelectedFileText.Text = string.Empty;
            return;
        }

        previewBox.ImageLocation = asset.Path;
        SelectedFileText.Text = $"{asset.Name}\n{asset.Path}\n{asset.SizeText}  style {asset.AnimationStyle}";
    }

    private async Task UploadSelectedAsync(Func<VrchatApiClient, LocalAsset, Task<UploadResult>> upload)
    {
        if (apiClient is null)
        {
            WpfMessageBox.Show(this, "Connect to VRCX before uploading.", "Not connected", MessageBoxButton.OK, MessageBoxImage.Information);
            return;
        }

        if (LocalAssetList.SelectedItem is not LocalAsset asset)
        {
            return;
        }

        SetBusy(true);
        try
        {
            ActionStatusText.Text = "Uploading...";
            UploadResult result = await upload(apiClient, asset);
            ActionStatusText.Text = $"Uploaded {result.Id}";
            await RefreshRemoteAsync();
        }
        catch (Exception ex)
        {
            ActionStatusText.Text = "Upload failed.";
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
        bool hasAsset = LocalAssetList.SelectedItem is LocalAsset;
        bool hasGif = LocalAssetList.SelectedItem is LocalAsset asset && asset.IsGif;
        bool connected = apiClient is not null;
        bool stickerHasCapacity = stickerCount < 0 || stickerCount < RemoteInventorySnapshot.StickerLimit;
        bool emojiHasCapacity = emojiCount < 0 || emojiCount < RemoteInventorySnapshot.EmojiLimit;
        UploadStickerButton.IsEnabled = hasAsset && connected && stickerHasCapacity;
        UploadEmojiButton.IsEnabled = hasAsset && connected && emojiHasCapacity;
        UploadAnimatedEmojiButton.IsEnabled = hasGif && connected && emojiHasCapacity;
        DeleteRemoteButton.IsEnabled = RemoteFileList.SelectedItem is RemoteInventoryItem && connected;
    }

    private void SetBusy(bool isBusy)
    {
        Cursor = isBusy ? WpfCursors.Wait : WpfCursors.Arrow;
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
        using Forms.FolderBrowserDialog dialog = new()
        {
            Description = "Choose a VRChat image folder",
            UseDescriptionForTitle = true,
            SelectedPath = Directory.Exists(CurrentFolder) ? CurrentFolder : KnownFolders.DefaultEmojiFolder
        };

        if (dialog.ShowDialog() == Forms.DialogResult.OK)
        {
            FolderChoice custom = new("Custom", dialog.SelectedPath);
            FolderCombo.ItemsSource = new[]
            {
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

    private void LocalAssetList_SelectionChanged(object sender, SelectionChangedEventArgs e)
    {
        LoadPreview(LocalAssetList.SelectedItem as LocalAsset);
        UpdateButtons();
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
        if (RemoteFileList.SelectedItem is RemoteInventoryItem item)
        {
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
        if (apiClient is null || RemoteFileList.SelectedItem is not RemoteInventoryItem item)
        {
            return;
        }

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
            await RefreshRemoteAsync();
        }
        catch (Exception ex)
        {
            WpfMessageBox.Show(this, ex.Message, "Delete failed", MessageBoxButton.OK, MessageBoxImage.Error);
        }
        finally
        {
            SetBusy(false);
            UpdateButtons();
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

    private sealed record FolderChoice(string Name, string Path);
}

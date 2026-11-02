using System.Collections.ObjectModel;
using System.Net.Http;
using System.Windows;
using System.Windows.Threading;
using VRCInventoryManager.Core;

namespace VRCInventoryManager;

internal sealed partial class MainWindow : Window, IDisposable
{
    private readonly ObservableCollection<RemoteInventoryViewModel> _remoteItems = [];

    private readonly AppSettingsStore _settingsStore = new();
    private readonly VrcxCookieProvider _cookieProvider = new();
    private readonly ImageThumbnailLoader _thumbnailLoader = new();
    private readonly DispatcherTimer _searchTimer = new() { Interval = TimeSpan.FromMilliseconds(250) };
    private KnownFolderPaths _localFolders = KnownFolders.Resolve();

    private List<LocalAsset> _allAssets = [];
    private List<LocalAssetViewModel> _filteredAssets = [];
    private IReadOnlyList<FolderNodeViewModel> _folderNodes = [];
    private FolderNodeViewModel? _selectedFolderNode;
    private AppSettings _settings = new();
    private VrchatApiClient? _apiClient;
    private HttpClient? _httpClient;
    private bool _isLoaded;
    private bool _isUpdatingFolderTree;
    private bool _shouldSuppressLocalSelectionChanged;
    private bool _shouldSuppressRemoteSelectionChanged;
    private int _busyDepth;
    private int _stickerCount = -1;
    private int _emojiCount = -1;
    private GithubRelease? _pendingUpdate;

    public MainWindow()
    {
        InitializeComponent();
        LocalAssetList.ItemsSource = _filteredAssets;
        RemoteFileList.ItemsSource = _remoteItems;
        _searchTimer.Tick += SearchTimer_Tick;
        _previewTimer.Tick += PreviewTimer_Tick;
        _previewSelectionTimer.Tick += PreviewSelectionTimer_Tick;
        Loaded += MainWindow_Loaded;
        Closing += (_, _) =>
        {
            _searchTimer.Stop();
            _previewTimer.Stop();
            _previewSelectionTimer.Stop();
            CancelPreviewLoad();
            Dispose();
        };
    }

    public void Dispose()
    {
        _previewCancellationSource?.Dispose();
        _httpClient?.Dispose();
        _thumbnailLoader.Dispose();
    }

    protected override void OnSourceInitialized(EventArgs e)
    {
        base.OnSourceInitialized(e);
        EnableDarkTitleBar(this);
    }

    private async void MainWindow_Loaded(object sender, RoutedEventArgs e)
    {
        if (_isLoaded)
        {
            return;
        }

        try
        {
            _settings = _settingsStore.Load();
            ConfigureFolders();
            SelectInitialFolder(GetInitialLocalRoot());
            _isLoaded = true;
            ClearPreview();
            UpdateButtons();
            _ = CheckForUpdateAsync(CancellationToken.None);
            await RefreshLocalAsync(CancellationToken.None);
            await ConnectAsync(CancellationToken.None);
        }
        catch (System.IO.IOException exception)
        {
            HandleMainWindowLoadedFailure(exception);
        }
        catch (UnauthorizedAccessException exception)
        {
            HandleMainWindowLoadedFailure(exception);
        }
        catch (InvalidOperationException exception)
        {
            HandleMainWindowLoadedFailure(exception);
        }
        catch (HttpRequestException exception)
        {
            HandleMainWindowLoadedFailure(exception);
        }
        catch (System.Text.Json.JsonException exception)
        {
            HandleMainWindowLoadedFailure(exception);
        }
        catch (NotSupportedException exception)
        {
            HandleMainWindowLoadedFailure(exception);
        }
        catch (Microsoft.Data.Sqlite.SqliteException exception)
        {
            HandleMainWindowLoadedFailure(exception);
        }
        catch (OperationCanceledException exception)
        {
            HandleMainWindowLoadedFailure(exception);
        }
    }

    private async Task CheckForUpdateAsync(CancellationToken cancellationToken = default)
    {
        try
        {
            await Updater.CleanUpAsync(cancellationToken);
            if (!_settings.IsUpdateCheckEnabled)
            {
                return;
            }

            _pendingUpdate = await Updater.FindAsync(_settings.SkippedUpdate, cancellationToken);
        }
        catch (System.IO.IOException exception)
        {
            HandleCheckForUpdateFailure(exception); return;
        }
        catch (UnauthorizedAccessException exception)
        {
            HandleCheckForUpdateFailure(exception); return;
        }
        catch (InvalidOperationException exception)
        {
            HandleCheckForUpdateFailure(exception); return;
        }
        catch (HttpRequestException exception)
        {
            HandleCheckForUpdateFailure(exception); return;
        }
        catch (System.Text.Json.JsonException exception)
        {
            HandleCheckForUpdateFailure(exception); return;
        }
        catch (NotSupportedException exception)
        {
            HandleCheckForUpdateFailure(exception); return;
        }
        catch (OperationCanceledException exception)
        {
            HandleCheckForUpdateFailure(exception); return;
        }

        OfferUpdate();
    }

    private async Task OfferUpdateWhenIdleAsync(CancellationToken cancellationToken)
    {
        await Task.Yield();
        cancellationToken.ThrowIfCancellationRequested();
        OfferUpdate();
    }

    private void OfferUpdate()
    {
        if (_pendingUpdate is not { } release || _busyDepth > 0 || !IsLoaded)
        {
            return;
        }

        _pendingUpdate = null;
        using var dialog = new UpdateWindow(release) { Owner = this };
        dialog.ShowDialog();
        string? downloaded = dialog.Downloaded;
        switch (dialog.Answer)
        {
            case UpdateAnswer.Skip:
                _settings = _settings with { SkippedUpdate = release.TagName };
                _settingsStore.Save(_settings);
                break;
            case UpdateAnswer.Downloaded:
                try
                {
                    Updater.Apply(downloaded!);
                }
                catch (System.IO.IOException exception)
                {
                    HandleOfferUpdateFailure(exception); return;
                }
                catch (UnauthorizedAccessException exception)
                {
                    HandleOfferUpdateFailure(exception); return;
                }
                catch (InvalidOperationException exception)
                {
                    HandleOfferUpdateFailure(exception); return;
                }
                catch (HttpRequestException exception)
                {
                    HandleOfferUpdateFailure(exception); return;
                }
                catch (System.ComponentModel.Win32Exception exception)
                {
                    HandleOfferUpdateFailure(exception); return;
                }

                Close();
                break;
            case UpdateAnswer.Later:
                break;
            default:
                break;
        }
    }
}

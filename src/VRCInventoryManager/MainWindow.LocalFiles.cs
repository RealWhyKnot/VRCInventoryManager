using System.Diagnostics;
using System.IO;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using Microsoft.Win32;
using VRCInventoryManager.Core;

namespace VRCInventoryManager;

internal sealed partial class MainWindow
{
    private string CurrentFolder => FolderCombo.SelectedItem is FolderChoice choice ? choice.Path : _localFolders.EmojiFolder;

    private void ConfigureFolders()
    {
        _localFolders = KnownFolders.Resolve();
        FolderCombo.ItemsSource = BuildFolderChoices(_settings.LocalRoot);
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

        FolderCombo.SelectedItem = FolderCombo.Items
            .OfType<FolderChoice>()
            .FirstOrDefault(choice => string.Equals(choice.Name, "Emoji", StringComparison.OrdinalIgnoreCase));
    }

    private async Task RefreshLocalAsync(CancellationToken cancellationToken = default)
    {
        string folder = CurrentFolder;
        var stopwatch = Stopwatch.StartNew();
        SetBusy(isBusy: true);
        try
        {
            CurrentFolderText.Text = folder;
            ActionStatusText.Text = "Scanning...";
            _thumbnailLoader.ClearLocal();
            IReadOnlySet<string>? excludedFolders = IsDefaultPhotosFolder(folder) ? KnownFolders.InventoryFolderNames : null;
            _allAssets = await Task.Run(() => LocalAssetScanner.Scan(folder, excludedFolders).ToList(), cancellationToken);
            ConfigureFolderTree(folder);
            ApplyFilter();
            ActionStatusText.Text = _allAssets.Count == 0 ? "No local images found." : "Ready.";
            _settings = _settings with { LocalRoot = folder };
            _settingsStore.Save(_settings);
            App.Log.Info($"Scanned local folder '{folder}' and found {_allAssets.Count} images in {stopwatch.ElapsedMilliseconds} ms.");
        }
        catch (IOException exception)
        {
            HandleRefreshLocalFailure(exception, folder);
        }
        catch (UnauthorizedAccessException exception)
        {
            HandleRefreshLocalFailure(exception, folder);
        }
        catch (InvalidOperationException exception)
        {
            HandleRefreshLocalFailure(exception, folder);
        }
        catch (OperationCanceledException exception)
        {
            HandleRefreshLocalFailure(exception, folder);
        }
        finally
        {
            SetBusy(isBusy: false);
        }
    }

    private void ApplyFilter()
    {
        LocalAsset? previousSelection = (LocalAssetList.SelectedItem as LocalAssetViewModel)?.Asset;
        string query = SearchBox.Text.Trim();
        string? selectedFolder = _selectedFolderNode?.RelativePath;
        bool includeSubfolders = IncludeSubfoldersCheckBox.IsChecked != false;
        _filteredAssets = [.. _allAssets
            .Where(asset => LocalAssetFilter.Matches(asset, selectedFolder, includeSubfolders, query))
            .Select(asset => new LocalAssetViewModel(asset))];
        LocalAssetList.ItemsSource = _filteredAssets;

        LocalCountText.Text = $"{_filteredAssets.Count:N0} files";
        LocalEmptyText.Visibility = _filteredAssets.Count == 0 ? Visibility.Visible : Visibility.Collapsed;

        if (_filteredAssets.Count == 0)
        {
            if (RemoteFileList.SelectedItem is null)
            {
                ClearPreview();
            }

            return;
        }

        LocalAssetViewModel? nextSelection = previousSelection is null
            ? null
            : _filteredAssets.FirstOrDefault(asset => string.Equals(asset.Asset.Path, previousSelection.Path, StringComparison.OrdinalIgnoreCase));
        LocalAssetList.SelectedItem = nextSelection is not null ? nextSelection : RemoteFileList.SelectedItem is not null ? null : (object)_filteredAssets[0];
    }

    private async void FolderCombo_SelectionChanged(object sender, SelectionChangedEventArgs e)
    {
        if (_isLoaded)
        {
            await RefreshLocalAsync(CancellationToken.None);
        }
    }

    private async void Browse_Click(object sender, RoutedEventArgs e)
    {
        OpenFolderDialog dialog = new()
        {
            Title = "Choose a VRChat image folder",
            InitialDirectory = Directory.Exists(CurrentFolder) ? CurrentFolder : _localFolders.EmojiFolder,
        };

        if (dialog.ShowDialog(this) == true)
        {
            IReadOnlyList<FolderChoice> choices = BuildFolderChoices(dialog.FolderName);
            FolderCombo.ItemsSource = choices;
            FolderCombo.DisplayMemberPath = nameof(FolderChoice.Name);
            FolderCombo.SelectedItem = choices.First(choice => string.Equals(choice.Name, "Custom", StringComparison.OrdinalIgnoreCase));
            await RefreshLocalAsync(CancellationToken.None);
        }
    }

    private async void RefreshLocal_Click(object sender, RoutedEventArgs e) => await RefreshLocalAsync(CancellationToken.None);

    private void SearchBox_TextChanged(object sender, TextChangedEventArgs e)
    {
        if (!_isLoaded)
        {
            return;
        }

        _searchTimer.Stop();
        _searchTimer.Start();
    }

    private void SearchTimer_Tick(object? sender, EventArgs e)
    {
        _searchTimer.Stop();
        ApplyFilter();
    }

    private void FolderTree_SelectedItemChanged(object sender, RoutedPropertyChangedEventArgs<object> e)
    {
        if (_isLoaded && !_isUpdatingFolderTree && e.NewValue is FolderNodeViewModel node)
        {
            _selectedFolderNode = node;
            ApplyFilter();
        }
    }

    private void IncludeSubfolders_Changed(object sender, RoutedEventArgs e)
    {
        if (_isLoaded)
        {
            ApplyFilter();
        }
    }

    private void ConfigureFolderTree(string folder)
    {
        _isUpdatingFolderTree = true;
        try
        {
            FolderNode rootNode = FolderTreeBuilder.Build(_allAssets, folder);
            FolderNodeViewModel rootViewModel = new(rootNode)
            {
                IsExpanded = true,
                IsSelected = true,
            };
            _folderNodes = [rootViewModel];
            _selectedFolderNode = rootViewModel;
            FolderTree.ItemsSource = _folderNodes;
        }
        finally
        {
            _isUpdatingFolderTree = false;
        }
    }

    private void Window_KeyDown(object sender, KeyEventArgs e)
    {
        if (_filteredAssets.Count == 0)
        {
            return;
        }

        int index = LocalAssetList.SelectedIndex;
        if (e.Key == Key.Right)
        {
            LocalAssetList.SelectedIndex = Math.Min(_filteredAssets.Count - 1, index + 1);
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
            LocalAssetList.SelectedIndex = _filteredAssets.Count - 1;
            LocalAssetList.ScrollIntoView(LocalAssetList.SelectedItem);
        }
    }

    private string GetInitialLocalRoot() =>
        string.IsNullOrWhiteSpace(_settings.LocalRoot) ? _localFolders.EmojiFolder : _settings.LocalRoot;

    private IReadOnlyList<FolderChoice> BuildFolderChoices(string? customPath)
    {
        string customRoot = string.IsNullOrWhiteSpace(customPath) ? _localFolders.EmojiFolder : customPath;
        return
        [
            new FolderChoice("Photos", _localFolders.PhotosFolder),
            new FolderChoice("Emoji", _localFolders.EmojiFolder),
            new FolderChoice("Stickers", _localFolders.StickersFolder),
            new FolderChoice("Prints", _localFolders.PrintsFolder),
            new FolderChoice("Custom", customRoot),
        ];
    }

    private bool IsDefaultPhotosFolder(string folder) =>
        string.Equals(
            Path.GetFullPath(folder).TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar),
            Path.GetFullPath(_localFolders.PhotosFolder).TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar),
            StringComparison.OrdinalIgnoreCase);

    private sealed record FolderChoice(string Name, string Path)
    {
        public override string ToString() => Name;
    }
}

using System.Diagnostics;
using System.IO;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using Microsoft.Win32;
using VRCInventoryManager.Core;

namespace VRCInventoryManager;

public partial class MainWindow
{
    private string CurrentFolder => FolderCombo.SelectedItem is FolderChoice choice ? choice.Path : localFolders.EmojiFolder;

    private void ConfigureFolders()
    {
        localFolders = KnownFolders.Resolve();
        FolderCombo.ItemsSource = BuildFolderChoices(settings.LocalRoot);
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

    private async Task RefreshLocalAsync()
    {
        string folder = CurrentFolder;
        Stopwatch stopwatch = Stopwatch.StartNew();
        SetBusy(true);
        try
        {
            CurrentFolderText.Text = folder;
            ActionStatusText.Text = "Scanning...";
            thumbnailLoader.ClearLocal();
            IReadOnlySet<string>? excludedFolders = IsDefaultPhotosFolder(folder) ? KnownFolders.InventoryFolderNames : null;
            allAssets = await Task.Run(() => scanner.Scan(folder, excludedFolders).ToList());
            ConfigureFolderTree(folder);
            ApplyFilter();
            ActionStatusText.Text = allAssets.Count == 0 ? "No local images found." : "Ready.";
            settings = settings with { LocalRoot = folder };
            settingsStore.Save(settings);
            App.Log.Info($"Scanned local folder '{folder}' and found {allAssets.Count} images in {stopwatch.ElapsedMilliseconds} ms.");
        }
        catch (Exception ex)
        {
            allAssets = [];
            ConfigureFolderTree(folder);
            ApplyFilter();
            ActionStatusText.Text = "Local scan failed.";
            App.Log.Error($"Local scan failed for '{folder}'.", ex);
        }
        finally
        {
            SetBusy(false);
        }
    }

    private void ApplyFilter()
    {
        LocalAsset? previousSelection = (LocalAssetList.SelectedItem as LocalAssetViewModel)?.Asset;
        string query = SearchBox.Text.Trim();
        string? selectedFolder = selectedFolderNode?.RelativePath;
        bool includeSubfolders = IncludeSubfoldersCheckBox.IsChecked != false;
        filteredAssets = allAssets
            .Where(asset => LocalAssetFilter.Matches(asset, selectedFolder, includeSubfolders, query))
            .Select(asset => new LocalAssetViewModel(asset))
            .ToList();
        LocalAssetList.ItemsSource = filteredAssets;

        LocalCountText.Text = $"{filteredAssets.Count:N0} files";
        LocalEmptyText.Visibility = filteredAssets.Count == 0 ? Visibility.Visible : Visibility.Collapsed;

        if (filteredAssets.Count == 0)
        {
            if (RemoteFileList.SelectedItem is null)
            {
                ClearPreview();
            }

            return;
        }

        LocalAssetViewModel? nextSelection = previousSelection is null
            ? null
            : filteredAssets.FirstOrDefault(asset => string.Equals(asset.Asset.Path, previousSelection.Path, StringComparison.OrdinalIgnoreCase));
        if (nextSelection is not null)
        {
            LocalAssetList.SelectedItem = nextSelection;
        }
        else if (RemoteFileList.SelectedItem is not null)
        {
            LocalAssetList.SelectedItem = null;
        }
        else
        {
            LocalAssetList.SelectedItem = filteredAssets[0];
        }
    }

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
            InitialDirectory = Directory.Exists(CurrentFolder) ? CurrentFolder : localFolders.EmojiFolder
        };

        if (dialog.ShowDialog(this) == true)
        {
            IReadOnlyList<FolderChoice> choices = BuildFolderChoices(dialog.FolderName);
            FolderCombo.ItemsSource = choices;
            FolderCombo.DisplayMemberPath = nameof(FolderChoice.Name);
            FolderCombo.SelectedItem = choices.First(choice => string.Equals(choice.Name, "Custom", StringComparison.OrdinalIgnoreCase));
            await RefreshLocalAsync();
        }
    }

    private async void RefreshLocal_Click(object sender, RoutedEventArgs e) => await RefreshLocalAsync();

    private void SearchBox_TextChanged(object sender, TextChangedEventArgs e)
    {
        if (!loaded)
        {
            return;
        }

        searchTimer.Stop();
        searchTimer.Start();
    }

    private void SearchTimer_Tick(object? sender, EventArgs e)
    {
        searchTimer.Stop();
        ApplyFilter();
    }

    private void FolderTree_SelectedItemChanged(object sender, RoutedPropertyChangedEventArgs<object> e)
    {
        if (loaded && !updatingFolderTree && e.NewValue is FolderNodeViewModel node)
        {
            selectedFolderNode = node;
            ApplyFilter();
        }
    }

    private void IncludeSubfolders_Changed(object sender, RoutedEventArgs e)
    {
        if (loaded)
        {
            ApplyFilter();
        }
    }

    private void ConfigureFolderTree(string folder)
    {
        updatingFolderTree = true;
        try
        {
            FolderNode rootNode = FolderTreeBuilder.Build(allAssets, folder);
            FolderNodeViewModel rootViewModel = new(rootNode)
            {
                IsExpanded = true,
                IsSelected = true
            };
            folderNodes = [rootViewModel];
            selectedFolderNode = rootViewModel;
            FolderTree.ItemsSource = folderNodes;
        }
        finally
        {
            updatingFolderTree = false;
        }
    }

    private void Window_KeyDown(object sender, KeyEventArgs e)
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

    private string GetInitialLocalRoot() =>
        string.IsNullOrWhiteSpace(settings.LocalRoot) ? localFolders.EmojiFolder : settings.LocalRoot;

    private IReadOnlyList<FolderChoice> BuildFolderChoices(string? customPath)
    {
        string customRoot = string.IsNullOrWhiteSpace(customPath) ? localFolders.EmojiFolder : customPath;
        return
        [
            new FolderChoice("Photos", localFolders.PhotosFolder),
            new FolderChoice("Emoji", localFolders.EmojiFolder),
            new FolderChoice("Stickers", localFolders.StickersFolder),
            new FolderChoice("Prints", localFolders.PrintsFolder),
            new FolderChoice("Custom", customRoot)
        ];
    }

    private bool IsDefaultPhotosFolder(string folder) =>
        string.Equals(
            Path.GetFullPath(folder).TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar),
            Path.GetFullPath(localFolders.PhotosFolder).TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar),
            StringComparison.OrdinalIgnoreCase);

    private sealed record FolderChoice(string Name, string Path)
    {
        public override string ToString() => Name;
    }
}

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
    private string CurrentFolder => FolderCombo.SelectedItem is FolderChoice choice ? choice.Path : settings.LocalRoot;

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

    private static bool IsDefaultPhotosFolder(string folder) =>
        string.Equals(
            Path.GetFullPath(folder).TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar),
            Path.GetFullPath(KnownFolders.DefaultPhotosFolder).TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar),
            StringComparison.OrdinalIgnoreCase);

    private sealed record FolderChoice(string Name, string Path)
    {
        public override string ToString() => Name;
    }

    private sealed record BucketChoice(string Name, string? Bucket)
    {
        public override string ToString() => Name;
    }
}

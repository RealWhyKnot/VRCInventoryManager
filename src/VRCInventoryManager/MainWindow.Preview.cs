using System.Windows;
using System.Windows.Controls;
using System.Windows.Media.Imaging;
using System.Windows.Threading;
using VRCInventoryManager.Core;

namespace VRCInventoryManager;

public partial class MainWindow
{
    private readonly DispatcherTimer previewTimer = new();

    private List<BitmapSource> previewFrames = [];
    private List<TimeSpan> previewFrameDelays = [];
    private int previewFrameIndex;

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
}

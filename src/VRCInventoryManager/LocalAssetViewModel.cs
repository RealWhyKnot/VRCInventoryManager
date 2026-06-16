using System.ComponentModel;
using System.Runtime.CompilerServices;
using System.Windows.Media.Imaging;
using VRCInventoryManager.Core;

namespace VRCInventoryManager;

internal sealed class LocalAssetViewModel(LocalAsset asset) : INotifyPropertyChanged
{
    private bool thumbnailRequested;
    private BitmapSource? thumbnail;

    public event PropertyChangedEventHandler? PropertyChanged;

    public LocalAsset Asset { get; } = asset;

    public BitmapSource? Thumbnail
    {
        get => thumbnail;
        private set
        {
            if (!ReferenceEquals(thumbnail, value))
            {
                thumbnail = value;
                OnPropertyChanged();
            }
        }
    }

    public string Name => Asset.Name;

    public string DetailsText => Asset.DetailsText;

    public string TileMeta => $"{Asset.Extension.TrimStart('.').ToUpperInvariant()}  {Asset.SizeText}";

    public string FolderName => Asset.Bucket;

    public async Task EnsureThumbnailAsync(ImageThumbnailLoader loader)
    {
        if (thumbnailRequested)
        {
            return;
        }

        thumbnailRequested = true;
        Thumbnail = await loader.LoadLocalAsync(Asset);
    }

    private void OnPropertyChanged([CallerMemberName] string? propertyName = null) =>
        PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(propertyName));
}

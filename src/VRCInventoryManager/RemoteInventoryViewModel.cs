using System.ComponentModel;
using System.Runtime.CompilerServices;
using System.Windows.Media.Imaging;
using VRCInventoryManager.Core;

namespace VRCInventoryManager;

internal sealed class RemoteInventoryViewModel(RemoteInventoryItem item) : INotifyPropertyChanged
{
    private bool thumbnailRequested;
    private BitmapSource? thumbnail;

    public event PropertyChangedEventHandler? PropertyChanged;

    public RemoteInventoryItem Item { get; } = item;

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

    public string DisplayType => Item.DisplayType;

    public string Summary => Item.Summary;

    public string Id => Item.Id;

    public string Status => Item.Status;

    public async Task EnsureThumbnailAsync(ImageThumbnailLoader loader)
    {
        if (thumbnailRequested || string.IsNullOrWhiteSpace(Item.PreviewUrl))
        {
            return;
        }

        thumbnailRequested = true;
        Thumbnail = await loader.LoadRemoteAsync(Item.PreviewUrl);
    }

    private void OnPropertyChanged([CallerMemberName] string? propertyName = null) =>
        PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(propertyName));
}

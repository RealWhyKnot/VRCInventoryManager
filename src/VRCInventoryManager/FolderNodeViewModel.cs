using System.Collections.ObjectModel;
using System.ComponentModel;
using System.Runtime.CompilerServices;
using VRCInventoryManager.Core;

namespace VRCInventoryManager;

internal sealed class FolderNodeViewModel : INotifyPropertyChanged
{
    private bool isExpanded;
    private bool isSelected;

    public FolderNodeViewModel(FolderNode node)
    {
        Node = node;
        Children = new ObservableCollection<FolderNodeViewModel>(
            node.Children.Select(child => new FolderNodeViewModel(child)));
    }

    public event PropertyChangedEventHandler? PropertyChanged;

    public FolderNode Node { get; }

    public ObservableCollection<FolderNodeViewModel> Children { get; }

    public string Name => Node.Name;

    public string RelativePath => Node.RelativePath;

    public string FullPath => Node.FullPath;

    public string CountText => string.IsNullOrEmpty(Node.RelativePath) || Node.DirectCount == Node.TotalCount
        ? $"{Node.TotalCount:N0}"
        : $"{Node.DirectCount:N0} / {Node.TotalCount:N0}";

    public bool IsExpanded
    {
        get => isExpanded;
        set
        {
            if (isExpanded != value)
            {
                isExpanded = value;
                OnPropertyChanged();
            }
        }
    }

    public bool IsSelected
    {
        get => isSelected;
        set
        {
            if (isSelected != value)
            {
                isSelected = value;
                OnPropertyChanged();
            }
        }
    }

    private void OnPropertyChanged([CallerMemberName] string? propertyName = null) =>
        PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(propertyName));
}

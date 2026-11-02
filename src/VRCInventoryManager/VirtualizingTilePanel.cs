using System.Windows;
using System.Windows.Controls;
using System.Windows.Controls.Primitives;
using System.Windows.Media;

namespace VRCInventoryManager;

internal sealed class VirtualizingTilePanel : VirtualizingPanel, IScrollInfo
{
    public static readonly DependencyProperty ItemWidthProperty = DependencyProperty.Register(
        nameof(ItemWidth),
        typeof(double),
        typeof(VirtualizingTilePanel),
        new FrameworkPropertyMetadata(128.0, FrameworkPropertyMetadataOptions.AffectsMeasure));

    public static readonly DependencyProperty ItemHeightProperty = DependencyProperty.Register(
        nameof(ItemHeight),
        typeof(double),
        typeof(VirtualizingTilePanel),
        new FrameworkPropertyMetadata(162.0, FrameworkPropertyMetadataOptions.AffectsMeasure));

    private Size _extent;
    private Size _viewport;
    private Point _offset;
    private int _columnCount = 1;

    public double ItemWidth
    {
        get => (double)GetValue(ItemWidthProperty);
        set => SetValue(ItemWidthProperty, value);
    }

    public double ItemHeight
    {
        get => (double)GetValue(ItemHeightProperty);
        set => SetValue(ItemHeightProperty, value);
    }

    public bool CanHorizontallyScroll { get; set; }

    public bool CanVerticallyScroll { get; set; }

    public double ExtentWidth => _extent.Width;

    public double ExtentHeight => _extent.Height;

    public double ViewportWidth => _viewport.Width;

    public double ViewportHeight => _viewport.Height;

    public double HorizontalOffset => _offset.X;

    public double VerticalOffset => _offset.Y;

    public ScrollViewer? ScrollOwner { get; set; }

    protected override Size MeasureOverride(Size availableSize)
    {
        var owner = ItemsControl.GetItemsOwner(this);
        int itemCount = owner?.Items.Count ?? 0;
        double availableWidth = double.IsInfinity(availableSize.Width) || availableSize.Width <= 0
            ? ItemWidth
            : availableSize.Width;

        _columnCount = Math.Max(1, (int)Math.Floor(availableWidth / ItemWidth));
        int rowCount = itemCount == 0 ? 0 : (int)Math.Ceiling(itemCount / (double)_columnCount);
        _viewport = new Size(availableWidth, double.IsInfinity(availableSize.Height) ? 0 : availableSize.Height);
        _extent = new Size(availableWidth, rowCount * ItemHeight);
        SetVerticalOffset(_offset.Y);

        int firstVisibleRow = Math.Max(0, (int)Math.Floor(_offset.Y / ItemHeight));
        int visibleRowCount = Math.Max(1, (int)Math.Ceiling(_viewport.Height / ItemHeight) + 2);
        int firstIndex = Math.Min(itemCount, firstVisibleRow * _columnCount);
        int lastIndex = Math.Min(itemCount - 1, ((firstVisibleRow + visibleRowCount) * _columnCount) - 1);

        CleanUpItems(firstIndex, lastIndex);
        if (itemCount > 0 && firstIndex <= lastIndex)
        {
            GenerateItems(firstIndex, lastIndex);
        }

        ScrollOwner?.InvalidateScrollInfo();
        return availableSize;
    }

    protected override Size ArrangeOverride(Size finalSize)
    {
        IItemContainerGenerator generator = ItemContainerGenerator;
        for (int i = 0; i < InternalChildren.Count; i++)
        {
            UIElement child = InternalChildren[i];
            int itemIndex = generator.IndexFromGeneratorPosition(new GeneratorPosition(i, 0));
            if (itemIndex < 0)
            {
                continue;
            }

            int row = itemIndex / _columnCount;
            int column = itemIndex % _columnCount;
            Rect bounds = new(column * ItemWidth, (row * ItemHeight) - _offset.Y, ItemWidth, ItemHeight);
            child.Arrange(bounds);
        }

        return finalSize;
    }

    protected override void BringIndexIntoView(int index)
    {
        if (index < 0)
        {
            return;
        }

        int row = index / _columnCount;
        double top = row * ItemHeight;
        double bottom = top + ItemHeight;
        if (top < _offset.Y)
        {
            SetVerticalOffset(top);
        }
        else if (bottom > _offset.Y + _viewport.Height)
        {
            SetVerticalOffset(bottom - _viewport.Height);
        }
    }

    public void LineUp() => SetVerticalOffset(_offset.Y - ItemHeight);

    public void LineDown() => SetVerticalOffset(_offset.Y + ItemHeight);

    public void LineLeft()
    {
    }

    public void LineRight()
    {
    }

    public void PageUp() => SetVerticalOffset(_offset.Y - _viewport.Height);

    public void PageDown() => SetVerticalOffset(_offset.Y + _viewport.Height);

    public void PageLeft()
    {
    }

    public void PageRight()
    {
    }

    public void MouseWheelUp() => SetVerticalOffset(_offset.Y - (ItemHeight * 3));

    public void MouseWheelDown() => SetVerticalOffset(_offset.Y + (ItemHeight * 3));

    public void MouseWheelLeft()
    {
    }

    public void MouseWheelRight()
    {
    }

    public void SetHorizontalOffset(double value)
    {
        _offset.X = 0;
        ScrollOwner?.InvalidateScrollInfo();
    }

    public void SetVerticalOffset(double value)
    {
        double maxOffset = Math.Max(0, _extent.Height - _viewport.Height);
        double newOffset = Math.Max(0, Math.Min(value, maxOffset));
        if (Math.Abs(newOffset - _offset.Y) < 0.1)
        {
            return;
        }

        _offset.Y = newOffset;
        InvalidateMeasure();
        ScrollOwner?.InvalidateScrollInfo();
    }

    public Rect MakeVisible(Visual visual, Rect rectangle)
    {
        int index = InternalChildren.IndexOf((UIElement)visual);
        if (index < 0)
        {
            return Rect.Empty;
        }

        int itemIndex = ItemContainerGenerator.IndexFromGeneratorPosition(new GeneratorPosition(index, 0));
        BringIndexIntoView(itemIndex);
        return rectangle;
    }

    private void GenerateItems(int firstIndex, int lastIndex)
    {
        IItemContainerGenerator generator = ItemContainerGenerator;
        GeneratorPosition startPosition = generator.GeneratorPositionFromIndex(firstIndex);
        int childIndex = startPosition.Offset == 0 ? startPosition.Index : startPosition.Index + 1;

        using IDisposable generation = generator.StartAt(startPosition, GeneratorDirection.Forward, allowStartAtRealizedItem: true);
        for (int itemIndex = firstIndex; itemIndex <= lastIndex; itemIndex++, childIndex++)
        {
            var child = (UIElement)generator.GenerateNext(out bool newlyRealized);
            if (newlyRealized)
            {
                if (childIndex >= InternalChildren.Count)
                {
                    AddInternalChild(child);
                }
                else
                {
                    InsertInternalChild(childIndex, child);
                }

                generator.PrepareItemContainer(child);
            }

            child.Measure(new Size(ItemWidth, ItemHeight));
        }
    }

    private void CleanUpItems(int firstIndex, int lastIndex)
    {
        IItemContainerGenerator generator = ItemContainerGenerator;
        for (int i = InternalChildren.Count - 1; i >= 0; i--)
        {
            GeneratorPosition childGeneratorPosition = new(i, 0);
            int itemIndex = generator.IndexFromGeneratorPosition(childGeneratorPosition);
            if (itemIndex >= firstIndex && itemIndex <= lastIndex)
            {
                continue;
            }

            RemoveInternalChildRange(i, 1);
            generator.Remove(childGeneratorPosition, 1);
        }
    }
}

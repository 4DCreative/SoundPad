using System.Windows;
using System.Windows.Controls;
namespace SoundPad.Controls;
public sealed class SquarePadPanel : Panel
{
    public static readonly DependencyProperty ViewportHeightProperty = DependencyProperty.Register(nameof(ViewportHeight), typeof(double), typeof(SquarePadPanel), new FrameworkPropertyMetadata(0d, FrameworkPropertyMetadataOptions.AffectsMeasure));
    public double ViewportHeight { get => (double)GetValue(ViewportHeightProperty); set => SetValue(ViewportHeightProperty, value); }
    private int columns = 1;
    private double cell = 240;
    protected override Size MeasureOverride(Size available)
    {
        var width = double.IsInfinity(available.Width) ? 960 : Math.Max(1, available.Width);
        var count = Math.Max(1, InternalChildren.Count);
        columns = Math.Min(count, Math.Max(1, (int)Math.Ceiling(Math.Sqrt(count))));
        cell = Math.Min(300, width / columns);
        if (ViewportHeight > 0)
        {
            var best = 0d;
            for (var c = 1; c <= count; c++)
            {
                var candidate = Math.Min(300, Math.Min(width / c, ViewportHeight / Math.Ceiling(count / (double)c)));
                if (candidate > best) { best = candidate; columns = c; cell = candidate; }
            }
        }
        foreach (UIElement child in InternalChildren) child.Measure(new Size(cell, cell));
        return new Size(width, Math.Ceiling(InternalChildren.Count / (double)columns) * cell);
    }
    protected override Size ArrangeOverride(Size finalSize)
    {
        var left = Math.Max(0, (finalSize.Width - columns * cell) / 2);
        for (var i = 0; i < InternalChildren.Count; i++)
            InternalChildren[i].Arrange(new Rect(left + i % columns * cell, i / columns * cell, cell, cell));
        return finalSize;
    }
}

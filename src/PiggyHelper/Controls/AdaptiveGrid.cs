using System.Windows;
using System.Windows.Controls;

namespace PiggyHelper.Controls;

/// <summary>
/// 自适应网格：根据可用宽度自动决定一行放几个，每个格子等宽撑满，窗口大小变化时也整齐。
/// </summary>
public class AdaptiveGrid : Panel
{
    public static readonly DependencyProperty MinItemWidthProperty = DependencyProperty.Register(
        nameof(MinItemWidth), typeof(double), typeof(AdaptiveGrid),
        new FrameworkPropertyMetadata(200.0, FrameworkPropertyMetadataOptions.AffectsMeasure));

    public static readonly DependencyProperty SpacingProperty = DependencyProperty.Register(
        nameof(Spacing), typeof(double), typeof(AdaptiveGrid),
        new FrameworkPropertyMetadata(16.0, FrameworkPropertyMetadataOptions.AffectsMeasure));

    public static readonly DependencyProperty MaxColumnsProperty = DependencyProperty.Register(
        nameof(MaxColumns), typeof(int), typeof(AdaptiveGrid),
        new FrameworkPropertyMetadata(0, FrameworkPropertyMetadataOptions.AffectsMeasure));

    public static readonly DependencyProperty EvenColumnsProperty = DependencyProperty.Register(
        nameof(EvenColumns), typeof(bool), typeof(AdaptiveGrid),
        new FrameworkPropertyMetadata(false, FrameworkPropertyMetadataOptions.AffectsMeasure));

    public double MinItemWidth { get => (double)GetValue(MinItemWidthProperty); set => SetValue(MinItemWidthProperty, value); }
    public double Spacing { get => (double)GetValue(SpacingProperty); set => SetValue(SpacingProperty, value); }
    /// <summary>最多几列（0 表示不限）。</summary>
    public int MaxColumns { get => (int)GetValue(MaxColumnsProperty); set => SetValue(MaxColumnsProperty, value); }
    /// <summary>只用双数列（比如 4 个格子排成 2×2 或 4×1，不会排成 3+1）。</summary>
    public bool EvenColumns { get => (bool)GetValue(EvenColumnsProperty); set => SetValue(EvenColumnsProperty, value); }

    private int Columns(double width)
    {
        if (double.IsInfinity(width) || width <= 0) return Math.Max(1, MaxColumns);
        int cols = Math.Max(1, (int)((width + Spacing) / (MinItemWidth + Spacing)));
        if (MaxColumns > 0) cols = Math.Min(cols, MaxColumns);
        if (EvenColumns && cols > 1 && cols % 2 == 1) cols--;
        return cols;
    }

    private double ItemWidth(double width, int cols) =>
        double.IsInfinity(width) ? MinItemWidth : Math.Max(0, (width - Spacing * (cols - 1)) / cols);

    protected override Size MeasureOverride(Size available)
    {
        int cols = Columns(available.Width);
        double itemW = ItemWidth(available.Width, cols);
        double total = 0, rowH = 0;
        int i = 0;
        foreach (UIElement child in InternalChildren)
        {
            child.Measure(new Size(itemW, double.PositiveInfinity));
            rowH = Math.Max(rowH, child.DesiredSize.Height);
            if (++i % cols == 0) { total += rowH + Spacing; rowH = 0; }
        }
        if (i % cols != 0) total += rowH; else if (i > 0) total -= Spacing;
        double w = double.IsInfinity(available.Width) ? cols * itemW + (cols - 1) * Spacing : available.Width;
        return new Size(w, Math.Max(0, total));
    }

    protected override Size ArrangeOverride(Size finalSize)
    {
        int cols = Columns(finalSize.Width);
        double itemW = ItemWidth(finalSize.Width, cols);
        var children = InternalChildren.Cast<UIElement>().ToList();
        double y = 0;
        for (int r = 0; r * cols < children.Count; r++)
        {
            var row = children.Skip(r * cols).Take(cols).ToList();
            double rowH = row.Max(c => c.DesiredSize.Height);
            for (int c = 0; c < row.Count; c++)
                row[c].Arrange(new Rect(c * (itemW + Spacing), y, itemW, rowH));
            y += rowH + Spacing;
        }
        return finalSize;
    }
}

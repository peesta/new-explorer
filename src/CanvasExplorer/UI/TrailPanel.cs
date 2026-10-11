using System.Windows;
using System.Windows.Controls;

namespace CanvasExplorer.UI;

/// <summary>
/// A name followed by trailing marks (tag dots): the marks sit right after the text, and when space runs out
/// the name gives way (trims) first, never the marks. First child = the name, second = the marks.
/// </summary>
public sealed class TrailPanel : Panel
{
    protected override Size MeasureOverride(Size available)
    {
        if (Children.Count < 2) return base.MeasureOverride(available);
        var trail = Children[1];
        trail.Measure(new Size(double.PositiveInfinity, available.Height));
        var nameWidth = Math.Max(0, available.Width - trail.DesiredSize.Width);
        Children[0].Measure(new Size(nameWidth, available.Height));
        return new Size(Children[0].DesiredSize.Width + trail.DesiredSize.Width,
            Math.Max(Children[0].DesiredSize.Height, trail.DesiredSize.Height));
    }

    protected override Size ArrangeOverride(Size final)
    {
        if (Children.Count < 2) return final;
        var trail = Children[1];
        var nameWidth = Math.Min(Children[0].DesiredSize.Width, Math.Max(0, final.Width - trail.DesiredSize.Width));
        Children[0].Arrange(new Rect(0, 0, nameWidth, final.Height));
        trail.Arrange(new Rect(nameWidth, 0, trail.DesiredSize.Width, final.Height));
        return final;
    }
}

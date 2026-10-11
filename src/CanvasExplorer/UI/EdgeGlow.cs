using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;

namespace CanvasExplorer.UI;

/// <summary>
/// Outline overlay for resizing: the grabbed side is drawn fully in the accent colour, and the
/// neighbouring sides fade from accent back to the normal outline shortly after the corners.
/// </summary>
public sealed class EdgeGlow : Border
{
    const double Solid = 14;  // accent continues this far past the corner
    const double Fade = 46;   // then fades out over this distance

    readonly LinearGradientBrush _brush;
    readonly GradientStop _from = new(Colors.Transparent, 0);
    readonly GradientStop _to = new(Colors.Transparent, 1);
    ResizeEdges _edges;

    public EdgeGlow()
    {
        _brush = new LinearGradientBrush
        {
            MappingMode = BrushMappingMode.Absolute,
            SpreadMethod = GradientSpreadMethod.Pad,
            GradientStops = { _from, _to },
        };
        BorderThickness = new Thickness(1.5);
        BorderBrush = _brush;
        IsHitTestVisible = false;
        // Hidden (not Collapsed) keeps the real size, which the gradient geometry depends on.
        Visibility = Visibility.Hidden;
        SizeChanged += (_, _) => { if (_edges != ResizeEdges.None) Show(_edges); };
    }

    public void Show(ResizeEdges edges)
    {
        if (edges == ResizeEdges.None)
        {
            Hide();
            return;
        }
        _edges = edges;
        var accent = (TryFindResource("AccentBrush") as SolidColorBrush)?.Color ?? Colors.CornflowerBlue;
        _from.Color = Color.FromArgb(0, accent.R, accent.G, accent.B);
        _to.Color = accent;

        double w = ActualWidth, h = ActualHeight;
        // Gradient runs from the inside of the frame towards the grabbed side(s).
        double sx = w / 2, sy = h / 2, ex = w / 2, ey = h / 2;
        if (edges.HasFlag(ResizeEdges.Left)) { sx = Solid + Fade; ex = Solid; }
        if (edges.HasFlag(ResizeEdges.Right)) { sx = w - Solid - Fade; ex = w - Solid; }
        if (edges.HasFlag(ResizeEdges.Top)) { sy = Solid + Fade; ey = Solid; }
        if (edges.HasFlag(ResizeEdges.Bottom)) { sy = h - Solid - Fade; ey = h - Solid; }
        _brush.StartPoint = new Point(sx, sy);
        _brush.EndPoint = new Point(ex, ey);
        Visibility = Visibility.Visible;
    }

    public void Hide()
    {
        _edges = ResizeEdges.None;
        Visibility = Visibility.Hidden;
    }
}

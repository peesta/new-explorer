using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;

namespace CanvasExplorer.UI;

/// <summary>
/// Outline overlay that lights up in the accent colour around one point of the border
/// (where the user grabs it) and fades out along the edge.
/// </summary>
public sealed class EdgeGlow : Border
{
    const double Radius = 110;

    readonly RadialGradientBrush _brush;
    readonly GradientStop _inner = new(Colors.Transparent, 0);
    readonly GradientStop _mid = new(Colors.Transparent, 0.45);
    readonly GradientStop _outer = new(Colors.Transparent, 1);

    public EdgeGlow()
    {
        _brush = new RadialGradientBrush
        {
            MappingMode = BrushMappingMode.Absolute,
            RadiusX = Radius,
            RadiusY = Radius,
            GradientStops = { _inner, _mid, _outer },
        };
        BorderThickness = new Thickness(1.5);
        BorderBrush = _brush;
        IsHitTestVisible = false;
        Visibility = Visibility.Collapsed;
    }

    /// <summary>Lights the outline around <paramref name="center"/> (in this element's coordinates).</summary>
    public void Show(Point center)
    {
        var accent = (TryFindResource("AccentBrush") as SolidColorBrush)?.Color ?? Colors.CornflowerBlue;
        _inner.Color = accent;
        _mid.Color = Color.FromArgb(150, accent.R, accent.G, accent.B);
        _outer.Color = Color.FromArgb(0, accent.R, accent.G, accent.B);
        _brush.Center = center;
        _brush.GradientOrigin = center;
        Visibility = Visibility.Visible;
    }

    public void Hide() => Visibility = Visibility.Collapsed;
}

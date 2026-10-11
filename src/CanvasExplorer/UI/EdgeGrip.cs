using System.Windows;
using System.Windows.Controls;
using System.Windows.Controls.Primitives;
using System.Windows.Input;
using System.Windows.Media;

namespace CanvasExplorer.UI;

/// <summary>
/// Invisible vertical splitter. While hovered or dragged it lights the right side of
/// <see cref="Glow"/> instead of showing a handle.
/// </summary>
public sealed class EdgeGrip : Thumb
{
    public EdgeGrip()
    {
        Cursor = Cursors.SizeWE;
        var root = new FrameworkElementFactory(typeof(Grid));
        root.SetValue(Panel.BackgroundProperty, Brushes.Transparent);
        Template = new ControlTemplate(typeof(Thumb)) { VisualTree = root };
    }

    /// <summary>Outline overlay of the panel this grip resizes (its edge on <see cref="Side"/> gets lit).</summary>
    public EdgeGlow? Glow { get; set; }

    /// <summary>Which edge of the panel the grip is on (right when the panel is on the left, and vice versa).</summary>
    public ResizeEdges Side { get; set; } = ResizeEdges.Right;

    protected override void OnMouseMove(MouseEventArgs e)
    {
        base.OnMouseMove(e);
        Glow?.Show(Side);
    }

    protected override void OnMouseLeave(MouseEventArgs e)
    {
        base.OnMouseLeave(e);
        if (!IsDragging) Glow?.Hide();
    }

    protected override void OnLostMouseCapture(MouseEventArgs e)
    {
        base.OnLostMouseCapture(e);
        if (!IsMouseOver) Glow?.Hide();
    }
}

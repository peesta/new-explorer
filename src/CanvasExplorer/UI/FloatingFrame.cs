using System.Windows;
using System.Windows.Automation.Peers;
using System.Windows.Controls;
using System.Windows.Controls.Primitives;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Media.Effects;
using System.Windows.Media.Media3D;

namespace CanvasExplorer.UI;

[Flags]
public enum ResizeEdges
{
    None = 0,
    Left = 1,
    Top = 2,
    Right = 4,
    Bottom = 8,
    Horizontal = Left | Right,
    All = Left | Top | Right | Bottom,
}

/// <summary>
/// A window that lives on the canvas: moved by its drag handle, resized from any allowed edge.
/// Hovering an edge tints the outline with the accent colour, strongest next to the cursor.
/// </summary>
public class FloatingFrame : Grid
{
    const double GripZone = 6;

    public static readonly DependencyProperty IsDragHandleProperty = DependencyProperty.RegisterAttached(
        "IsDragHandle", typeof(bool), typeof(FloatingFrame), new PropertyMetadata(false));

    public static void SetIsDragHandle(DependencyObject d, bool value) => d.SetValue(IsDragHandleProperty, value);
    public static bool GetIsDragHandle(DependencyObject d) => (bool)d.GetValue(IsDragHandleProperty);

    enum Mode { None, Move, Resize }

    readonly Border _shadow;
    readonly Border _host;
    readonly Border _ring;
    readonly EdgeGlow _glow = new() { CornerRadius = new CornerRadius(6) };
    Mode _mode;
    ResizeEdges _active;
    Point _start;
    Rect _startBounds;
    bool _overrideCursor;
    bool _selected;

    public ResizeEdges Edges { get; set; } = ResizeEdges.All;
    public bool IsMovable { get; set; } = true;
    public double MinW { get; set; } = 180;
    public double MinH { get; set; } = 100;

    /// <summary>Optional magnet: gets the proposed bounds and the edges being dragged (None = move).</summary>
    public Func<Rect, ResizeEdges, Rect>? Snap { get; set; }

    /// <summary>Mouse went down anywhere inside the frame.</summary>
    public event EventHandler? Activated;
    /// <summary>A move or resize finished.</summary>
    public event EventHandler? BoundsCommitted;

    public FloatingFrame()
    {
        _shadow = new Border
        {
            CornerRadius = new CornerRadius(7),
            Background = Brushes.Black,
            Margin = new Thickness(2),
            IsHitTestVisible = false,
            Effect = new DropShadowEffect { BlurRadius = 30, ShadowDepth = 10, Direction = 270, Opacity = 0.6, Color = Colors.Black },
        };
        _host = new Border();
        _ring = new Border
        {
            BorderThickness = new Thickness(1.5),
            CornerRadius = new CornerRadius(8),
            Margin = new Thickness(-3),
            IsHitTestVisible = false,
            Visibility = Visibility.Collapsed,
        };
        _ring.SetResourceReference(Border.BorderBrushProperty, "AccentBrush");

        Children.Add(_shadow);
        Children.Add(_host);
        Children.Add(_glow);
        Children.Add(_ring);
    }

    // Panels are invisible to screen readers and UI automation; a window on the canvas should not be.
    protected override AutomationPeer OnCreateAutomationPeer() => new FramePeer(this);

    sealed class FramePeer(FloatingFrame owner) : FrameworkElementAutomationPeer(owner)
    {
        protected override AutomationControlType GetAutomationControlTypeCore() => AutomationControlType.Pane;
        protected override string GetClassNameCore() => nameof(FloatingFrame);
    }

    public UIElement? Child
    {
        get => _host.Child;
        set => _host.Child = value;
    }

    public double FrameCornerRadius
    {
        get => _glow.CornerRadius.TopLeft;
        set
        {
            _glow.CornerRadius = new CornerRadius(value);
            _shadow.CornerRadius = new CornerRadius(value + 1);
            _ring.CornerRadius = new CornerRadius(value + 2);
        }
    }

    public bool HasShadow
    {
        get => _shadow.Visibility == Visibility.Visible;
        set => _shadow.Visibility = value ? Visibility.Visible : Visibility.Collapsed;
    }

    public bool IsSelected
    {
        get => _selected;
        set
        {
            if (_selected == value) return;
            _selected = value;
            UpdateRing();
            OnSelectedChanged();
        }
    }

    protected virtual void OnSelectedChanged() { }

    // While an edge is lit (hover or resize) the selection outline steps aside so the two don't blend.
    void UpdateRing() =>
        _ring.Visibility = _selected && !_glow.IsVisible ? Visibility.Visible : Visibility.Collapsed;

    public Rect Bounds
    {
        get
        {
            var x = Canvas.GetLeft(this);
            var y = Canvas.GetTop(this);
            return new Rect(double.IsNaN(x) ? 0 : x, double.IsNaN(y) ? 0 : y,
                double.IsNaN(Width) ? ActualWidth : Width, double.IsNaN(Height) ? ActualHeight : Height);
        }
        set
        {
            Canvas.SetLeft(this, Math.Round(value.X));
            Canvas.SetTop(this, Math.Round(value.Y));
            Width = Math.Round(Math.Max(MinW, value.Width));
            Height = Math.Round(Math.Max(MinH, value.Height));
        }
    }

    public bool IsInteracting => _mode != Mode.None;

    IInputElement? Reference => VisualTreeHelper.GetParent(this) as IInputElement;

    ResizeEdges HitEdges(Point p)
    {
        var e = ResizeEdges.None;
        if (p.X <= GripZone) e |= ResizeEdges.Left;
        else if (p.X >= ActualWidth - GripZone) e |= ResizeEdges.Right;
        if (p.Y <= GripZone) e |= ResizeEdges.Top;
        else if (p.Y >= ActualHeight - GripZone) e |= ResizeEdges.Bottom;
        return e & Edges;
    }

    protected override void OnPreviewMouseMove(MouseEventArgs e)
    {
        base.OnPreviewMouseMove(e);
        if (_mode != Mode.None)
        {
            if (Reference is { } r) Apply(e.GetPosition(r) - _start);
            UpdateHover(_mode == Mode.Resize ? _active : ResizeEdges.None, e.GetPosition(this));
            e.Handled = true;
            return;
        }
        if (e.LeftButton == MouseButtonState.Pressed || e.RightButton == MouseButtonState.Pressed)
        {
            UpdateHover(ResizeEdges.None, default);
            return;
        }
        var p = e.GetPosition(this);
        UpdateHover(HitEdges(p), p);
    }

    protected override void OnMouseLeave(MouseEventArgs e)
    {
        base.OnMouseLeave(e);
        if (_mode == Mode.None) UpdateHover(ResizeEdges.None, default);
    }

    void UpdateHover(ResizeEdges edges, Point p)
    {
        if (edges == ResizeEdges.None)
        {
            _glow.Hide();
            UpdateRing();
            if (_overrideCursor)
            {
                Mouse.OverrideCursor = null;
                _overrideCursor = false;
            }
            return;
        }

        Mouse.OverrideCursor = edges switch
        {
            ResizeEdges.Left or ResizeEdges.Right => Cursors.SizeWE,
            ResizeEdges.Top or ResizeEdges.Bottom => Cursors.SizeNS,
            ResizeEdges.Left | ResizeEdges.Top or ResizeEdges.Right | ResizeEdges.Bottom => Cursors.SizeNWSE,
            _ => Cursors.SizeNESW,
        };
        _overrideCursor = true;

        _glow.Show(edges);
        UpdateRing();
    }

    // Wheel input inside a window never reaches the canvas, even when a list or text has
    // scrolled to its end.
    protected override void OnMouseWheel(MouseWheelEventArgs e)
    {
        base.OnMouseWheel(e);
        e.Handled = true;
    }

    protected override void OnPreviewMouseLeftButtonDown(MouseButtonEventArgs e)
    {
        base.OnPreviewMouseLeftButtonDown(e);
        Activated?.Invoke(this, EventArgs.Empty);
        var edges = HitEdges(e.GetPosition(this));
        if (edges != ResizeEdges.None)
        {
            Begin(Mode.Resize, edges, e);
            return;
        }
        if (IsMovable && IsInDragHandle(e.OriginalSource as DependencyObject))
            Begin(Mode.Move, ResizeEdges.None, e);
    }

    protected override void OnPreviewMouseRightButtonDown(MouseButtonEventArgs e)
    {
        base.OnPreviewMouseRightButtonDown(e);
        Activated?.Invoke(this, EventArgs.Empty);
    }

    void Begin(Mode mode, ResizeEdges edges, MouseButtonEventArgs e)
    {
        if (Reference is not { } r) return;
        _mode = mode;
        _active = edges;
        _start = e.GetPosition(r);
        _startBounds = Bounds;
        CaptureMouse();
        e.Handled = true;
    }

    protected override void OnPreviewMouseLeftButtonUp(MouseButtonEventArgs e)
    {
        base.OnPreviewMouseLeftButtonUp(e);
        if (_mode == Mode.None) return;
        End();
        e.Handled = true;
        var p = e.GetPosition(this);
        UpdateHover(IsMouseOver ? HitEdges(p) : ResizeEdges.None, p);
    }

    protected override void OnLostMouseCapture(MouseEventArgs e)
    {
        base.OnLostMouseCapture(e);
        if (_mode != Mode.None) End();
    }

    void End()
    {
        _mode = Mode.None;
        if (IsMouseCaptured) ReleaseMouseCapture();
        BoundsCommitted?.Invoke(this, EventArgs.Empty);
    }

    void Apply(Vector d)
    {
        var b = _startBounds;
        Rect next;
        if (_mode == Mode.Move)
        {
            next = new Rect(b.X + d.X, b.Y + d.Y, b.Width, b.Height);
        }
        else
        {
            double x = b.X, y = b.Y, w = b.Width, h = b.Height;
            if (_active.HasFlag(ResizeEdges.Left))
            {
                w = Math.Max(MinW, b.Width - d.X);
                x = b.Right - w;
            }
            if (_active.HasFlag(ResizeEdges.Right)) w = Math.Max(MinW, b.Width + d.X);
            if (_active.HasFlag(ResizeEdges.Top))
            {
                h = Math.Max(MinH, b.Height - d.Y);
                y = b.Bottom - h;
            }
            if (_active.HasFlag(ResizeEdges.Bottom)) h = Math.Max(MinH, b.Height + d.Y);
            next = new Rect(x, y, w, h);
        }
        if (Snap != null) next = Snap(next, _mode == Mode.Move ? ResizeEdges.None : _active);
        Bounds = next;
    }

    bool IsInDragHandle(DependencyObject? d)
    {
        while (d != null && d != this)
        {
            if (d is ButtonBase or TextBoxBase) return false;
            if (GetIsDragHandle(d)) return true;
            d = d is Visual or Visual3D ? VisualTreeHelper.GetParent(d) : LogicalTreeHelper.GetParent(d);
        }
        return false;
    }
}

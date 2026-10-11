using System.Windows;
using System.Windows.Automation;
using System.Windows.Automation.Peers;
using System.Windows.Automation.Provider;
using System.Windows.Input;
using System.Windows.Media;

namespace CanvasExplorer.Previews;

/// <summary>Thin progress line with a knob: played part in accent colour. Click or drag to seek.</summary>
public sealed class SeekBar : FrameworkElement
{
    double _progress;

    public event EventHandler<double>? SeekRequested;

    public SeekBar()
    {
        Cursor = Cursors.Hand;
        Height = 14;
        AutomationProperties.SetName(this, "Перемотка");
    }

    public double Progress
    {
        get => _progress;
        set { var v = Math.Clamp(value, 0, 1); if (Math.Abs(_progress - v) > 0.0005) { _progress = v; InvalidateVisual(); } }
    }

    protected override void OnRender(DrawingContext dc)
    {
        var w = ActualWidth;
        var mid = ActualHeight / 2;
        dc.DrawRectangle(Brushes.Transparent, null, new Rect(RenderSize));
        if (w < 4) return;
        var accent = (Brush)FindResource("AccentBrush");
        var rest = new SolidColorBrush(Color.FromRgb(0x3A, 0x3A, 0x3A));
        dc.DrawRoundedRectangle(rest, null, new Rect(0, mid - 1.5, w, 3), 1.5, 1.5);
        var x = _progress * w;
        dc.DrawRoundedRectangle(accent, null, new Rect(0, mid - 1.5, x, 3), 1.5, 1.5);
        if (IsMouseOver || IsMouseCaptured) dc.DrawEllipse(Brushes.White, null, new Point(Math.Clamp(x, 5, w - 5), mid), 5, 5);
    }

    protected override void OnMouseEnter(MouseEventArgs e) { base.OnMouseEnter(e); InvalidateVisual(); }
    protected override void OnMouseLeave(MouseEventArgs e) { base.OnMouseLeave(e); InvalidateVisual(); }

    protected override void OnMouseLeftButtonDown(MouseButtonEventArgs e)
    {
        base.OnMouseLeftButtonDown(e);
        CaptureMouse();
        SeekRequested?.Invoke(this, Math.Clamp(e.GetPosition(this).X / ActualWidth, 0, 1));
        e.Handled = true;
    }

    protected override void OnMouseMove(MouseEventArgs e)
    {
        base.OnMouseMove(e);
        if (IsMouseCaptured) SeekRequested?.Invoke(this, Math.Clamp(e.GetPosition(this).X / ActualWidth, 0, 1));
    }

    protected override void OnMouseLeftButtonUp(MouseButtonEventArgs e)
    {
        base.OnMouseLeftButtonUp(e);
        ReleaseMouseCapture();
    }

    protected override AutomationPeer OnCreateAutomationPeer() => new Peer(this);

    /// <summary>Screen readers and UI automation see a slider (0…100 %).</summary>
    sealed class Peer(SeekBar owner) : FrameworkElementAutomationPeer(owner), IRangeValueProvider
    {
        protected override AutomationControlType GetAutomationControlTypeCore() => AutomationControlType.Slider;
        public override object? GetPattern(PatternInterface p) => p == PatternInterface.RangeValue ? this : base.GetPattern(p);
        public double Value => Math.Round(owner.Progress * 100, 1);
        public bool IsReadOnly => false;
        public double Maximum => 100;
        public double Minimum => 0;
        public double LargeChange => 10;
        public double SmallChange => 1;
        public void SetValue(double value) => owner.SeekRequested?.Invoke(owner, Math.Clamp(value / 100, 0, 1));
    }
}

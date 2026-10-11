using System.IO;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media;
using CanvasExplorer.Models;
using CanvasExplorer.Services;
using CanvasExplorer.UI;

namespace CanvasExplorer.Previews;

/// <summary>
/// Audio file on the canvas: waveform with seek, play/pause, repeat, volume. Playback itself belongs to the app-wide
/// <see cref="AudioEngine"/> (it goes on in the sidebar's mini player when this window or its folder is closed);
/// this window shows and controls it while its file is the one loaded. The file is not kept open.
/// </summary>
public sealed class AudioPreview : PreviewContent
{
    static AudioEngine Engine => AudioEngine.Current;

    readonly WaveformView _wave = new();
    readonly VolumeBar _volume = new();
    readonly Button _playBtn;
    readonly UI.Icon _playIcon;
    readonly Button _loopBtn;
    readonly TextBlock _time;
    readonly TextBlock _meta;
    TimeSpan _duration;
    /// <summary>Where play starts while another file (or none) is loaded in the player.</summary>
    TimeSpan _start;
    bool _unloaded;

    public AudioPreview(FileItem item) : base(item)
    {
        var mono = new FontFamily("Cascadia Mono, Consolas");

        _playIcon = new UI.Icon(UI.Icons.Play, 12) { Filled = true };
        _playBtn = new Button { Width = 30, Height = 30, Content = _playIcon, Style = (Style)Application.Current.FindResource("RoundAccentButton"), ToolTip = "Воспроизвести (пробел)", IsEnabled = false };
        _playBtn.Click += (_, _) => Toggle();

        var startBtn = SmallButton(UI.Icons.SkipBack, "В начало", () => Seek(0), filled: true);
        _loopBtn = SmallButton(UI.Icons.Loop, "Повтор", () => Engine.Loop = !Engine.Loop);

        _time = new TextBlock { FontFamily = mono, FontSize = 11, VerticalAlignment = VerticalAlignment.Center, Margin = new Thickness(8, 0, 0, 0), Text = "0:00 / 0:00" };
        _time.SetResourceReference(TextBlock.ForegroundProperty, "SubBrush");

        var volIcon = new UI.Icon(UI.Icons.Volume, 14) { VerticalAlignment = VerticalAlignment.Center, Margin = new Thickness(0, 0, 6, 0) };
        volIcon.SetResourceReference(UI.Icon.ForegroundProperty, "SubBrush");
        _volume.Width = 64;
        _volume.Height = 16;
        _volume.VerticalAlignment = VerticalAlignment.Center;
        // One volume for the whole app: the last level set anywhere.
        _volume.Value = Engine.Volume;
        _volume.ValueChanged += (_, v) => Engine.Volume = v;

        var left = new StackPanel { Orientation = Orientation.Horizontal, VerticalAlignment = VerticalAlignment.Center };
        left.Children.Add(startBtn);
        left.Children.Add(new Border { Width = 4 });
        left.Children.Add(_playBtn);
        left.Children.Add(new Border { Width = 4 });
        left.Children.Add(_loopBtn);
        left.Children.Add(_time);

        var right = new StackPanel { Orientation = Orientation.Horizontal, VerticalAlignment = VerticalAlignment.Center };
        right.Children.Add(volIcon);
        right.Children.Add(_volume);

        var controls = new DockPanel { Height = 32, LastChildFill = false };
        DockPanel.SetDock(left, Dock.Left);
        DockPanel.SetDock(right, Dock.Right);
        controls.Children.Add(left);
        controls.Children.Add(right);

        _wave.SeekRequested += (_, f) => Seek(f);
        _wave.Margin = new Thickness(0, 4, 0, 6);

        var grid = new Grid { Margin = new Thickness(8, 4, 8, 4) };
        grid.RowDefinitions.Add(new RowDefinition());
        grid.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });
        Grid.SetRow(controls, 1);
        grid.Children.Add(_wave);
        grid.Children.Add(controls);
        Child = grid;
        Focusable = true;
        KeyDown += (_, e) => { if (e.Key == Key.Space) { Toggle(); e.Handled = true; } };

        _meta = new TextBlock { FontFamily = mono, FontSize = 10, VerticalAlignment = VerticalAlignment.Center };
        _meta.SetResourceReference(TextBlock.ForegroundProperty, "MutedBrush");

        Engine.Changed += OnEngine;
        Engine.Tick += OnTick;
        _ = LoadAsync();
        UpdateView();
    }

    public override FrameworkElement? HeaderExtra => _meta;

    public override bool HandleKey(Key key)
    {
        if (key != Key.Space) return false;
        Toggle();
        return true;
    }

    public override Size PreferredSize => new(460, 136);

    bool IsCurrent => Engine.IsCurrent(Item.FullPath);

    async Task LoadAsync()
    {
        var path = Item.FullPath;
        try
        {
            var info = await AudioEngine.ReadInfoAsync(path);
            _duration = info.Duration;
            _meta.Text = Item.Ext + " · " + (info.SampleRate / 1000.0).ToString("0.#") + " кГц · " +
                         (info.Channels == 1 ? "моно" : info.Channels == 2 ? "стерео" : info.Channels + " кан.");
            _playBtn.IsEnabled = true;
            UpdateView();
        }
        catch
        {
            _meta.Text = "формат не поддерживается";
            return;
        }

        try
        {
            var peaks = await AudioEngine.PeaksAsync(path);
            if (!_unloaded) _wave.Peaks = peaks;
        }
        catch
        {
            // Waveform is optional; playback still works.
        }
    }

    void Toggle()
    {
        if (!_playBtn.IsEnabled) return;
        Engine.Toggle(Item.FullPath, _start);
    }

    void Seek(double fraction)
    {
        var to = TimeSpan.FromTicks((long)(Duration.Ticks * Math.Clamp(fraction, 0, 1)));
        if (IsCurrent) Engine.Seek(to);
        else _start = to;
        UpdateView();
    }

    TimeSpan Duration => IsCurrent ? Engine.Duration : _duration;
    TimeSpan Position => IsCurrent ? Engine.Position : _start;

    void OnEngine(object? sender, EventArgs e) => UpdateView();

    void OnTick(object? sender, EventArgs e)
    {
        if (IsCurrent) UpdatePosition();
    }

    void UpdateView()
    {
        var playing = IsCurrent && Engine.IsPlaying;
        _playIcon.Data = playing ? UI.Icons.Pause : UI.Icons.Play;
        _playBtn.ToolTip = playing ? "Пауза (пробел)" : "Воспроизвести (пробел)";
        System.Windows.Automation.AutomationProperties.SetName(_playBtn, playing ? "Пауза" : "Воспроизвести");
        _loopBtn.SetResourceReference(Control.ForegroundProperty, Engine.Loop ? "AccentBrush" : "SubBrush");
        if (Math.Abs(_volume.Value - Engine.Volume) > 0.0005) _volume.Value = Engine.Volume;
        UpdatePosition();
    }

    void UpdatePosition()
    {
        var total = Duration;
        var cur = Position;
        _wave.Progress = total.Ticks > 0 ? (double)cur.Ticks / total.Ticks : 0;
        _time.Text = AudioEngine.Format(cur) + " / " + AudioEngine.Format(total);
    }

    Button SmallButton(Geometry icon, string tip, Action click, bool filled = false)
    {
        var b = new Button
        {
            Width = 24,
            Height = 24,
            ToolTip = tip,
            Content = new UI.Icon(icon, 13) { Filled = filled, StrokeWidth = 2 },
            Style = (Style)Application.Current.FindResource("IconButton"),
        };
        b.SetResourceReference(Control.ForegroundProperty, filled ? "TextBrush" : "SubBrush");
        b.Click += (_, _) => click();
        return b;
    }

    /// <summary>The window goes away; the music does not (it carries on in the sidebar player).</summary>
    public override void Unload()
    {
        _unloaded = true;
        Engine.Changed -= OnEngine;
        Engine.Tick -= OnTick;
    }
}

/// <summary>Bar waveform; played part in accent colour. Click or drag to seek.</summary>
public sealed class WaveformView : FrameworkElement
{
    float[]? _peaks;
    double _progress;

    public event EventHandler<double>? SeekRequested;

    public WaveformView()
    {
        Cursor = Cursors.Hand;
        SnapsToDevicePixels = true;
    }

    public float[]? Peaks
    {
        get => _peaks;
        set { _peaks = value; InvalidateVisual(); }
    }

    public double Progress
    {
        get => _progress;
        set { if (Math.Abs(_progress - value) > 0.0005) { _progress = value; InvalidateVisual(); } }
    }

    protected override void OnRender(DrawingContext dc)
    {
        var w = ActualWidth;
        var h = ActualHeight;
        dc.DrawRectangle(Brushes.Transparent, null, new Rect(0, 0, w, h));
        if (w < 4 || h < 4) return;
        var accent = (Brush)FindResource("AccentBrush");
        var rest = new SolidColorBrush(Color.FromRgb(0x3A, 0x3A, 0x3A));
        const double bar = 2, gap = 2;
        var count = (int)(w / (bar + gap));
        var mid = h / 2;
        for (var i = 0; i < count; i++)
        {
            double v;
            if (_peaks is { Length: > 0 } p)
            {
                var a = i * p.Length / count;
                var b = Math.Max(a + 1, (i + 1) * p.Length / count);
                v = 0;
                for (var k = a; k < b && k < p.Length; k++) v = Math.Max(v, p[k]);
            }
            else v = 0.08;
            var bh = Math.Max(2, v * (h - 2));
            var x = i * (bar + gap);
            dc.DrawRoundedRectangle((double)i / count < _progress ? accent : rest, null,
                new Rect(x, mid - bh / 2, bar, bh), 1, 1);
        }
        var px = Math.Round(_progress * w);
        dc.DrawRectangle(Brushes.White, null, new Rect(Math.Min(px, w - 2), 0, 2, h));
    }

    protected override void OnMouseLeftButtonDown(MouseButtonEventArgs e)
    {
        base.OnMouseLeftButtonDown(e);
        CaptureMouse();
        SeekRequested?.Invoke(this, e.GetPosition(this).X / ActualWidth);
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
}

/// <summary>Thin horizontal volume slider.</summary>
public sealed class VolumeBar : FrameworkElement
{
    double _value = 0.8;

    public event EventHandler<double>? ValueChanged;

    public VolumeBar()
    {
        Cursor = Cursors.Hand;
        System.Windows.Automation.AutomationProperties.SetName(this, "Громкость");
        System.Windows.Automation.AutomationProperties.SetAutomationId(this, "Volume");
    }

    protected override System.Windows.Automation.Peers.AutomationPeer OnCreateAutomationPeer() => new VolumePeer(this);

    /// <summary>Screen readers and UI automation see the bar as a slider (0…100).</summary>
    sealed class VolumePeer(VolumeBar owner) : System.Windows.Automation.Peers.FrameworkElementAutomationPeer(owner),
        System.Windows.Automation.Provider.IRangeValueProvider
    {
        protected override System.Windows.Automation.Peers.AutomationControlType GetAutomationControlTypeCore() =>
            System.Windows.Automation.Peers.AutomationControlType.Slider;

        public override object? GetPattern(System.Windows.Automation.Peers.PatternInterface p) =>
            p == System.Windows.Automation.Peers.PatternInterface.RangeValue ? this : base.GetPattern(p);

        public double Value => Math.Round(owner.Value * 100);
        public bool IsReadOnly => false;
        public double Maximum => 100;
        public double Minimum => 0;
        public double LargeChange => 10;
        public double SmallChange => 5;
        public void SetValue(double value) => owner.Value = value / 100;
    }

    public double Value
    {
        get => _value;
        set
        {
            var v = Math.Clamp(value, 0, 1);
            if (Math.Abs(v - _value) < 1e-9) return;
            _value = v;
            InvalidateVisual();
            ValueChanged?.Invoke(this, _value);
        }
    }

    protected override void OnRender(DrawingContext dc)
    {
        var w = ActualWidth;
        var mid = ActualHeight / 2;
        dc.DrawRectangle(Brushes.Transparent, null, new Rect(0, 0, w, ActualHeight));
        dc.DrawRoundedRectangle(new SolidColorBrush(Color.FromRgb(0x3A, 0x3A, 0x3A)), null, new Rect(0, mid - 1.5, w, 3), 1.5, 1.5);
        dc.DrawRoundedRectangle(new SolidColorBrush(Color.FromRgb(0xA3, 0xA3, 0xA3)), null, new Rect(0, mid - 1.5, w * _value, 3), 1.5, 1.5);
    }

    protected override void OnMouseLeftButtonDown(MouseButtonEventArgs e)
    {
        base.OnMouseLeftButtonDown(e);
        CaptureMouse();
        Value = e.GetPosition(this).X / ActualWidth;
        e.Handled = true;
    }

    protected override void OnMouseMove(MouseEventArgs e)
    {
        base.OnMouseMove(e);
        if (IsMouseCaptured) Value = e.GetPosition(this).X / ActualWidth;
    }

    protected override void OnMouseLeftButtonUp(MouseButtonEventArgs e)
    {
        base.OnMouseLeftButtonUp(e);
        ReleaseMouseCapture();
    }

    protected override void OnMouseWheel(MouseWheelEventArgs e)
    {
        base.OnMouseWheel(e);
        Value += e.Delta > 0 ? 0.05 : -0.05;
        e.Handled = true;
    }
}

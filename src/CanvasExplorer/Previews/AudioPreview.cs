using System.IO;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Threading;
using CanvasExplorer.Models;
using CanvasExplorer.UI;
using NAudio.Wave;

namespace CanvasExplorer.Previews;

/// <summary>Audio player: waveform with seek, play/pause, loop, volume. One player sounds at a time.</summary>
public sealed class AudioPreview : PreviewContent
{
    static AudioPreview? _playing;

    readonly WaveformView _wave = new();
    readonly VolumeBar _volume = new();
    readonly Button _playBtn;
    readonly Icon _playIcon;
    readonly Button _loopBtn;
    readonly TextBlock _time;
    readonly TextBlock _meta;
    readonly DispatcherTimer _tick;
    readonly CancellationTokenSource _cts = new();
    WaveStream? _reader;
    WaveOutEvent? _output;
    bool _loop;
    bool _stoppingByUser;

    public AudioPreview(FileItem item) : base(item)
    {
        var mono = new FontFamily("Cascadia Mono, Consolas");

        _playIcon = new Icon(UI.Icons.Play, 12) { Filled = true };
        _playBtn = new Button { Width = 30, Height = 30, Content = _playIcon, Style = (Style)Application.Current.FindResource("RoundAccentButton"), ToolTip = "Воспроизвести (пробел)" };
        _playBtn.Click += (_, _) => Toggle();

        var startBtn = SmallButton(UI.Icons.SkipBack, "В начало", () => Seek(0), filled: true);
        _loopBtn = SmallButton(UI.Icons.Loop, "Повтор", ToggleLoop);

        _time = new TextBlock { FontFamily = mono, FontSize = 11, VerticalAlignment = VerticalAlignment.Center, Margin = new Thickness(8, 0, 0, 0), Text = "0:00 / 0:00" };
        _time.SetResourceReference(TextBlock.ForegroundProperty, "SubBrush");

        var volIcon = new Icon(UI.Icons.Volume, 14) { VerticalAlignment = VerticalAlignment.Center, Margin = new Thickness(0, 0, 6, 0) };
        volIcon.SetResourceReference(Icon.ForegroundProperty, "SubBrush");
        _volume.Width = 64;
        _volume.Height = 16;
        _volume.VerticalAlignment = VerticalAlignment.Center;
        _volume.ValueChanged += (_, v) => { if (_output != null) _output.Volume = (float)v; };

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

        _tick = new DispatcherTimer { Interval = TimeSpan.FromMilliseconds(40) };
        _tick.Tick += (_, _) => UpdatePosition();

        _ = LoadAsync();
    }

    public override FrameworkElement? HeaderExtra => _meta;

    public override bool HandleKey(Key key)
    {
        if (key != Key.Space) return false;
        Toggle();
        return true;
    }
    public override Size PreferredSize => new(460, 136);

    async Task LoadAsync()
    {
        var path = Item.FullPath;
        try
        {
            _reader = await Task.Run(() => (WaveStream)new MediaFoundationReader(path));
            var f = _reader.WaveFormat;
            _meta.Text = Item.Ext + " · " + (f.SampleRate / 1000.0).ToString("0.#") + " кГц · " +
                         (f.Channels == 1 ? "моно" : f.Channels == 2 ? "стерео" : f.Channels + " кан.");
            UpdatePosition();
        }
        catch
        {
            _meta.Text = "формат не поддерживается";
            _playBtn.IsEnabled = false;
            return;
        }

        try
        {
            var token = _cts.Token;
            _wave.Peaks = await Task.Run(() => ComputePeaks(path, 800, token), token);
        }
        catch
        {
            // Waveform is optional; playback still works.
        }
    }

    static float[] ComputePeaks(string path, int buckets, CancellationToken token)
    {
        using var reader = new MediaFoundationReader(path);
        var provider = reader.ToSampleProvider();
        var bytesPerSample = Math.Max(1, reader.WaveFormat.BitsPerSample / 8);
        var totalSamples = Math.Max(1, reader.Length / bytesPerSample);
        var perBucket = Math.Max(1, totalSamples / buckets);
        var peaks = new float[buckets];
        var buffer = new float[8192];
        long index = 0;
        int read;
        while ((read = provider.Read(buffer, 0, buffer.Length)) > 0)
        {
            token.ThrowIfCancellationRequested();
            for (var i = 0; i < read; i++, index++)
            {
                var b = (int)Math.Min(buckets - 1, index / perBucket);
                var v = Math.Abs(buffer[i]);
                if (v > peaks[b]) peaks[b] = v;
            }
        }
        var max = peaks.Max();
        if (max > 0) for (var i = 0; i < peaks.Length; i++) peaks[i] /= max;
        return peaks;
    }

    void EnsureOutput()
    {
        if (_output != null || _reader == null) return;
        _output = new WaveOutEvent { DesiredLatency = 120 };
        _output.Init(_reader);
        _output.Volume = (float)_volume.Value;
        _output.PlaybackStopped += (_, _) => Dispatcher.BeginInvoke(OnStopped);
    }

    void Toggle()
    {
        if (_reader == null) return;
        EnsureOutput();
        if (_output!.PlaybackState == PlaybackState.Playing)
        {
            Pause();
            return;
        }
        if (_playing != null && _playing != this) _playing.Pause();
        _playing = this;
        if (_reader.CurrentTime >= _reader.TotalTime - TimeSpan.FromMilliseconds(50)) _reader.CurrentTime = TimeSpan.Zero;
        _output.Play();
        _tick.Start();
        SetPlayingVisual(true);
    }

    void Pause()
    {
        if (_output?.PlaybackState != PlaybackState.Playing) return;
        _stoppingByUser = true;
        _output.Pause();
        _stoppingByUser = false;
        _tick.Stop();
        SetPlayingVisual(false);
        UpdatePosition();
    }

    void OnStopped()
    {
        if (_stoppingByUser || _reader == null) return;
        // Reached the end of the file.
        _reader.CurrentTime = TimeSpan.Zero;
        if (_loop && _output != null)
        {
            _output.Play();
            return;
        }
        _tick.Stop();
        SetPlayingVisual(false);
        UpdatePosition();
    }

    void Seek(double fraction)
    {
        if (_reader == null) return;
        _reader.CurrentTime = TimeSpan.FromTicks((long)(_reader.TotalTime.Ticks * Math.Clamp(fraction, 0, 1)));
        UpdatePosition();
    }

    void ToggleLoop()
    {
        _loop = !_loop;
        _loopBtn.SetResourceReference(Control.ForegroundProperty, _loop ? "AccentBrush" : "SubBrush");
    }

    void SetPlayingVisual(bool playing)
    {
        _playIcon.Data = playing ? UI.Icons.Pause : UI.Icons.Play;
        _playBtn.ToolTip = playing ? "Пауза (пробел)" : "Воспроизвести (пробел)";
    }

    void UpdatePosition()
    {
        if (_reader == null) return;
        var total = _reader.TotalTime;
        var cur = _reader.CurrentTime;
        _wave.Progress = total.Ticks > 0 ? (double)cur.Ticks / total.Ticks : 0;
        _time.Text = Fmt(cur) + " / " + Fmt(total);
    }

    static string Fmt(TimeSpan t) => t.TotalHours >= 1 ? t.ToString(@"h\:mm\:ss") : t.ToString(@"m\:ss");

    Button SmallButton(Geometry icon, string tip, Action click, bool filled = false)
    {
        var b = new Button
        {
            Width = 24,
            Height = 24,
            ToolTip = tip,
            Content = new Icon(icon, 13) { Filled = filled, StrokeWidth = 2 },
            Style = (Style)Application.Current.FindResource("IconButton"),
        };
        b.SetResourceReference(Control.ForegroundProperty, filled ? "TextBrush" : "SubBrush");
        b.Click += (_, _) => click();
        return b;
    }

    public override void Unload()
    {
        _cts.Cancel();
        _tick.Stop();
        if (_playing == this) _playing = null;
        _stoppingByUser = true;
        _output?.Stop();
        _output?.Dispose();
        _reader?.Dispose();
        _output = null;
        _reader = null;
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
    }

    public double Value
    {
        get => _value;
        set { _value = Math.Clamp(value, 0, 1); InvalidateVisual(); ValueChanged?.Invoke(this, _value); }
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

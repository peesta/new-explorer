using System.Windows;
using System.Windows.Automation;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Shapes;
using CanvasExplorer.Models;
using CanvasExplorer.Services;

namespace CanvasExplorer.Previews;

/// <summary>
/// Video on the canvas: picture, seek line, play/pause, repeat, this file's volume. Playback belongs to the app-wide
/// <see cref="VideoEngine"/>, so it goes on in the sidebar's video player when this window or its folder is closed;
/// both show the same frames. Until it plays the window shows the Windows thumbnail of the file.
/// </summary>
public sealed class VideoPreview : PreviewContent
{
    const double ControlsHeight = 52;
    static VideoEngine Engine => VideoEngine.Current;

    readonly Image _poster = new() { Stretch = Stretch.Uniform };
    readonly Rectangle _picture = new();
    readonly Border _bigPlay;
    readonly TextBlock _error;
    readonly SeekBar _seek = new() { Margin = new Thickness(0, 4, 0, 0) };
    readonly VolumeBar _volume = new() { Width = 64, Height = 16, VerticalAlignment = VerticalAlignment.Center };
    readonly Button _playBtn;
    readonly UI.Icon _playIcon = new(UI.Icons.Play, 12) { Filled = true };
    readonly Button _loopBtn;
    readonly TextBlock _time;
    readonly TextBlock _meta;
    TimeSpan _duration;
    TimeSpan _start;
    bool _shownCurrent;
    bool _sized;

    public VideoPreview(FileItem item) : base(item)
    {
        var mono = new FontFamily("Cascadia Mono, Consolas");
        AutomationProperties.SetAutomationId(_picture, "VideoPicture");
        RenderOptions.SetBitmapScalingMode(_poster, BitmapScalingMode.HighQuality);

        var playGlyph = new UI.Icon(UI.Icons.Play, 18) { Filled = true, Foreground = Brushes.White, Margin = new Thickness(3, 0, 0, 0), HorizontalAlignment = HorizontalAlignment.Center, VerticalAlignment = VerticalAlignment.Center };
        _bigPlay = new Border
        {
            Width = 46, Height = 46, CornerRadius = new CornerRadius(23), Background = new SolidColorBrush(Color.FromArgb(0x99, 0, 0, 0)),
            HorizontalAlignment = HorizontalAlignment.Center, VerticalAlignment = VerticalAlignment.Center, Child = playGlyph, IsHitTestVisible = false,
        };
        _error = new TextBlock { Foreground = Brushes.White, TextWrapping = TextWrapping.Wrap, TextAlignment = TextAlignment.Center, Margin = new Thickness(12), VerticalAlignment = VerticalAlignment.Center, Visibility = Visibility.Collapsed };
        var screen = new Grid { Background = Brushes.Black, Cursor = Cursors.Hand, ClipToBounds = true };
        screen.Children.Add(_poster);
        screen.Children.Add(_picture);
        screen.Children.Add(_bigPlay);
        screen.Children.Add(_error);
        screen.MouseLeftButtonDown += (_, e) =>
        {
            // Click: play/pause. Double click: open in the system player.
            if (e.ClickCount == 2) Shell.Open(Item.FullPath);
            else Toggle();
            e.Handled = true;
        };

        _seek.SeekRequested += (_, f) => Seek(f);

        _playBtn = new Button { Width = 30, Height = 30, Content = _playIcon, Style = (Style)Application.Current.FindResource("RoundAccentButton"), ToolTip = "Воспроизвести (пробел)" };
        AutomationProperties.SetName(_playBtn, "Воспроизвести");
        _playBtn.Click += (_, _) => Toggle();
        var startBtn = SmallButton(UI.Icons.SkipBack, "В начало", () => Seek(0), filled: true);
        _loopBtn = SmallButton(UI.Icons.Loop, "Повтор", () => Engine.Loop = !Engine.Loop);
        _time = new TextBlock { FontFamily = mono, FontSize = 11, VerticalAlignment = VerticalAlignment.Center, Margin = new Thickness(8, 0, 0, 0), Text = "0:00 / 0:00" };
        _time.SetResourceReference(TextBlock.ForegroundProperty, "SubBrush");
        AutomationProperties.SetAutomationId(_time, "VideoWindowTime");

        var volIcon = new UI.Icon(UI.Icons.Volume, 14) { VerticalAlignment = VerticalAlignment.Center, Margin = new Thickness(0, 0, 6, 0) };
        volIcon.SetResourceReference(UI.Icon.ForegroundProperty, "SubBrush");
        _volume.Value = Engine.VolumeOf(item.FullPath);
        _volume.ValueChanged += (_, v) => Engine.SetVolume(Item.FullPath, v);

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
        var controls = new DockPanel { Height = 34, LastChildFill = false };
        DockPanel.SetDock(left, Dock.Left);
        DockPanel.SetDock(right, Dock.Right);
        controls.Children.Add(left);
        controls.Children.Add(right);

        var grid = new Grid();
        grid.RowDefinitions.Add(new RowDefinition());
        grid.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });
        grid.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });
        Grid.SetRow(_seek, 1);
        Grid.SetRow(controls, 2);
        grid.Children.Add(new Border { CornerRadius = new CornerRadius(4), ClipToBounds = true, Child = screen });
        grid.Children.Add(_seek);
        grid.Children.Add(controls);
        Child = new Border { Padding = new Thickness(4, 2, 4, 0), Child = grid };
        Focusable = true;

        _meta = new TextBlock { FontFamily = mono, FontSize = 10, VerticalAlignment = VerticalAlignment.Center, Text = item.Ext };
        _meta.SetResourceReference(TextBlock.ForegroundProperty, "MutedBrush");

        Engine.Changed += OnEngine;
        Engine.Tick += OnTick;
        Engine.Opened += OnOpened;
        Engine.Failed += OnFailed;
        Engine.VolumeChanged += OnVolume;
        _ = LoadPosterAsync();
        UpdateView();
    }

    public override FrameworkElement? HeaderExtra => _meta;
    public override Size PreferredSize => new(480, 330);

    public override bool HandleKey(Key key)
    {
        if (key != Key.Space) return false;
        Toggle();
        return true;
    }

    bool IsCurrent => Engine.IsCurrent(Item.FullPath);

    async Task LoadPosterAsync()
    {
        var px = (int)Math.Ceiling(480 * Math.Max(1, VisualTreeHelper.GetDpi(this).DpiScaleX));
        var bmp = await ShellThumbnails.GetAsync(Item.FullPath, Item.Modified, Math.Min(px, 960), CancellationToken.None);
        if (bmp == null) return;
        _poster.Source = bmp;
        FitTo(bmp.PixelWidth, bmp.PixelHeight);
    }

    /// <summary>The window takes the picture's proportions (plus the controls under it).</summary>
    void FitTo(double w, double h)
    {
        if (_sized || w <= 0 || h <= 0) return;
        _sized = true;
        var width = PreferredSize.Width - 8;
        RaiseNaturalSize(new Size(w, h + ControlsHeight * w / width));
    }

    void Toggle()
    {
        _error.Visibility = Visibility.Collapsed;
        Engine.Toggle(Item.FullPath, _start);
    }

    void Seek(double fraction)
    {
        var to = TimeSpan.FromTicks((long)(Duration.Ticks * Math.Clamp(fraction, 0, 1)));
        if (IsCurrent) Engine.Seek(to);
        else _start = to;
        UpdatePosition();
    }

    TimeSpan Duration => IsCurrent && Engine.Duration > TimeSpan.Zero ? Engine.Duration : _duration;
    TimeSpan Position => IsCurrent ? Engine.Position : _start;

    void OnEngine(object? sender, EventArgs e) => UpdateView();

    void OnTick(object? sender, EventArgs e)
    {
        if (IsCurrent) UpdatePosition();
    }

    void OnOpened(object? sender, EventArgs e)
    {
        if (!IsCurrent) return;
        _duration = Engine.Duration;
        var size = Engine.NaturalSize;
        if (!size.IsEmpty)
        {
            _meta.Text = Item.Ext + " · " + size.Width + "×" + size.Height;
            FitTo(size.Width, size.Height);
        }
        _shownCurrent = false;
        UpdateView();
    }

    void OnFailed(object? sender, string path)
    {
        if (!string.Equals(path, Item.FullPath, StringComparison.OrdinalIgnoreCase)) return;
        _error.Text = "Не удалось воспроизвести этот файл (нет нужного кодека?).\nДвойной щелчок — открыть в системном плеере.";
        _error.Visibility = Visibility.Visible;
        UpdateView();
    }

    void OnVolume(object? sender, string path)
    {
        if (!string.Equals(path, Item.FullPath, StringComparison.OrdinalIgnoreCase)) return;
        var v = Engine.VolumeOf(path);
        if (Math.Abs(_volume.Value - v) > 0.0005) _volume.Value = v;
    }

    void UpdateView()
    {
        var current = IsCurrent;
        var playing = current && Engine.IsPlaying;
        // While this file is loaded the window shows the live picture, otherwise its thumbnail.
        if (current != _shownCurrent || (current && _picture.Fill == null && Engine.IsOpen))
        {
            _shownCurrent = current;
            _picture.Fill = current && Engine.IsOpen ? Engine.CreateBrush() : null;
        }
        _poster.Visibility = current && Engine.IsOpen ? Visibility.Collapsed : Visibility.Visible;
        _bigPlay.Visibility = playing || _error.Visibility == Visibility.Visible ? Visibility.Collapsed : Visibility.Visible;
        _playIcon.Data = playing ? UI.Icons.Pause : UI.Icons.Play;
        _playBtn.ToolTip = playing ? "Пауза (пробел)" : "Воспроизвести (пробел)";
        AutomationProperties.SetName(_playBtn, playing ? "Пауза" : "Воспроизвести");
        _loopBtn.SetResourceReference(Control.ForegroundProperty, Engine.Loop ? "AccentBrush" : "SubBrush");
        if (current && Engine.Duration > TimeSpan.Zero) _duration = Engine.Duration;
        UpdatePosition();
    }

    void UpdatePosition()
    {
        var total = Duration;
        var cur = Position;
        _seek.Progress = total.Ticks > 0 ? (double)cur.Ticks / total.Ticks : 0;
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

    /// <summary>The window goes away; the video does not (it carries on in the sidebar's video player).</summary>
    public override void Unload()
    {
        Engine.Changed -= OnEngine;
        Engine.Tick -= OnTick;
        Engine.Opened -= OnOpened;
        Engine.Failed -= OnFailed;
        Engine.VolumeChanged -= OnVolume;
        _picture.Fill = null;
    }
}

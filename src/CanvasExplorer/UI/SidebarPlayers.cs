using System.Windows;
using System.Windows.Automation;
using System.Windows.Automation.Peers;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Shapes;
using CanvasExplorer.Previews;
using CanvasExplorer.Services;

namespace CanvasExplorer.UI;

/// <summary>
/// A player at the bottom of the sidebar (audio or video). It shows up when its kind of media starts playing on the
/// canvas and keeps playing across tabs and folders, also after the window or folder it came from is closed; × stops it.
/// Its height changes by dragging its top edge (its width follows the sidebar); dragging its title row reorders the
/// players.
/// </summary>
public sealed class SidebarPlayer : Border
{
    const double ResizeZone = 5;

    readonly IMediaEngine _engine;
    readonly bool _video;
    readonly TextBlock _title;
    readonly TextBlock _folder;
    readonly FrameworkElement _titleRow;
    readonly WaveformView? _wave;
    readonly Rectangle? _picture;
    readonly SeekBar? _seek;
    readonly VolumeBar _volume = new() { Height = 16, VerticalAlignment = VerticalAlignment.Center };
    readonly Button _play;
    readonly Icon _playIcon = new(Icons.Play, 11) { Filled = true };
    readonly Button _loop;
    readonly TextBlock _time;
    readonly Action<string> _showInFolder;
    string? _shownPath;
    bool _resizing;
    double _resizeFromY, _resizeFromH;
    Point? _dragFrom;

    /// <summary>The user pressed the title row and moves vertically: the dock reorders (argument: pointer Y in the dock).</summary>
    public event EventHandler<MouseEventArgs>? ReorderMove;
    public event EventHandler? ReorderEnd;

    public string Kind => _video ? "video" : "audio";
    public bool IsReordering { get; private set; }

    public SidebarPlayer(IMediaEngine engine, bool video, Action<string> showInFolder)
    {
        _engine = engine;
        _video = video;
        _showInFolder = showInFolder;
        var id = video ? "Video" : "Mini";
        AutomationProperties.SetAutomationId(this, video ? "VideoPlayer" : "MiniPlayer");
        AutomationProperties.SetName(this, video ? "Видеоплеер" : "Аудиоплеер");
        CornerRadius = new CornerRadius(6);
        BorderThickness = new Thickness(1);
        Padding = new Thickness(9, 7, 7, 7);
        Margin = new Thickness(6, 0, 6, 6);
        SetResourceReference(BackgroundProperty, "WidgetBrush");
        SetResourceReference(BorderBrushProperty, "WidgetLineBrush");
        Visibility = Visibility.Collapsed;
        Height = StoredHeight;

        var kindIcon = new Icon(video ? Icons.Video : Icons.Music, 13) { VerticalAlignment = VerticalAlignment.Center, Margin = new Thickness(0, 0, 7, 0) };
        kindIcon.SetResourceReference(Icon.ForegroundProperty, "AccentBrush");
        var close = new Button { Width = 20, Height = 20, ToolTip = "Остановить и закрыть", Content = new Icon(Icons.Close, 10, 2.4), Style = (Style)Application.Current.FindResource("IconButton") };
        close.SetResourceReference(Control.ForegroundProperty, "SubBrush");
        AutomationProperties.SetAutomationId(close, id + "Close");
        close.Click += (_, _) => _engine.Close();
        _title = new TextBlock { FontWeight = FontWeights.SemiBold, FontSize = 12, TextTrimming = TextTrimming.CharacterEllipsis, VerticalAlignment = VerticalAlignment.Center };
        _title.SetResourceReference(TextBlock.ForegroundProperty, "TextBrush");
        AutomationProperties.SetAutomationId(_title, id + "Title");
        var top = new DockPanel { Background = Brushes.Transparent, Cursor = Cursors.SizeAll, ToolTip = "Перетащите, чтобы поменять плееры местами" };
        ToolTipService.SetInitialShowDelay(top, 1500);
        DockPanel.SetDock(kindIcon, Dock.Left);
        DockPanel.SetDock(close, Dock.Right);
        top.Children.Add(kindIcon);
        top.Children.Add(close);
        top.Children.Add(_title);
        _titleRow = top;

        // The folder it plays from: click to go there and see the file.
        _folder = new TextBlock { FontSize = 11, TextTrimming = TextTrimming.CharacterEllipsis, Cursor = Cursors.Hand, Margin = new Thickness(20, 1, 0, 0), ToolTip = "Показать в папке" };
        _folder.SetResourceReference(TextBlock.ForegroundProperty, "MutedBrush");
        _folder.MouseLeftButtonUp += (_, _) => { if (_engine.Path is { } p) _showInFolder(p); };

        FrameworkElement art;
        if (video)
        {
            // The picture: the same frames as the window on the canvas. Click = play/pause.
            _picture = new Rectangle { Cursor = Cursors.Hand };
            AutomationProperties.SetAutomationId(_picture, "VideoPicture");
            var screen = new Border { Background = Brushes.Black, CornerRadius = new CornerRadius(4), Margin = new Thickness(0, 6, 0, 4), Child = _picture, ClipToBounds = true };
            screen.MouseLeftButtonUp += (_, _) => TogglePlay();
            _seek = new SeekBar { Margin = new Thickness(0, 0, 0, 2) };
            _seek.SeekRequested += (_, f) => _engine.Seek(TimeSpan.FromTicks((long)(_engine.Duration.Ticks * f)));
            var g = new Grid();
            g.RowDefinitions.Add(new RowDefinition());
            g.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });
            Grid.SetRow(_seek, 1);
            g.Children.Add(screen);
            g.Children.Add(_seek);
            art = g;
            ((VideoEngine)engine).Opened += (_, _) => ShowPicture(force: true);
        }
        else
        {
            _wave = new WaveformView { Margin = new Thickness(0, 6, 0, 4), MinHeight = 16 };
            _wave.SeekRequested += (_, f) => _engine.Seek(TimeSpan.FromTicks((long)(_engine.Duration.Ticks * Math.Clamp(f, 0, 1))));
            art = _wave;
        }

        var back = Small(Icons.SkipBack, "В начало", () => _engine.Seek(TimeSpan.Zero), filled: true);
        _play = new Button { Width = 26, Height = 26, Content = _playIcon, Style = (Style)Application.Current.FindResource("RoundAccentButton"), Margin = new Thickness(3, 0, 3, 0) };
        AutomationProperties.SetAutomationId(_play, id + "Play");
        _play.Click += (_, _) => TogglePlay();
        _loop = Small(Icons.Loop, "Повтор", () => _engine.Loop = !_engine.Loop);
        _time = new TextBlock { FontFamily = new FontFamily("Cascadia Mono, Consolas"), FontSize = 10.5, VerticalAlignment = VerticalAlignment.Center, Margin = new Thickness(6, 0, 0, 0) };
        _time.SetResourceReference(TextBlock.ForegroundProperty, "SubBrush");
        AutomationProperties.SetAutomationId(_time, id + "Time");
        var controls = new StackPanel { Orientation = Orientation.Horizontal };
        controls.Children.Add(back);
        controls.Children.Add(_play);
        controls.Children.Add(_loop);
        controls.Children.Add(_time);

        var volIcon = new Icon(Icons.Volume, 13) { VerticalAlignment = VerticalAlignment.Center, Margin = new Thickness(0, 0, 6, 0) };
        volIcon.SetResourceReference(Icon.ForegroundProperty, "SubBrush");
        // The volume of the file being played (each file has its own).
        _volume.ValueChanged += (_, v) => { if (_engine.Path is { } p) _engine.SetVolume(p, v); };
        var volume = new DockPanel { Margin = new Thickness(2, 6, 2, 0) };
        DockPanel.SetDock(volIcon, Dock.Left);
        volume.Children.Add(volIcon);
        volume.Children.Add(_volume);

        var grid = new Grid();
        for (var i = 0; i < 5; i++) grid.RowDefinitions.Add(new RowDefinition { Height = i == 2 ? new GridLength(1, GridUnitType.Star) : GridLength.Auto });
        void Row(UIElement e, int r) { Grid.SetRow(e, r); grid.Children.Add(e); }
        Row(top, 0);
        Row(_folder, 1);
        Row(art, 2);
        Row(controls, 3);
        Row(volume, 4);
        Child = grid;

        ContextMenu = new ContextMenu();
        ContextMenuOpening += (_, _) => ContextMenu = Menus.Build(
            Menus.Item(_engine.IsPlaying ? "Пауза" : "Воспроизвести", _engine.IsPlaying ? Icons.Pause : Icons.Play, TogglePlay),
            Menus.Item("Показать в папке", Icons.Folder, () => { if (_engine.Path is { } p) _showInFolder(p); }),
            Menus.Item("Обычный размер", Icons.Fit, () => SetHeight(video ? 230 : 134)),
            Menus.Sep(),
            Menus.Item("Остановить и закрыть", Icons.Close, _engine.Close, danger: true));

        top.PreviewMouseLeftButtonDown += (_, e) =>
        {
            if (FindAncestor<Button>(e.OriginalSource as DependencyObject) != null) return;
            _dragFrom = e.GetPosition(this);
        };

        _engine.Changed += (_, _) => Update();
        _engine.Tick += (_, _) => UpdatePosition();
        _engine.VolumeChanged += (_, path) => { if (_engine.IsCurrent(path)) SyncVolume(); };
        Update();
    }

    double MinH => _video ? 150 : 112;
    double MaxH => _video ? 640 : 380;

    double StoredHeight
    {
        get => Math.Clamp(_video ? AppSettings.Current.VideoPlayerHeight : AppSettings.Current.AudioPlayerHeight, MinH, MaxH);
        set
        {
            if (_video) AppSettings.Current.VideoPlayerHeight = value;
            else AppSettings.Current.AudioPlayerHeight = value;
            AppSettings.ScheduleSave();
        }
    }

    void SetHeight(double h)
    {
        Height = Math.Clamp(h, MinH, MaxH);
        StoredHeight = Height;
    }

    void TogglePlay()
    {
        if (_engine.Path is { } p) _engine.Toggle(p, TimeSpan.Zero);
    }

    Button Small(Geometry icon, string tip, Action click, bool filled = false)
    {
        var b = new Button { Width = 22, Height = 22, ToolTip = tip, Content = new Icon(icon, 12) { Filled = filled, StrokeWidth = 2 }, Style = (Style)Application.Current.FindResource("IconButton") };
        b.SetResourceReference(Control.ForegroundProperty, filled ? "TextBrush" : "SubBrush");
        b.Click += (_, _) => click();
        return b;
    }

    // ---------- state ----------

    void Update()
    {
        var path = _engine.Path;
        Visibility = path == null ? Visibility.Collapsed : Visibility.Visible;
        if (path == null)
        {
            _shownPath = null;
            if (_picture != null) _picture.Fill = null;
            return;
        }
        _title.Text = System.IO.Path.GetFileName(path);
        _title.ToolTip = path;
        _folder.Text = System.IO.Path.GetFileName(System.IO.Path.GetDirectoryName(path)) ?? "";
        var playing = _engine.IsPlaying;
        _playIcon.Data = playing ? Icons.Pause : Icons.Play;
        _play.ToolTip = playing ? "Пауза" : "Воспроизвести";
        AutomationProperties.SetName(_play, playing ? "Пауза" : "Воспроизвести");
        _loop.SetResourceReference(Control.ForegroundProperty, _engine.Loop ? "AccentBrush" : "SubBrush");
        if (_shownPath != path)
        {
            _shownPath = path;
            SyncVolume();
            if (_wave != null)
            {
                _wave.Peaks = null;
                _ = LoadPeaks(path);
            }
            ShowPicture(force: true);
        }
        UpdatePosition();
    }

    void ShowPicture(bool force)
    {
        if (_picture == null) return;
        if (force || _picture.Fill == null) _picture.Fill = ((VideoEngine)_engine).CreateBrush();
    }

    void SyncVolume()
    {
        if (_engine.Path is not { } p) return;
        var v = _engine.VolumeOf(p);
        if (Math.Abs(_volume.Value - v) > 0.0005) _volume.Value = v;
    }

    async Task LoadPeaks(string path)
    {
        try
        {
            var peaks = await AudioEngine.PeaksAsync(path);
            if (_shownPath == path && _wave != null) _wave.Peaks = peaks;
        }
        catch { }
    }

    void UpdatePosition()
    {
        if (_engine.Path == null) return;
        var total = _engine.Duration;
        var cur = _engine.Position;
        var f = total.Ticks > 0 ? (double)cur.Ticks / total.Ticks : 0;
        if (_wave != null) _wave.Progress = f;
        if (_seek != null) _seek.Progress = f;
        _time.Text = AudioEngine.Format(cur) + " / " + AudioEngine.Format(total);
    }

    // ---------- resizing by the top edge, reordering by the title row ----------

    protected override void OnPreviewMouseMove(MouseEventArgs e)
    {
        base.OnPreviewMouseMove(e);
        var p = e.GetPosition(this);
        if (_resizing)
        {
            var screenY = PointToScreen(p).Y / VisualTreeHelper.GetDpi(this).DpiScaleY;
            SetHeight(_resizeFromH + (_resizeFromY - screenY));
            e.Handled = true;
            return;
        }
        if (IsReordering)
        {
            ReorderMove?.Invoke(this, e);
            e.Handled = true;
            return;
        }
        if (_dragFrom is { } from && e.LeftButton == MouseButtonState.Pressed && Math.Abs(p.Y - from.Y) > 6)
        {
            _dragFrom = null;
            IsReordering = true;
            CaptureMouse();
            Opacity = 0.75;
            e.Handled = true;
            return;
        }
        Cursor = p.Y <= ResizeZone && e.LeftButton != MouseButtonState.Pressed ? Cursors.SizeNS : null;
    }

    protected override void OnPreviewMouseLeftButtonDown(MouseButtonEventArgs e)
    {
        base.OnPreviewMouseLeftButtonDown(e);
        var p = e.GetPosition(this);
        if (p.Y > ResizeZone) return;
        _resizing = true;
        _resizeFromY = PointToScreen(p).Y / VisualTreeHelper.GetDpi(this).DpiScaleY;
        _resizeFromH = ActualHeight;
        CaptureMouse();
        e.Handled = true;
    }

    protected override void OnPreviewMouseLeftButtonUp(MouseButtonEventArgs e)
    {
        base.OnPreviewMouseLeftButtonUp(e);
        _dragFrom = null;
        if (!_resizing && !IsReordering) return;
        EndDrag();
        e.Handled = true;
    }

    protected override void OnLostMouseCapture(MouseEventArgs e)
    {
        base.OnLostMouseCapture(e);
        if (_resizing || IsReordering) EndDrag();
    }

    protected override void OnMouseLeave(MouseEventArgs e)
    {
        base.OnMouseLeave(e);
        if (!_resizing) Cursor = null;
    }

    void EndDrag()
    {
        var reordering = IsReordering;
        _resizing = false;
        IsReordering = false;
        Opacity = 1;
        if (IsMouseCaptured) ReleaseMouseCapture();
        if (reordering) ReorderEnd?.Invoke(this, EventArgs.Empty);
    }

    static T? FindAncestor<T>(DependencyObject? d) where T : DependencyObject
    {
        for (; d != null; d = d is Visual ? VisualTreeHelper.GetParent(d) : LogicalTreeHelper.GetParent(d))
            if (d is T t) return t;
        return null;
    }

    // A Border is invisible to screen readers; the player should not be.
    protected override AutomationPeer OnCreateAutomationPeer() => new PlayerPeer(this);

    sealed class PlayerPeer(SidebarPlayer owner) : FrameworkElementAutomationPeer(owner)
    {
        protected override AutomationControlType GetAutomationControlTypeCore() => AutomationControlType.Pane;
        protected override string GetClassNameCore() => nameof(SidebarPlayer);
    }
}

/// <summary>The players at the bottom of the sidebar, in the user's order (drag a title row to swap them).</summary>
public sealed class PlayerDock : StackPanel
{
    public PlayerDock(params SidebarPlayer[] players)
    {
        var order = AppSettings.Current.PlayerOrder;
        foreach (var p in players.OrderBy(p => order.IndexOf(p.Kind) is var i && i >= 0 ? i : 99))
        {
            Children.Add(p);
            p.ReorderMove += (_, e) => Follow(p, e);
            p.ReorderEnd += (_, _) => Save();
        }
    }

    /// <summary>Moves the dragged player above or below the other visible ones as the pointer passes their middle.</summary>
    void Follow(SidebarPlayer moving, MouseEventArgs e)
    {
        var y = e.GetPosition(this).Y;
        var others = Children.OfType<SidebarPlayer>().Where(p => p != moving).ToList();
        // Its new place: after every visible player whose middle the pointer has passed.
        var target = 0;
        for (var i = 0; i < others.Count; i++)
        {
            var o = others[i];
            if (o.Visibility == Visibility.Visible && y > o.TranslatePoint(new Point(0, o.ActualHeight / 2), this).Y) target = i + 1;
        }
        if (Children.IndexOf(moving) == target) return;
        // The dragged player itself is never removed (that would drop its mouse capture): the others move around it.
        foreach (var o in others) Children.Remove(o);
        for (var i = 0; i < others.Count; i++) Children.Insert(i < target ? i : i + 1, others[i]);
    }

    void Save()
    {
        AppSettings.Current.PlayerOrder = Children.OfType<SidebarPlayer>().Select(p => p.Kind).ToList();
        AppSettings.ScheduleSave();
    }
}

/// <summary>
/// Vertical layout for the sidebar: the first child (favorites, drives, tags) takes the top and scrolls; the others
/// (the players) sit at the very bottom, just above the panel's lower edge, and never move.
/// </summary>
public sealed class SidebarStack : Panel
{
    protected override Size MeasureOverride(Size available)
    {
        double rest = 0, width = 0;
        for (var i = 1; i < Children.Count; i++)
        {
            Children[i].Measure(new Size(available.Width, double.PositiveInfinity));
            rest += Children[i].DesiredSize.Height;
            width = Math.Max(width, Children[i].DesiredSize.Width);
        }
        if (Children.Count > 0)
        {
            Children[0].Measure(new Size(available.Width, Math.Max(0, available.Height - rest)));
            width = Math.Max(width, Children[0].DesiredSize.Width);
        }
        return new Size(double.IsInfinity(available.Width) ? width : available.Width,
            double.IsInfinity(available.Height) ? rest + (Children.Count > 0 ? Children[0].DesiredSize.Height : 0) : available.Height);
    }

    protected override Size ArrangeOverride(Size final)
    {
        double rest = 0;
        for (var i = 1; i < Children.Count; i++) rest += Children[i].DesiredSize.Height;
        if (Children.Count > 0)
            Children[0].Arrange(new Rect(0, 0, final.Width, Math.Min(Children[0].DesiredSize.Height, Math.Max(0, final.Height - rest))));
        var y = Math.Max(0, final.Height - rest);
        for (var i = 1; i < Children.Count; i++)
        {
            Children[i].Arrange(new Rect(0, y, final.Width, Children[i].DesiredSize.Height));
            y += Children[i].DesiredSize.Height;
        }
        return final;
    }
}

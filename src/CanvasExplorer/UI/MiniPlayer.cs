using System.IO;
using System.Windows;
using System.Windows.Automation;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media;
using CanvasExplorer.Previews;
using CanvasExplorer.Services;

namespace CanvasExplorer.UI;

/// <summary>
/// The player at the bottom of the sidebar, under the tags. It appears when audio starts on the canvas and keeps
/// playing across tabs and folders, also after the window or folder it came from is closed. × stops it.
/// </summary>
public sealed class MiniPlayer : Border
{
    static AudioEngine Engine => AudioEngine.Current;

    readonly TextBlock _title;
    readonly TextBlock _folder;
    readonly WaveformView _wave = new() { Height = 22, Margin = new Thickness(0, 6, 0, 4) };
    readonly VolumeBar _volume = new() { Height = 16, VerticalAlignment = VerticalAlignment.Center };
    readonly Button _play;
    readonly Icon _playIcon = new(Icons.Play, 11) { Filled = true };
    readonly Button _loop;
    readonly TextBlock _time;
    readonly Action<string> _showInFolder;
    string? _peaksFor;

    public MiniPlayer(Action<string> showInFolder)
    {
        _showInFolder = showInFolder;
        AutomationProperties.SetAutomationId(this, "MiniPlayer");
        AutomationProperties.SetName(this, "Плеер");
        CornerRadius = new CornerRadius(6);
        BorderThickness = new Thickness(1);
        Padding = new Thickness(9, 7, 7, 7);
        Margin = new Thickness(6, 4, 6, 6);
        SetResourceReference(BackgroundProperty, "WidgetBrush");
        SetResourceReference(BorderBrushProperty, "WidgetLineBrush");
        Visibility = Visibility.Collapsed;

        var note = new Icon(Icons.Music, 13) { VerticalAlignment = VerticalAlignment.Center, Margin = new Thickness(0, 0, 7, 0) };
        note.SetResourceReference(Icon.ForegroundProperty, "AccentBrush");
        var close = new Button { Width = 20, Height = 20, ToolTip = "Остановить и закрыть", Content = new Icon(Icons.Close, 10, 2.4), Style = (Style)Application.Current.FindResource("IconButton") };
        close.SetResourceReference(Control.ForegroundProperty, "SubBrush");
        AutomationProperties.SetAutomationId(close, "MiniClose");
        close.Click += (_, _) => Engine.Close();
        _title = new TextBlock { FontWeight = FontWeights.SemiBold, FontSize = 12, TextTrimming = TextTrimming.CharacterEllipsis, VerticalAlignment = VerticalAlignment.Center };
        _title.SetResourceReference(TextBlock.ForegroundProperty, "TextBrush");
        AutomationProperties.SetAutomationId(_title, "MiniTitle");
        var top = new DockPanel();
        DockPanel.SetDock(note, Dock.Left);
        DockPanel.SetDock(close, Dock.Right);
        top.Children.Add(note);
        top.Children.Add(close);
        top.Children.Add(_title);

        // The folder it plays from: click to go there and see the file.
        _folder = new TextBlock { FontSize = 11, TextTrimming = TextTrimming.CharacterEllipsis, Cursor = Cursors.Hand, Margin = new Thickness(20, 1, 0, 0), ToolTip = "Показать в папке" };
        _folder.SetResourceReference(TextBlock.ForegroundProperty, "MutedBrush");
        _folder.MouseLeftButtonUp += (_, _) => { if (Engine.Path is { } p) _showInFolder(p); };

        _wave.SeekRequested += (_, f) => Engine.Seek(TimeSpan.FromTicks((long)(Engine.Duration.Ticks * Math.Clamp(f, 0, 1))));

        var back = Small(Icons.SkipBack, "В начало", () => Engine.Seek(TimeSpan.Zero), filled: true);
        _play = new Button { Width = 26, Height = 26, Content = _playIcon, Style = (Style)Application.Current.FindResource("RoundAccentButton"), Margin = new Thickness(3, 0, 3, 0) };
        AutomationProperties.SetAutomationId(_play, "MiniPlay");
        _play.Click += (_, _) => { if (Engine.Path is { } p) Engine.Toggle(p, TimeSpan.Zero); };
        _loop = Small(Icons.Loop, "Повтор", () => Engine.Loop = !Engine.Loop);
        _time = new TextBlock { FontFamily = new FontFamily("Cascadia Mono, Consolas"), FontSize = 10.5, VerticalAlignment = VerticalAlignment.Center, Margin = new Thickness(6, 0, 0, 0) };
        _time.SetResourceReference(TextBlock.ForegroundProperty, "SubBrush");
        AutomationProperties.SetAutomationId(_time, "MiniTime");
        var controls = new StackPanel { Orientation = Orientation.Horizontal };
        controls.Children.Add(back);
        controls.Children.Add(_play);
        controls.Children.Add(_loop);
        controls.Children.Add(_time);

        var volIcon = new Icon(Icons.Volume, 13) { VerticalAlignment = VerticalAlignment.Center, Margin = new Thickness(0, 0, 6, 0) };
        volIcon.SetResourceReference(Icon.ForegroundProperty, "SubBrush");
        _volume.Value = Engine.Volume;
        _volume.ValueChanged += (_, v) => Engine.Volume = v;
        var volume = new DockPanel { Margin = new Thickness(2, 6, 2, 0) };
        DockPanel.SetDock(volIcon, Dock.Left);
        volume.Children.Add(volIcon);
        volume.Children.Add(_volume);

        var stack = new StackPanel();
        stack.Children.Add(top);
        stack.Children.Add(_folder);
        stack.Children.Add(_wave);
        stack.Children.Add(controls);
        stack.Children.Add(volume);
        Child = stack;

        ContextMenu = new ContextMenu();
        ContextMenuOpening += (_, _) => ContextMenu = Menus.Build(
            Menus.Item(Engine.IsPlaying ? "Пауза" : "Воспроизвести", Engine.IsPlaying ? Icons.Pause : Icons.Play,
                () => { if (Engine.Path is { } p) Engine.Toggle(p, TimeSpan.Zero); }),
            Menus.Item("Показать в папке", Icons.Folder, () => { if (Engine.Path is { } p) _showInFolder(p); }),
            Menus.Sep(),
            Menus.Item("Остановить и закрыть", Icons.Close, Engine.Close, danger: true));

        Engine.Changed += (_, _) => Update();
        Engine.Tick += (_, _) => UpdatePosition();
        Update();
    }

    // A Border is invisible to screen readers; the player should not be.
    protected override System.Windows.Automation.Peers.AutomationPeer OnCreateAutomationPeer() => new PlayerPeer(this);

    sealed class PlayerPeer(MiniPlayer owner) : System.Windows.Automation.Peers.FrameworkElementAutomationPeer(owner)
    {
        protected override System.Windows.Automation.Peers.AutomationControlType GetAutomationControlTypeCore() =>
            System.Windows.Automation.Peers.AutomationControlType.Pane;
        protected override string GetClassNameCore() => nameof(MiniPlayer);
    }

    Button Small(Geometry icon, string tip, Action click, bool filled = false)
    {
        var b = new Button { Width = 22, Height = 22, ToolTip = tip, Content = new Icon(icon, 12) { Filled = filled, StrokeWidth = 2 }, Style = (Style)Application.Current.FindResource("IconButton") };
        b.SetResourceReference(Control.ForegroundProperty, filled ? "TextBrush" : "SubBrush");
        b.Click += (_, _) => click();
        return b;
    }

    void Update()
    {
        var path = Engine.Path;
        Visibility = path == null ? Visibility.Collapsed : Visibility.Visible;
        if (path == null) return;
        _title.Text = System.IO.Path.GetFileName(path);
        _title.ToolTip = path;
        _folder.Text = System.IO.Path.GetFileName(System.IO.Path.GetDirectoryName(path)) ?? "";
        var playing = Engine.IsPlaying;
        _playIcon.Data = playing ? Icons.Pause : Icons.Play;
        _play.ToolTip = playing ? "Пауза" : "Воспроизвести";
        AutomationProperties.SetName(_play, playing ? "Пауза" : "Воспроизвести");
        _loop.SetResourceReference(Control.ForegroundProperty, Engine.Loop ? "AccentBrush" : "SubBrush");
        if (Math.Abs(_volume.Value - Engine.Volume) > 0.0005) _volume.Value = Engine.Volume;
        if (_peaksFor != path)
        {
            _peaksFor = path;
            _wave.Peaks = null;
            _ = LoadPeaks(path);
        }
        UpdatePosition();
    }

    async Task LoadPeaks(string path)
    {
        try
        {
            var peaks = await AudioEngine.PeaksAsync(path);
            if (_peaksFor == path) _wave.Peaks = peaks;
        }
        catch { }
    }

    void UpdatePosition()
    {
        if (Engine.Path == null) return;
        var total = Engine.Duration;
        var cur = Engine.Position;
        _wave.Progress = total.Ticks > 0 ? (double)cur.Ticks / total.Ticks : 0;
        _time.Text = AudioEngine.Format(cur) + " / " + AudioEngine.Format(total);
    }
}

/// <summary>
/// Vertical layout for the sidebar: the first child (the scrolling sections) takes what is left after the others
/// (the player), so the player sits right under the tags and, when the list is long, stays in place while the list scrolls.
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
            rest += Children[0].DesiredSize.Height;
        }
        return new Size(double.IsInfinity(available.Width) ? width : available.Width, Math.Min(rest, available.Height));
    }

    protected override Size ArrangeOverride(Size final)
    {
        double rest = 0;
        for (var i = 1; i < Children.Count; i++) rest += Children[i].DesiredSize.Height;
        double y = 0;
        for (var i = 0; i < Children.Count; i++)
        {
            var h = i == 0 ? Math.Min(Children[0].DesiredSize.Height, Math.Max(0, final.Height - rest)) : Children[i].DesiredSize.Height;
            Children[i].Arrange(new Rect(0, y, final.Width, h));
            y += h;
        }
        return final;
    }
}

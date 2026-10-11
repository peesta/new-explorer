using System.IO;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Media.Animation;
using System.Windows.Threading;
using CanvasExplorer.Models;
using CanvasExplorer.Previews;
using CanvasExplorer.Services;

namespace CanvasExplorer.UI;

/// <summary>
/// The folder area. Without a canvas the file list fills it ("docked"). Dragging a side edge of the list
/// inward (or "Создать холст") turns the folder into a canvas: the list becomes a movable window and
/// files can be pinned around it as live previews.
/// </summary>
public partial class CanvasView : UserControl
{
    const double MinZoom = 0.25, MaxZoom = 3, DotStep = 20;

    readonly ScaleTransform _scale = new();
    readonly TranslateTransform _pan = new();
    readonly DrawingBrush _dots;
    readonly DispatcherTimer _saveTimer;
    readonly List<PreviewWindow> _pins = new();
    readonly FloatingFrame _listFrame = new();
    FileListView? _list;
    string? _folder;
    FolderLayout? _layout;
    PreviewWindow? _selected;
    int _z = 10;
    Point? _panFrom;
    Point _panOrigin;
    bool _docked;
    bool _rightPan;
    bool _rmbArmed;
    Point _rmbStart;
    bool _ctrlReset;
    bool _suppressMenu;
    EventHandler? _animation;
    ResizeEdges _anchors;

    public CanvasView()
    {
        InitializeComponent();
        World.RenderTransform = new TransformGroup { Children = { _scale, _pan } };
        _dots = MakeDotBrush();
        Surface.Background = _dots;

        _listFrame.MinW = 260;
        _listFrame.MinH = 160;
        _listFrame.BoundsCommitted += OnListBoundsCommitted;
        _listFrame.Snap = SnapList;
        _listFrame.FrameCornerRadius = 8;
        Panel.SetZIndex(_listFrame, 0);
        World.Children.Add(_listFrame);

        _saveTimer = new DispatcherTimer { Interval = TimeSpan.FromMilliseconds(500) };
        _saveTimer.Tick += (_, _) => { _saveTimer.Stop(); Save(); };

        Surface.SizeChanged += (_, _) =>
        {
            Surface.Clip = new RectangleGeometry(new Rect(Surface.RenderSize), 7, 7);
            if (!HasCanvas) Dock();
            else FollowAnchors();
        };
        Surface.MouseLeftButtonDown += OnSurfaceDown;
        Surface.PreviewMouseDown += OnSurfacePreviewDown;
        Surface.MouseMove += OnSurfaceMove;
        Surface.MouseUp += OnSurfaceUp;
        Surface.LostMouseCapture += (_, _) => _panFrom = null;
        Surface.MouseWheel += OnSurfaceWheel;
        // Right button drag pans the canvas; Ctrl + right click returns to the start view.
        Surface.PreviewMouseRightButtonDown += OnRightDown;
        Surface.PreviewMouseMove += OnRightDragDetect;
        Surface.PreviewMouseRightButtonUp += OnRightUp;
        Surface.AddHandler(ContextMenuOpeningEvent, new ContextMenuEventHandler((_, e) =>
        {
            if (_suppressMenu) e.Handled = true;
        }), true);
        Surface.DragOver += OnDragOver;
        Surface.Drop += OnDrop;
        Surface.KeyDown += OnKeyDown;
        Surface.ContextMenu = new ContextMenu();
        Surface.ContextMenuOpening += OnSurfaceMenu;

        CornerZone.MouseEnter += (_, _) => FadeZoomPanel(1);
        CornerZone.MouseLeave += (_, _) => FadeZoomPanel(0);
        ZoomInBtn.Click += (_, _) => ZoomAt(Center, _scale.ScaleX * 1.2);
        ZoomOutBtn.Click += (_, _) => ZoomAt(Center, _scale.ScaleX / 1.2);
        ZoomResetBtn.Click += (_, _) => ResetView();
        ArrangeBtn.Click += (_, _) => Arrange();

        EnterDocked();
    }

    public IExplorerHost? Host { get; set; }
    public bool HasCanvas => _layout != null && !_docked;
    public event EventHandler? PinsChanged;
    public IEnumerable<string> PinnedNames =>
        _docked && _layout != null ? _layout.Items.Select(i => i.Name) : _pins.Select(p => p.Item.Name);

    public void AttachList(FileListView list)
    {
        _list = list;
        _listFrame.Child = list;
    }

    Point Center => new(Surface.ActualWidth / 2, Surface.ActualHeight / 2);

    // ---------- folder lifecycle ----------

    public void LoadFolder(string folder, IReadOnlyList<FileItem> items)
    {
        SaveNow();
        StopAnimation();
        ClearPins();
        _folder = folder;
        _layout = LayoutStore.Load(folder);
        _docked = _layout?.Docked == true;
        if (!HasCanvas)
        {
            EnterDocked();
        }
        else
        {
            EnterCanvas();
            var l = _layout!.List;
            _listFrame.Bounds = l.W > 0 && l.H > 0 ? new Rect(l.X, l.Y, l.W, l.H) : DefaultListBounds();
            RestorePins(items);
            // Every visit starts from the canvas origin, not from where it was left.
            _scale.ScaleX = _scale.ScaleY = 1;
            _pan.X = _pan.Y = 0;
            UpdateView();
            Dispatcher.InvokeAsync(() => { _anchors = TouchingEdges(); FollowAnchors(); }, DispatcherPriority.Loaded);
        }
        RaisePinsChanged();
    }

    void RestorePins(IReadOnlyList<FileItem> items)
    {
        if (_layout == null) return;
        foreach (var p in _layout.Items)
        {
            var item = items.FirstOrDefault(i => string.Equals(i.Name, p.Name, StringComparison.OrdinalIgnoreCase));
            if (item == null || item.IsFolder) continue;
            var pw = AddPin(item, new Rect(p.X, p.Y, p.W, p.H), fit: false);
            if (Math.Abs(p.Zoom - 1) > 0.001) pw.Body.SetZoom(p.Zoom);
            pw.BeginAnimation(OpacityProperty, new DoubleAnimation(0, 1, TimeSpan.FromMilliseconds(Anim.Short)));
        }
    }

    /// <summary>The folder was re-read: drop pins whose files disappeared.</summary>
    public void SyncItems(IReadOnlyList<FileItem> items)
    {
        var names = new HashSet<string>(items.Select(i => i.Name), StringComparer.OrdinalIgnoreCase);
        foreach (var pw in _pins.Where(p => !names.Contains(p.Item.Name)).ToList())
            RemovePin(pw, save: false);
        RaisePinsChanged();
    }

    public void RenameFile(string oldName, FileItem renamed)
    {
        if (_docked && _layout != null)
        {
            foreach (var p in _layout.Items.Where(p => string.Equals(p.Name, oldName, StringComparison.OrdinalIgnoreCase)))
                p.Name = renamed.Name;
            Save();
            return;
        }
        foreach (var pw in _pins.Where(p => string.Equals(p.Item.Name, oldName, StringComparison.OrdinalIgnoreCase)))
            pw.Rename(renamed);
        ScheduleSave();
    }

    public void CreateCanvas(Rect? listBounds = null)
    {
        if (_folder == null || HasCanvas) return;
        StopAnimation();
        _scale.ScaleX = _scale.ScaleY = 1;
        _pan.X = _pan.Y = 0;
        if (_layout != null && _docked)
        {
            // Bring back the canvas that was folded away, with its pinned windows.
            _docked = false;
            _layout.Docked = false;
            EnterCanvas();
            _listFrame.Bounds = listBounds ?? DefaultListBounds();
            if (_list != null) RestorePins(_list.Items);
            UpdateView();
            Save();
            RaisePinsChanged();
            return;
        }
        _layout = new FolderLayout();
        EnterCanvas();
        _listFrame.Bounds = listBounds ?? DefaultListBounds();
        UpdateView();
        _anchors = TouchingEdges();
        Save();
    }

    public void RemoveCanvas()
    {
        if (_folder == null || _layout == null) return;
        StopAnimation();
        ClearPins();
        _layout = null;
        _docked = false;
        _saveTimer.Stop();
        LayoutStore.Delete(_folder);
        EnterDocked();
        RaisePinsChanged();
    }

    /// <summary>The list was stretched over the whole view: hide the canvas but keep its pins.</summary>
    void CollapseCanvas()
    {
        if (_folder == null || _layout == null) return;
        if (_pins.Count == 0)
        {
            RemoveCanvas();
            return;
        }
        Save();
        ClearPins();
        _docked = true;
        _layout.Docked = true;
        LayoutStore.Save(_folder, _layout);
        EnterDocked();
        RaisePinsChanged();
    }

    /// <summary>List edges that sit exactly on the view edges (they follow the app window when it resizes).</summary>
    ResizeEdges TouchingEdges()
    {
        var z = _scale.ScaleX;
        var b = _listFrame.Bounds;
        double left = b.X * z + _pan.X, top = b.Y * z + _pan.Y;
        double right = left + b.Width * z, bottom = top + b.Height * z;
        var e = ResizeEdges.None;
        if (Math.Abs(left) < 1.5) e |= ResizeEdges.Left;
        if (Math.Abs(top) < 1.5) e |= ResizeEdges.Top;
        if (Math.Abs(right - Surface.ActualWidth) < 1.5) e |= ResizeEdges.Right;
        if (Math.Abs(bottom - Surface.ActualHeight) < 1.5) e |= ResizeEdges.Bottom;
        return e;
    }

    void FollowAnchors()
    {
        if (_anchors == ResizeEdges.None || _listFrame.IsInteracting) return;
        var z = _scale.ScaleX;
        var v = new Rect(-_pan.X / z, -_pan.Y / z, Surface.ActualWidth / z, Surface.ActualHeight / z);
        var b = _listFrame.Bounds;
        double l = b.Left, t = b.Top, r = b.Right, bt = b.Bottom;
        if (_anchors.HasFlag(ResizeEdges.Left)) l = v.Left;
        if (_anchors.HasFlag(ResizeEdges.Top)) t = v.Top;
        if (_anchors.HasFlag(ResizeEdges.Right)) r = v.Right;
        if (_anchors.HasFlag(ResizeEdges.Bottom)) bt = v.Bottom;
        // Edges that are not anchored keep their place unless the list would get too small.
        if (_anchors.HasFlag(ResizeEdges.Right) && !_anchors.HasFlag(ResizeEdges.Left)) l = Math.Min(l, r - _listFrame.MinW);
        if (_anchors.HasFlag(ResizeEdges.Bottom) && !_anchors.HasFlag(ResizeEdges.Top)) t = Math.Min(t, bt - _listFrame.MinH);
        _listFrame.Bounds = new Rect(l, t, Math.Max(0, r - l), Math.Max(0, bt - t));
        ScheduleSave();
    }

    bool CoversView()
    {
        var z = _scale.ScaleX;
        var b = _listFrame.Bounds;
        var left = b.X * z + _pan.X;
        var top = b.Y * z + _pan.Y;
        const double tolerance = 16;
        return left <= tolerance && top <= tolerance &&
               left + b.Width * z >= Surface.ActualWidth - tolerance &&
               top + b.Height * z >= Surface.ActualHeight - tolerance;
    }

    /// <summary>Magnet: list edges stick to the visible area's edges.</summary>
    Rect SnapList(Rect r, ResizeEdges edges)
    {
        if (!HasCanvas) return r;
        var z = _scale.ScaleX;
        var v = new Rect(-_pan.X / z, -_pan.Y / z, Surface.ActualWidth / z, Surface.ActualHeight / z);
        var t = 16 / z;
        if (edges == ResizeEdges.None)
        {
            if (Math.Abs(r.Left - v.Left) < t) r.X = v.Left;
            else if (Math.Abs(r.Right - v.Right) < t) r.X = v.Right - r.Width;
            if (Math.Abs(r.Top - v.Top) < t) r.Y = v.Top;
            else if (Math.Abs(r.Bottom - v.Bottom) < t) r.Y = v.Bottom - r.Height;
            return r;
        }
        double l = r.Left, tp = r.Top, rt = r.Right, bt = r.Bottom;
        if (edges.HasFlag(ResizeEdges.Left) && Math.Abs(l - v.Left) < t) l = v.Left;
        if (edges.HasFlag(ResizeEdges.Right) && Math.Abs(rt - v.Right) < t) rt = v.Right;
        if (edges.HasFlag(ResizeEdges.Top) && Math.Abs(tp - v.Top) < t) tp = v.Top;
        if (edges.HasFlag(ResizeEdges.Bottom) && Math.Abs(bt - v.Bottom) < t) bt = v.Bottom;
        return new Rect(l, tp, Math.Max(0, rt - l), Math.Max(0, bt - tp));
    }

    Rect DefaultListBounds() =>
        new(0, 0, Math.Clamp(Surface.ActualWidth * 0.4, 320, 480), Math.Max(240, Surface.ActualHeight));

    void EnterDocked()
    {
        _scale.ScaleX = _scale.ScaleY = 1;
        _pan.X = _pan.Y = 0;
        _listFrame.Edges = ResizeEdges.Horizontal;
        _listFrame.IsMovable = false;
        _listFrame.HasShadow = false;
        CornerZone.Visibility = Visibility.Collapsed;
        Dock();
        UpdateView();
    }

    void EnterCanvas()
    {
        _listFrame.Edges = ResizeEdges.All;
        _listFrame.IsMovable = true;
        _listFrame.HasShadow = true;
        CornerZone.Visibility = Visibility.Visible;
    }

    void Dock()
    {
        if (Surface.ActualWidth <= 0) return;
        _listFrame.Bounds = new Rect(0, 0, Surface.ActualWidth, Surface.ActualHeight);
    }

    void OnListBoundsCommitted(object? sender, EventArgs e)
    {
        if (!HasCanvas)
        {
            // Pulling a side edge of the docked list inward creates the canvas.
            if (_listFrame.ActualWidth < Surface.ActualWidth - 40)
            {
                var b = _listFrame.Bounds;
                CreateCanvas(new Rect(b.X, 0, b.Width, Surface.ActualHeight));
            }
            else Dock();
            return;
        }
        if (CoversView())
        {
            CollapseCanvas();
            return;
        }
        _anchors = TouchingEdges();
        ScheduleSave();
    }

    // ---------- pins ----------

    public void Pin(IEnumerable<FileItem> items, Point? worldPos = null)
    {
        if (_folder == null) return;
        if (!HasCanvas) CreateCanvas();
        var offset = 0.0;
        PreviewWindow? last = null;
        foreach (var item in items.Where(i => !i.IsFolder))
        {
            var existing = _pins.FirstOrDefault(p => string.Equals(p.Item.Name, item.Name, StringComparison.OrdinalIgnoreCase));
            if (existing != null) { last = existing; continue; }
            var body = PreviewFactory.Create(item);
            var size = body.PreferredSize;
            var pos = worldPos is { } wp ? new Point(wp.X + offset, wp.Y + offset) : NextSpot();
            last = AddPin(item, new Rect(pos, size), fit: true, body);
            offset += 28;
        }
        // The view stays where it is: pinning never moves the canvas.
        if (last != null) Select(last);
        RaisePinsChanged();
        ScheduleSave();
    }

    PreviewWindow AddPin(FileItem item, Rect bounds, bool fit, PreviewContent? body = null)
    {
        var pw = new PreviewWindow(item, body ?? PreviewFactory.Create(item), fit);
        pw.Bounds = bounds;
        Panel.SetZIndex(pw, ++_z);
        pw.Activated += (_, _) => Select(pw);
        pw.BoundsCommitted += (_, _) => ScheduleSave();
        pw.BoundsChangedByContent += (_, _) => ScheduleSave();
        pw.CloseRequested += (_, _) => RemovePin(pw, save: true);
        pw.RevealRequested += (_, _) => _list?.Reveal(pw.Item.Name);
        pw.Body.StateChanged += (_, _) => ScheduleSave();
        World.Children.Add(pw);
        _pins.Add(pw);
        return pw;
    }

    void RemovePin(PreviewWindow pw, bool save)
    {
        pw.Body.Unload();
        World.Children.Remove(pw);
        _pins.Remove(pw);
        if (_selected == pw) _selected = null;
        if (save)
        {
            RaisePinsChanged();
            ScheduleSave();
        }
    }

    void ClearPins()
    {
        foreach (var pw in _pins.ToList()) RemovePin(pw, save: false);
        _selected = null;
    }

    Point NextSpot()
    {
        var l = _listFrame.Bounds;
        var n = _pins.Count % 8;
        return new Point(l.Right + 28 + n * 28, l.Top + 20 + n * 28);
    }

    void Select(PreviewWindow? pw)
    {
        if (_selected != pw)
        {
            if (_selected != null) _selected.IsSelected = false;
            _selected = pw;
            if (pw != null) pw.IsSelected = true;
        }
        if (pw == null) return;
        Panel.SetZIndex(pw, ++_z);
        // Keyboard goes to the window (Delete unpins it), never stays in the file list.
        if (!pw.IsKeyboardFocusWithin) pw.Focus();
    }

    void Arrange()
    {
        var l = _listFrame.Bounds;
        double x0 = l.Right + 28, x = x0, y = l.Top, rowH = 0;
        var maxRight = x0 + Math.Max(700, Surface.ActualWidth / _scale.ScaleX - x0 - 40);
        foreach (var pw in _pins)
        {
            var b = pw.Bounds;
            if (x > x0 && x + b.Width > maxRight)
            {
                x = x0;
                y += rowH + 24;
                rowH = 0;
            }
            pw.Bounds = new Rect(x, y, b.Width, b.Height);
            x += b.Width + 24;
            rowH = Math.Max(rowH, b.Height);
        }
        ScheduleSave();
    }

    void RaisePinsChanged() => PinsChanged?.Invoke(this, EventArgs.Empty);

    // ---------- pan & zoom ----------

    void OnSurfaceDown(object sender, MouseButtonEventArgs e)
    {
        if (e.OriginalSource != Surface) return;
        Select(null);
        Surface.Focus();
        if (HasCanvas) BeginPan(e);
    }

    void OnSurfacePreviewDown(object sender, MouseButtonEventArgs e)
    {
        if (e.ChangedButton == MouseButton.Middle && HasCanvas) BeginPan(e);
    }

    void BeginPan(MouseButtonEventArgs e)
    {
        StopAnimation();
        _anchors = ResizeEdges.None;
        _panFrom = e.GetPosition(Surface);
        _panOrigin = new Point(_pan.X, _pan.Y);
        Surface.CaptureMouse();
        Surface.Cursor = Cursors.SizeAll;
        e.Handled = true;
    }

    void OnRightDown(object sender, MouseButtonEventArgs e)
    {
        if (!HasCanvas) return;
        if (Keyboard.Modifiers.HasFlag(ModifierKeys.Control))
        {
            _ctrlReset = true;
            _suppressMenu = true;
            e.Handled = true;
            return;
        }
        // The click is not taken yet: a plain right click must still open context menus.
        _rmbArmed = true;
        _rmbStart = e.GetPosition(Surface);
    }

    void OnRightDragDetect(object sender, MouseEventArgs e)
    {
        if (!_rmbArmed || _rightPan) return;
        if (e.RightButton != MouseButtonState.Pressed)
        {
            _rmbArmed = false;
            return;
        }
        var p = e.GetPosition(Surface);
        if (Math.Abs(p.X - _rmbStart.X) < 4 && Math.Abs(p.Y - _rmbStart.Y) < 4) return;
        StopAnimation();
        _rightPan = true;
        _anchors = ResizeEdges.None;
        _suppressMenu = true;
        _panFrom = _rmbStart;
        _panOrigin = new Point(_pan.X, _pan.Y);
        Surface.CaptureMouse();
        Surface.Cursor = Cursors.SizeAll;
        e.Handled = true;
    }

    void OnRightUp(object sender, MouseButtonEventArgs e)
    {
        _rmbArmed = false;
        if (_ctrlReset)
        {
            _ctrlReset = false;
            ResetView();
            e.Handled = true;
            ReleaseMenuLater();
            return;
        }
        if (!_rightPan) return;
        _rightPan = false;
        _panFrom = null;
        Surface.ReleaseMouseCapture();
        Surface.Cursor = null;
        ScheduleSave();
        e.Handled = true;
        ReleaseMenuLater();
    }

    // The context-menu request arrives right after the button-up; swallow it first.
    void ReleaseMenuLater() => Dispatcher.InvokeAsync(() => _suppressMenu = false, DispatcherPriority.Input);

    void OnSurfaceMove(object sender, MouseEventArgs e)
    {
        if (_panFrom is not { } from) return;
        var p = e.GetPosition(Surface);
        _pan.X = _panOrigin.X + p.X - from.X;
        _pan.Y = _panOrigin.Y + p.Y - from.Y;
        UpdateView();
    }

    void OnSurfaceUp(object sender, MouseButtonEventArgs e)
    {
        if (_panFrom == null) return;
        _panFrom = null;
        Surface.ReleaseMouseCapture();
        Surface.Cursor = null;
        ScheduleSave();
    }

    void OnSurfaceWheel(object sender, MouseWheelEventArgs e)
    {
        if (!HasCanvas) return;
        StopAnimation();
        _anchors = ResizeEdges.None;
        var mods = Keyboard.Modifiers;
        if (mods.HasFlag(ModifierKeys.Control))
            ZoomAt(e.GetPosition(Surface), _scale.ScaleX * (e.Delta > 0 ? 1.1 : 1 / 1.1));
        else if (mods.HasFlag(ModifierKeys.Shift))
            _pan.X += e.Delta * 0.6;
        else
            _pan.Y += e.Delta * 0.6;
        UpdateView();
        ScheduleSave();
        e.Handled = true;
    }

    void ZoomAt(Point p, double target)
    {
        if (!HasCanvas) return;
        StopAnimation();
        _anchors = ResizeEdges.None;
        var z = _scale.ScaleX;
        var nz = Math.Clamp(target, MinZoom, MaxZoom);
        if (Math.Abs(nz - 1) < 0.04) nz = 1;
        _pan.X = p.X - (p.X - _pan.X) * nz / z;
        _pan.Y = p.Y - (p.Y - _pan.Y) * nz / z;
        _scale.ScaleX = _scale.ScaleY = nz;
        UpdateView();
        ScheduleSave();
    }

    /// <summary>Glides back to the canvas origin at 100%.</summary>
    void ResetView()
    {
        if (!HasCanvas) return;
        AnimateTo(0, 0, 1);
    }

    void AnimateTo(double x, double y, double zoom)
    {
        StopAnimation();
        double fx = _pan.X, fy = _pan.Y, fz = _scale.ScaleX;
        var started = DateTime.UtcNow;
        const double duration = 260;
        _animation = (_, _) =>
        {
            var t = Math.Min(1, (DateTime.UtcNow - started).TotalMilliseconds / duration);
            var k = 1 - Math.Pow(1 - t, 3); // ease-out
            _pan.X = fx + (x - fx) * k;
            _pan.Y = fy + (y - fy) * k;
            _scale.ScaleX = _scale.ScaleY = fz + (zoom - fz) * k;
            UpdateView();
            if (t >= 1)
            {
                StopAnimation();
                _anchors = TouchingEdges();
                ScheduleSave();
            }
        };
        CompositionTarget.Rendering += _animation;
    }

    void StopAnimation()
    {
        if (_animation == null) return;
        CompositionTarget.Rendering -= _animation;
        _animation = null;
    }

    void UpdateView()
    {
        var z = _scale.ScaleX;
        var step = DotStep * z;
        while (step < 12) step *= 2;
        _dots.Viewport = new Rect(Mod(_pan.X, step), Mod(_pan.Y, step), step, step);
        _dots.Opacity = HasCanvas ? 1 : 0;
        ZoomText.Text = Math.Round(z * 100) + "%";
    }

    static double Mod(double a, double m) => ((a % m) + m) % m;

    static DrawingBrush MakeDotBrush()
    {
        var group = new DrawingGroup();
        group.Children.Add(new GeometryDrawing(Brushes.Transparent, null, new RectangleGeometry(new Rect(0, 0, DotStep, DotStep))));
        var dot = new SolidColorBrush(Color.FromRgb(0x24, 0x24, 0x24));
        dot.Freeze();
        group.Children.Add(new GeometryDrawing(dot, null, new EllipseGeometry(new Point(1, 1), 1, 1)));
        return new DrawingBrush(group)
        {
            TileMode = TileMode.Tile,
            Viewbox = new Rect(0, 0, DotStep, DotStep),
            ViewboxUnits = BrushMappingMode.Absolute,
            Viewport = new Rect(0, 0, DotStep, DotStep),
            ViewportUnits = BrushMappingMode.Absolute,
        };
    }

    void FadeZoomPanel(double to) =>
        ZoomPanel.BeginAnimation(OpacityProperty, new DoubleAnimation(to, TimeSpan.FromMilliseconds(to > 0 ? 120 : 260)));

    // ---------- drag & drop: pin files from the list ----------

    void OnDragOver(object sender, DragEventArgs e)
    {
        e.Effects = DragDropEffects.None;
        if (_folder != null && e.Data.GetData(DataFormats.FileDrop) is string[] files &&
            files.Any(f => Shell.SameFolder(Path.GetDirectoryName(f), _folder) && File.Exists(f)))
            e.Effects = DragDropEffects.Link;
        e.Handled = true;
    }

    void OnDrop(object sender, DragEventArgs e)
    {
        if (_folder == null || e.Data.GetData(DataFormats.FileDrop) is not string[] files || _list == null) return;
        var names = new HashSet<string>(files.Where(f => Shell.SameFolder(Path.GetDirectoryName(f), _folder)).Select(Path.GetFileName)!, StringComparer.OrdinalIgnoreCase);
        var items = _list.Items.Where(i => names.Contains(i.Name)).ToList();
        if (items.Count == 0) return;
        var wasDocked = !HasCanvas;
        if (wasDocked) CreateCanvas();
        var pos = e.GetPosition(World);
        if (wasDocked || _listFrame.Bounds.Contains(pos)) Pin(items);
        else Pin(items, new Point(pos.X - 20, pos.Y - 14));
        e.Handled = true;
    }

    // ---------- keyboard & menu ----------

    void OnKeyDown(object sender, KeyEventArgs e)
    {
        if (e.OriginalSource is System.Windows.Controls.Primitives.TextBoxBase) return;
        if (e.Key == Key.Delete && _selected != null)
        {
            RemovePin(_selected, save: true);
            e.Handled = true;
        }
        else if (e.Key == Key.D0 && Keyboard.Modifiers == ModifierKeys.Control)
        {
            ResetView();
            e.Handled = true;
        }
    }

    void OnSurfaceMenu(object sender, ContextMenuEventArgs e)
    {
        if (e.OriginalSource != Surface || !HasCanvas)
        {
            e.Handled = e.OriginalSource == Surface;
            return;
        }
        Surface.ContextMenu = Menus.Build(
            Menus.Item("Упорядочить окна", Icons.Grid, Arrange),
            Menus.Item("Сбросить вид", Icons.Fit, ResetView, "Ctrl+0"),
            Menus.Sep(),
            Menus.Item("Убрать холст", Icons.Canvas, () => Host?.RemoveCanvas(), danger: true));
    }

    // ---------- persistence ----------

    void ScheduleSave()
    {
        if (!HasCanvas) return;
        _saveTimer.Stop();
        _saveTimer.Start();
    }

    public void SaveNow()
    {
        if (_saveTimer.IsEnabled)
        {
            _saveTimer.Stop();
            Save();
        }
    }

    void Save()
    {
        if (_folder == null || _layout == null) return;
        if (_docked)
        {
            LayoutStore.Save(_folder, _layout);
            return;
        }
        var l = _listFrame.Bounds;
        _layout.View = new ViewState { X = _pan.X, Y = _pan.Y, Zoom = _scale.ScaleX };
        _layout.List = new RectState { X = l.X, Y = l.Y, W = l.Width, H = l.Height };
        _layout.Items = _pins.Select(p =>
        {
            var b = p.Bounds;
            return new PinState { Name = p.Item.Name, X = b.X, Y = b.Y, W = b.Width, H = b.Height, Zoom = p.Body.Zoom };
        }).ToList();
        LayoutStore.Save(_folder, _layout);
    }

    public void FlushPins()
    {
        foreach (var pw in _pins) pw.Body.Unload();
    }
}

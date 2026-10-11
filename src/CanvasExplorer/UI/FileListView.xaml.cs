using System.Collections.ObjectModel;
using System.ComponentModel;
using System.IO;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Data;
using System.Windows.Input;
using System.Windows.Media;
using CanvasExplorer.Models;
using CanvasExplorer.Services;

namespace CanvasExplorer.UI;

public partial class FileListView : UserControl
{
    ObservableCollection<FileItem> _items = new();
    ICollectionView _view;
    string _filter = "";
    int _density = 1;
    bool _grid;
    Point? _dragStart;
    bool _suppressModeEvents;
    FileItem? _anchor;
    FileItem? _pendingSingle;
    bool _marquee;
    bool _marqueeMoved;
    Point _marqueeStart;
    FileItem? _marqueeClickItem;
    ModifierKeys _marqueeMods;
    HashSet<FileItem> _marqueeBase = new();
    readonly HashSet<FileItem> _marqueeHit = new();

    public FileListView()
    {
        InitializeComponent();
        _view = MakeView(_items);
        List.ItemsSource = _view;

        DensityL.Checked += (_, _) => SetDensity(0);
        DensityM.Checked += (_, _) => SetDensity(1);
        DensityS.Checked += (_, _) => SetDensity(2);
        ViewList.Checked += (_, _) => SetGrid(false);
        ViewGrid.Checked += (_, _) => SetGrid(true);

        List.KeyDown += OnKeyDown;
        List.PreviewMouseLeftButtonDown += OnPreviewDown;
        List.PreviewMouseDown += (_, e) =>
        {
            // Middle click on a folder opens it in a new tab.
            if (e.ChangedButton != MouseButton.Middle || ItemAt(e.OriginalSource) is not { IsFolder: true } folder) return;
            Host?.OpenInNewTab(folder.FullPath);
            e.Handled = true;
        };
        List.PreviewMouseMove += OnPreviewMouseMove;
        List.PreviewMouseLeftButtonUp += OnPreviewUp;
        List.LostMouseCapture += (_, _) => EndMarquee();
        List.DragOver += OnDragOver;
        List.Drop += OnDrop;
        List.ContextMenu = new ContextMenu();
        List.ContextMenuOpening += OnListMenu;
        List.SelectionChanged += (_, _) => UpdateCount();
        Header.ContextMenu = new ContextMenu();
        Header.ContextMenuOpening += (_, _) => Header.ContextMenu = BuildFolderMenu();

        _density = Math.Clamp(AppSettings.Current.Density, 0, 2);
        ApplyMode();
    }

    public IExplorerHost? Host { get; set; }
    public IReadOnlyList<FileItem> Items => _items;
    public int PinnedCount { get; set; }

    ICollectionView MakeView(ObservableCollection<FileItem> items)
    {
        var view = CollectionViewSource.GetDefaultView(items);
        view.Filter = o => _filter.Length == 0 || ((FileItem)o).Name.Contains(_filter, StringComparison.OrdinalIgnoreCase);
        return view;
    }

    public void SetItems(IReadOnlyList<FileItem> items, ICollection<string>? keepSelected = null)
    {
        _items = new ObservableCollection<FileItem>(items);
        _view = MakeView(_items);
        List.ItemsSource = _view;
        // A different folder always starts at the top.
        if (keepSelected == null) Scroller?.ScrollToHome();
        ShowMessage(items.Count == 0 ? "Папка пуста" : null);
        if (keepSelected is { Count: > 0 })
        {
            foreach (var it in _items)
                if (keepSelected.Contains(it.Name)) List.SelectedItems.Add(it);
        }
        // The Shift+click anchor survives a refresh (the items are new objects with the same names).
        _anchor = keepSelected != null && _anchor != null
            ? _items.FirstOrDefault(i => string.Equals(i.Name, _anchor.Name, StringComparison.OrdinalIgnoreCase))
            : null;
        _pendingSingle = null;
        UpdateCount();
        LoadThumbnails();
    }

    CancellationTokenSource? _thumbs;

    /// <summary>In the tile view, images and videos get their pictures (in the background, cached).</summary>
    void LoadThumbnails()
    {
        _thumbs?.Cancel();
        _thumbs = null;
        if (!_grid) return;
        var wanted = _items.Where(i => i.HasThumbnail && i.Thumb == null).ToList();
        if (wanted.Count == 0) return;
        var cts = _thumbs = new CancellationTokenSource();
        var px = (int)Math.Ceiling(96 * Math.Max(1, VisualTreeHelper.GetDpi(this).DpiScaleX));
        foreach (var item in wanted)
        {
            _ = ShellThumbnails.GetAsync(item.FullPath, item.Modified, px, cts.Token).ContinueWith(t =>
            {
                if (t.Result is { } bmp && !cts.IsCancellationRequested)
                    Dispatcher.BeginInvoke(() => item.Thumb = bmp, System.Windows.Threading.DispatcherPriority.Background);
            }, TaskScheduler.Default);
        }
    }

    public void SetFilter(string text)
    {
        _filter = text.Trim();
        _view.Refresh();
        UpdateCount();
        if (_items.Count > 0)
            ShowMessage(_filter.Length > 0 && _view.IsEmpty ? "Ничего не найдено" : null);
    }

    /// <summary>Short fade/slide-in used when another folder is shown.</summary>
    public void PlayEnter()
    {
        var move = new TranslateTransform(0, 6);
        List.RenderTransform = move;
        var duration = TimeSpan.FromMilliseconds(Anim.Short);
        var ease = new System.Windows.Media.Animation.CubicEase { EasingMode = System.Windows.Media.Animation.EasingMode.EaseOut };
        List.BeginAnimation(OpacityProperty, new System.Windows.Media.Animation.DoubleAnimation(0, 1, duration) { EasingFunction = ease });
        move.BeginAnimation(TranslateTransform.YProperty, new System.Windows.Media.Animation.DoubleAnimation(6, 0, duration) { EasingFunction = ease });
    }

    public void ShowMessage(string? text)
    {
        Message.Text = text ?? "";
        Message.Visibility = text == null ? Visibility.Collapsed : Visibility.Visible;
    }

    public void UpdateCount()
    {
        var total = _items.Count;
        var text = Format.Plural(total, "объект", "объекта", "объектов");
        // While searching, say how many of them match.
        // "из" takes the genitive: из 1 объекта, из 2 объектов, из 21 объекта.
        if (_filter.Length > 0)
            text = "найдено " + _view.Cast<object>().Count() + " из " + Format.Plural(total, "объекта", "объектов", "объектов");
        if (PinnedCount > 0) text += " · " + PinnedCount + " на холсте";
        var sel = List.SelectedItems.Count;
        if (sel > 1) text += " · выбрано " + sel;
        CountText.Text = text;
    }

    public IReadOnlyList<FileItem> Selected => List.SelectedItems.Cast<FileItem>().ToList();

    public void Reveal(string name)
    {
        var item = _items.FirstOrDefault(i => string.Equals(i.Name, name, StringComparison.OrdinalIgnoreCase));
        if (item == null) return;
        if (_filter.Length > 0 && !item.Name.Contains(_filter, StringComparison.OrdinalIgnoreCase)) return;
        List.SelectedItems.Clear();
        List.SelectedItem = item;
        List.ScrollIntoView(item);
        FocusItem(item);
    }

    public void FocusList()
    {
        if (List.SelectedItem is FileItem it) FocusItem(it);
        else List.Focus();
    }

    void FocusItem(FileItem item)
    {
        Dispatcher.BeginInvoke(() =>
        {
            if (List.ItemContainerGenerator.ContainerFromItem(item) is ListBoxItem c) c.Focus();
        }, System.Windows.Threading.DispatcherPriority.Loaded);
    }

    // ---------- display modes ----------

    void SetDensity(int d)
    {
        if (_suppressModeEvents) return;
        _density = d;
        AppSettings.Current.Density = d;
        AppSettings.ScheduleSave();
        // Picking a row size means "list" for this folder.
        if (_grid)
        {
            _grid = false;
            Host?.SetFolderGrid(false);
        }
        ApplyMode();
    }

    void SetGrid(bool grid)
    {
        if (_suppressModeEvents) return;
        if (_grid == grid) return;
        _grid = grid;
        Host?.SetFolderGrid(grid);
        ApplyMode();
        LoadThumbnails();
    }

    /// <summary>Shows the folder as tiles or rows (each folder remembers its own choice).</summary>
    public void ShowAsGrid(bool grid)
    {
        if (_grid == grid) return;
        _grid = grid;
        ApplyMode();
    }

    public bool IsGrid => _grid;

    void ApplyMode()
    {
        _suppressModeEvents = true;
        ViewList.IsChecked = !_grid;
        ViewGrid.IsChecked = _grid;
        DensityL.IsChecked = !_grid && _density == 0;
        DensityM.IsChecked = !_grid && _density == 1;
        DensityS.IsChecked = !_grid && _density == 2;
        _suppressModeEvents = false;

        List.ItemTemplate = (DataTemplate)Resources[_grid ? "Tile" : _density switch { 0 => "RowL", 2 => "RowS", _ => "RowM" }];
        List.ItemsPanel = (ItemsPanelTemplate)Resources[_grid ? "WrapTpl" : "StackTpl"];
        List.ItemContainerStyle = (Style)(_grid ? Resources["TileItem"] : FindResource("FileRow"));
        ScrollViewer.SetCanContentScroll(List, !_grid);
        HeaderColumns.Visibility = _grid ? Visibility.Hidden : Visibility.Visible;
        DensityGroup.Opacity = _grid ? 0.5 : 1;
    }

    // ---------- input ----------

    FileItem? ItemAt(object source)
    {
        var container = ItemsControl.ContainerFromElement(List, source as DependencyObject) as ListBoxItem;
        return container?.DataContext as FileItem;
    }

    // ---------- mouse selection ----------
    // Pressing on a file's icon or name selects it and can drag it out; pressing anywhere else
    // starts a selection frame.

    static bool IsGrab(object source)
    {
        for (var d = source as DependencyObject; d != null && d is not ListBoxItem; d = ParentOf(d))
            if (d is FrameworkElement { Tag: "grab" }) return true;
        return false;
    }

    static bool InScrollBar(object source)
    {
        for (var d = source as DependencyObject; d != null; d = ParentOf(d))
            if (d is System.Windows.Controls.Primitives.ScrollBar) return true;
        return false;
    }

    static DependencyObject? ParentOf(DependencyObject d) =>
        d is Visual or System.Windows.Media.Media3D.Visual3D ? VisualTreeHelper.GetParent(d) : LogicalTreeHelper.GetParent(d);

    ScrollViewer? Scroller => FindChild<ScrollViewer>(List, _ => true);

    Panel? ItemsHost => FindChild<Panel>(List, p => p.IsItemsHost);

    static T? FindChild<T>(DependencyObject root, Func<T, bool> match) where T : DependencyObject
    {
        for (var i = 0; i < VisualTreeHelper.GetChildrenCount(root); i++)
        {
            var c = VisualTreeHelper.GetChild(root, i);
            if (c is T t && match(t)) return t;
            if (FindChild(c, match) is { } found) return found;
        }
        return null;
    }

    Vector ScrollOffset => Scroller is { } sv ? new Vector(sv.HorizontalOffset, sv.VerticalOffset) : default;

    void OnPreviewDown(object sender, MouseButtonEventArgs e)
    {
        if (InScrollBar(e.OriginalSource)) return;
        var item = ItemAt(e.OriginalSource);
        if (e.ClickCount == 2)
        {
            if (item != null) Host?.OpenItem(item);
            e.Handled = true;
            return;
        }
        var mods = Keyboard.Modifiers;
        var p = e.GetPosition(List);
        if (item != null && IsGrab(e.OriginalSource))
        {
            ClickSelect(item, mods, deferIfSelected: true);
            _dragStart = p;
            FocusItem(item);
            e.Handled = true;
            return;
        }

        _marquee = true;
        _marqueeMoved = false;
        _marqueeClickItem = item;
        _marqueeMods = mods;
        _marqueeStart = p + ScrollOffset;
        _marqueeBase = (mods & (ModifierKeys.Control | ModifierKeys.Shift)) != 0 ? new HashSet<FileItem>(Selected) : new HashSet<FileItem>();
        _marqueeHit.Clear();
        List.Focus();
        List.CaptureMouse();
        e.Handled = true;
    }

    void ClickSelect(FileItem item, ModifierKeys mods, bool deferIfSelected)
    {
        if (mods.HasFlag(ModifierKeys.Control))
        {
            if (List.SelectedItems.Contains(item)) List.SelectedItems.Remove(item);
            else List.SelectedItems.Add(item);
            _anchor = item;
        }
        else if (mods.HasFlag(ModifierKeys.Shift))
        {
            var order = _view.Cast<FileItem>().ToList();
            var a = _anchor != null ? order.IndexOf(_anchor) : -1;
            var b = order.IndexOf(item);
            if (a < 0) a = b;
            SyncSelection(new HashSet<FileItem>(order.GetRange(Math.Min(a, b), Math.Abs(b - a) + 1)));
        }
        else if (deferIfSelected && List.SelectedItems.Contains(item))
        {
            // Keep the group selected in case this becomes a drag; collapse to one item on release.
            _pendingSingle = item;
        }
        else
        {
            SelectOnly(item);
        }
    }

    void SelectOnly(FileItem item)
    {
        List.SelectedItems.Clear();
        List.SelectedItem = item;
        _anchor = item;
    }

    void SyncSelection(HashSet<FileItem> want)
    {
        foreach (var s in List.SelectedItems.Cast<FileItem>().ToList())
            if (!want.Contains(s)) List.SelectedItems.Remove(s);
        foreach (var w in want)
            if (!List.SelectedItems.Contains(w)) List.SelectedItems.Add(w);
    }

    void OnPreviewUp(object sender, MouseButtonEventArgs e)
    {
        _dragStart = null;
        if (_marquee)
        {
            var moved = _marqueeMoved;
            EndMarquee();
            if (!moved)
            {
                if (_marqueeClickItem is { } it)
                {
                    ClickSelect(it, _marqueeMods, deferIfSelected: false);
                    FocusItem(it);
                }
                else if ((_marqueeMods & (ModifierKeys.Control | ModifierKeys.Shift)) == 0)
                {
                    List.SelectedItems.Clear();
                }
            }
            e.Handled = true;
            return;
        }
        if (_pendingSingle is { } single)
        {
            _pendingSingle = null;
            SelectOnly(single);
        }
    }

    void EndMarquee()
    {
        if (!_marquee) return;
        _marquee = false;
        Marquee.Visibility = Visibility.Collapsed;
        if (List.IsMouseCaptured) List.ReleaseMouseCapture();
    }

    void UpdateMarquee(Point listPoint)
    {
        var sv = Scroller;
        if (sv != null)
        {
            // Scroll when dragging past the top or bottom edge.
            if (listPoint.Y < 0) sv.ScrollToVerticalOffset(sv.VerticalOffset - 16);
            else if (listPoint.Y > List.ActualHeight) sv.ScrollToVerticalOffset(sv.VerticalOffset + 16);
        }
        var off = ScrollOffset;
        var current = listPoint + off;
        if (!_marqueeMoved && (current - _marqueeStart).Length < 4) return;
        _marqueeMoved = true;

        var rect = new Rect(_marqueeStart, current);
        Canvas.SetLeft(Marquee, rect.X - off.X);
        Canvas.SetTop(Marquee, rect.Y - off.Y);
        Marquee.Width = rect.Width;
        Marquee.Height = rect.Height;
        Marquee.Visibility = Visibility.Visible;

        if (ItemsHost is { } panel)
        {
            foreach (var child in panel.Children.OfType<ListBoxItem>())
            {
                if (child.DataContext is not FileItem fi || !child.IsVisible) continue;
                var b = child.TransformToAncestor(List).TransformBounds(new Rect(child.RenderSize));
                b.Offset(off);
                if (rect.IntersectsWith(b)) _marqueeHit.Add(fi);
                else _marqueeHit.Remove(fi);
            }
        }
        var want = new HashSet<FileItem>(_marqueeBase);
        foreach (var h in _marqueeHit)
        {
            if (_marqueeMods.HasFlag(ModifierKeys.Control) && _marqueeBase.Contains(h)) want.Remove(h);
            else want.Add(h);
        }
        SyncSelection(want);
    }

    void OnKeyDown(object sender, KeyEventArgs e)
    {
        var mods = Keyboard.Modifiers;
        var key = e.Key == Key.System ? e.SystemKey : e.Key;
        var sel = Selected;
        switch (key)
        {
            case Key.Enter when mods == ModifierKeys.Alt && sel.Count > 0:
                Shell.ShowProperties(sel[0].FullPath);
                break;
            case Key.Enter when sel.Count > 0:
                Host?.OpenItem(sel[0]);
                break;
            case Key.Back:
                Host?.GoUp();
                break;
            case Key.Delete when sel.Count > 0:
                DeleteSelected();
                break;
            case Key.F2 when sel.Count == 1:
                Host?.RenameItem(sel[0]);
                break;
            case Key.C when mods == ModifierKeys.Control && sel.Count > 0:
                Shell.SetClipboard(sel.Select(i => i.FullPath), cut: false);
                break;
            case Key.X when mods == ModifierKeys.Control && sel.Count > 0:
                Shell.SetClipboard(sel.Select(i => i.FullPath), cut: true);
                break;
            case Key.V when mods == ModifierKeys.Control:
                Paste();
                break;
            case Key.Escape when sel.Count > 0:
                List.SelectedItems.Clear();
                break;
            case Key.N when mods == (ModifierKeys.Control | ModifierKeys.Shift):
                Host?.CreateFolder();
                break;
            default:
                return;
        }
        e.Handled = true;
    }

    void OnPreviewMouseMove(object sender, MouseEventArgs e)
    {
        if (_marquee)
        {
            if (e.LeftButton == MouseButtonState.Pressed) UpdateMarquee(e.GetPosition(List));
            else EndMarquee();
            e.Handled = true;
            return;
        }
        if (_dragStart is not { } start || e.LeftButton != MouseButtonState.Pressed) return;
        var p = e.GetPosition(List);
        if (Math.Abs(p.X - start.X) < SystemParameters.MinimumHorizontalDragDistance &&
            Math.Abs(p.Y - start.Y) < SystemParameters.MinimumVerticalDragDistance) return;
        _dragStart = null;
        _pendingSingle = null;
        var sel = Selected;
        if (sel.Count == 0) return;
        var data = new DataObject(DataFormats.FileDrop, sel.Select(i => i.FullPath).ToArray());
        DragDrop.DoDragDrop(List, data, DragDropEffects.Copy | DragDropEffects.Move | DragDropEffects.Link);
    }

    /// <summary>Folder a drop lands in: the folder row under the cursor, else the current folder.</summary>
    string? DropTarget(DragEventArgs e, string[] files)
    {
        if (ItemAt(e.OriginalSource) is { IsFolder: true } folder && !files.Any(f => Shell.SameFolder(f, folder.FullPath)))
            return folder.FullPath;
        return Host?.CurrentFolder;
    }

    void OnDragOver(object sender, DragEventArgs e)
    {
        e.Effects = DragDropEffects.None;
        if (e.Data.GetData(DataFormats.FileDrop) is string[] files && files.Length > 0 && DropTarget(e, files) is { } dest)
            e.Effects = Shell.DropEffect(files, dest, e.KeyStates, e.AllowedEffects);
        e.Handled = true;
    }

    void OnDrop(object sender, DragEventArgs e)
    {
        if (e.Data.GetData(DataFormats.FileDrop) is not string[] files || Host == null || DropTarget(e, files) is not { } dest) return;
        e.Effects = Shell.Drop(e, dest, Host.Hwnd);
        e.Handled = true;
    }

    // ---------- image preview on icon hover ----------

    void Chip_ToolTipOpening(object sender, ToolTipEventArgs e)
    {
        if (sender is not FrameworkElement fe || fe.DataContext is not FileItem item || item.Kind != FileKind.Image)
        {
            e.Handled = true;
            return;
        }
        var thumb = Thumbnails.Get(item.FullPath, item.Modified);
        if (thumb == null)
        {
            e.Handled = true;
            return;
        }
        var img = new Image { Source = thumb.Image, MaxWidth = 260, MaxHeight = 220, Stretch = Stretch.Uniform };
        RenderOptions.SetBitmapScalingMode(img, BitmapScalingMode.HighQuality);
        var caption = new TextBlock { Text = thumb.Width + " × " + thumb.Height + " · " + item.SizeText, FontSize = 11, Margin = new Thickness(0, 6, 0, 0) };
        caption.SetResourceReference(TextBlock.ForegroundProperty, "MutedBrush");
        var panel = new StackPanel();
        panel.Children.Add(img);
        panel.Children.Add(caption);
        fe.ToolTip = panel;
    }

    // ---------- menus ----------

    void OnListMenu(object sender, ContextMenuEventArgs e)
    {
        var item = ItemAt(e.OriginalSource);
        if (item != null && !List.SelectedItems.Contains(item)) SelectOnly(item);
        List.ContextMenu = item == null ? BuildFolderMenu() : BuildItemMenu(Selected);
    }

    ContextMenu BuildItemMenu(IReadOnlyList<FileItem> sel)
    {
        var first = sel[0];
        var files = sel.Where(i => !i.IsFolder).ToList();
        var canPin = files.Count > 0 && Host?.IsVirtualView != true;
        return Menus.Build(
            Menus.Item("Открыть", Icons.Open, () => Host?.OpenItem(first), "Enter"),
            first.IsFolder ? Menus.Item("Открыть в новой вкладке", Icons.Plus, () => Host?.OpenInNewTab(first.FullPath))
                           : Menus.Item("Открыть с помощью…", Icons.OpenWith, () => Shell.OpenWith(first.FullPath)),
            canPin ? Menus.Item(files.All(f => f.IsPinned) ? "Показать на холсте" : "Закрепить на холсте", Icons.Pin, () => Host?.PinItems(files)) : null,
            sel.Count == 1 && first.IsFolder && Host?.IsFavorite(first.FullPath) == false
                ? Menus.Item("Добавить в избранное", Icons.Star, () => Host?.AddFavorite(first.FullPath)) : null,
            TagsMenu(sel),
            Host?.IsVirtualView == true && sel.Count == 1 ? Menus.Item("Показать в папке", Icons.Folder, () => Host?.ShowInFolder(first.FullPath)) : null,
            Menus.Sep(),
            Menus.Item("Вырезать", Icons.Cut, () => Shell.SetClipboard(sel.Select(i => i.FullPath), true), "Ctrl+X"),
            Menus.Item("Копировать", Icons.Copy, () => Shell.SetClipboard(sel.Select(i => i.FullPath), false), "Ctrl+C"),
            Menus.Item("Вставить", Icons.Paste, Paste, "Ctrl+V", enabled: Shell.CanPaste()),
            Menus.Item("Переименовать", Icons.Rename, () => Host?.RenameItem(first), "F2", enabled: sel.Count == 1),
            Menus.Item("Удалить", Icons.Trash, DeleteSelected, "Del", danger: true),
            Menus.Sep(),
            Menus.Item("Копировать путь", Icons.Path, () => Shell.SetClipboardText(string.Join(Environment.NewLine, sel.Select(i => i.FullPath)))),
            Menus.Item("Свойства", Icons.Info, () => Shell.ShowProperties(first.FullPath), "Alt+Enter"));
    }

    /// <summary>"Теги ›": every tag with a check mark (on all selected = checked), then "Новый тег…".</summary>
    MenuItem TagsMenu(IReadOnlyList<FileItem> sel)
    {
        var paths = sel.Select(i => i.FullPath).ToList();
        var store = TagStore.Current;
        var items = new List<object?>();
        foreach (var tag in store.Tags)
        {
            var all = paths.All(p => store.Has(p, tag.Id));
            items.Add(Menus.Check(tag.Name, TagMark.Visual(tag, 9, 13), all, () => store.Set(paths, tag.Id, !all)));
        }
        if (items.Count > 0) items.Add(Menus.Sep());
        items.Add(Menus.Item("Новый тег…", Icons.Plus, () => Host?.NewTag(paths)));
        return Menus.Sub("Теги", Icons.Tag, items.ToArray());
    }

    /// <summary>Re-reads tag marks of the shown items (tags were created, changed or assigned).</summary>
    public void RefreshTags()
    {
        foreach (var it in _items) it.Tags = TagMark.For(it.FullPath);
    }

    ContextMenu BuildFolderMenu()
    {
        var hasCanvas = Host?.HasCanvas == true;
        if (Host?.IsVirtualView == true)
            return Menus.Build(Menus.Item("Обновить", Icons.Refresh, () => Host?.RefreshFolder(), "F5"));
        return Menus.Build(
            Menus.Item("Вставить", Icons.Paste, Paste, "Ctrl+V", enabled: Shell.CanPaste()),
            Menus.Sub("Создать", Icons.NewFile,
                Menus.Item("Папку", Icons.FolderPlus, () => Host?.CreateFolder(), "Ctrl+Shift+N"),
                Menus.Sep(),
                Menus.Item("Текстовый документ", Icons.TextFile, () => Host?.CreateFile("Новый текстовый документ", ".txt")),
                Menus.Item("Документ Markdown", Icons.TextFile, () => Host?.CreateFile("Новая заметка", ".md"))),
            Menus.Item("Обновить", Icons.Refresh, () => Host?.RefreshFolder(), "F5"),
            Menus.Sep(),
            hasCanvas
                ? Menus.Item("Убрать холст", Icons.Canvas, () => Host?.RemoveCanvas(), danger: true)
                : Menus.Item("Создать холст", Icons.Canvas, () => Host?.CreateCanvas()),
            Menus.Sep(),
            Menus.Item("Свойства папки", Icons.Info, () => { if (Host?.CurrentFolder is { } f) Shell.ShowProperties(f); }));
    }

    void Paste()
    {
        if (Host?.CurrentFolder is { } folder) Shell.Paste(folder, Host.Hwnd);
    }

    void DeleteSelected()
    {
        var sel = Selected;
        if (sel.Count == 0 || Host == null) return;
        Shell.Delete(sel.Select(i => i.FullPath), Host.Hwnd);
    }
}

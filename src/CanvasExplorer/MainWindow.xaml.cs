using System.IO;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Interop;
using System.Windows.Shell;
using System.Windows.Media;
using System.Windows.Threading;
using CanvasExplorer.Models;
using CanvasExplorer.Services;
using CanvasExplorer.UI;
using UiIcon = CanvasExplorer.UI.Icon;

namespace CanvasExplorer;

/// <summary>One open folder tab with its own navigation history.</summary>
sealed class FolderTab
{
    public string Path = "";
    public readonly Stack<string> Back = new();
    public readonly Stack<string> Forward = new();
}

public partial class MainWindow : Window, IExplorerHost
{
    static readonly Guid DownloadsId = new("374DE290-123F-4565-9164-39C4925E467B");

    readonly FileListView _list = new();
    readonly List<FolderTab> _tabs = new();
    FolderTab _tab = new();
    readonly List<RadioButton> _sideItems = new();
    readonly DispatcherTimer _refreshTimer;
    FileSystemWatcher? _watcher;
    string? _current;
    int _loadVersion;
    IntPtr _hwnd;

    public MainWindow()
    {
        InitializeComponent();

        _list.Host = this;
        CanvasArea.Host = this;
        CanvasArea.AttachList(_list);
        CanvasArea.PinsChanged += (_, _) => SyncPinnedFlags();

        _refreshTimer = new DispatcherTimer { Interval = TimeSpan.FromMilliseconds(250) };
        _refreshTimer.Tick += (_, _) => { _refreshTimer.Stop(); _ = LoadItemsAsync(keepCanvas: true); };

        BackBtn.Click += (_, _) => GoBack();
        ForwardBtn.Click += (_, _) => GoForward();
        UpBtn.Click += (_, _) => GoUp();
        RefreshBtn.Click += (_, _) => RefreshFolder();
        MinBtn.Click += (_, _) => WindowState = WindowState.Minimized;
        MaxBtn.Click += (_, _) => WindowState = WindowState == WindowState.Maximized ? WindowState.Normal : WindowState.Maximized;
        CloseBtn.Click += (_, _) => Close();

        SearchBox.TextChanged += (_, _) =>
        {
            SearchHint.Visibility = SearchBox.Text.Length == 0 ? Visibility.Visible : Visibility.Collapsed;
            _list.SetFilter(SearchBox.Text);
        };
        SearchBox.KeyDown += (_, e) =>
        {
            if (e.Key == Key.Escape) { SearchBox.Text = ""; _list.FocusList(); }
            if (e.Key == Key.Down) _list.FocusList();
        };

        PathBar.SizeChanged += (_, e) => { if (Math.Abs(e.NewSize.Width - e.PreviousSize.Width) > 1) UpdateCrumbs(); };
        PathBar.MouseLeftButtonDown += (_, e) => { if (!PathEdit.IsVisible) BeginPathEdit(); e.Handled = true; };
        PathEdit.KeyDown += OnPathKey;
        PathEdit.LostKeyboardFocus += (_, _) => EndPathEdit();

        SideColumn.Width = new GridLength(Math.Clamp(AppSettings.Current.SidebarWidth, 150, 420));
        SideGrip.Glow = SideGlow;
        SideGrip.DragDelta += (_, e) =>
            SideColumn.Width = new GridLength(Math.Clamp(SideColumn.ActualWidth + e.HorizontalChange, 150, 420));

        StateChanged += (_, _) => OnStateChanged();
        SourceInitialized += (_, _) => OnSourceInitialized();
        Loaded += (_, _) => OnLoaded();
        Closing += (_, _) => OnClosing();
        PreviewKeyDown += OnWindowKey;
        MouseDown += (_, e) =>
        {
            if (e.ChangedButton == MouseButton.XButton1) GoBack();
            if (e.ChangedButton == MouseButton.XButton2) GoForward();
        };

        RestoreWindowBounds();
        BuildSidebar();
    }

    public string? CurrentFolder => _current;
    public IntPtr Hwnd => _hwnd;
    public bool HasCanvas => CanvasArea.HasCanvas;

    // ---------- window chrome ----------

    void RestoreWindowBounds()
    {
        var s = AppSettings.Current;
        if (!double.IsNaN(s.WinLeft) && !double.IsNaN(s.WinTop))
        {
            var r = new Rect(s.WinLeft, s.WinTop, s.WinWidth, s.WinHeight);
            var screen = new Rect(SystemParameters.VirtualScreenLeft, SystemParameters.VirtualScreenTop,
                SystemParameters.VirtualScreenWidth, SystemParameters.VirtualScreenHeight);
            if (screen.IntersectsWith(r))
            {
                WindowStartupLocation = WindowStartupLocation.Manual;
                Left = r.Left;
                Top = r.Top;
            }
        }
        Width = Math.Max(MinWidth, s.WinWidth);
        Height = Math.Max(MinHeight, s.WinHeight);
        if (s.Maximized) WindowState = WindowState.Maximized;
    }

    void OnSourceInitialized()
    {
        _hwnd = new WindowInteropHelper(this).Handle;
        int on = 1, round = 2, border = 0x002A2A2A;
        Native.DwmSetWindowAttribute(_hwnd, Native.DWMWA_USE_IMMERSIVE_DARK_MODE, ref on, 4);
        Native.DwmSetWindowAttribute(_hwnd, Native.DWMWA_WINDOW_CORNER_PREFERENCE, ref round, 4);
        Native.DwmSetWindowAttribute(_hwnd, Native.DWMWA_BORDER_COLOR, ref border, 4);
        OnStateChanged();
    }

    void OnStateChanged()
    {
        var max = WindowState == WindowState.Maximized;
        // A maximized chrome-less window overhangs the screen by the resize frame.
        RootBorder.Margin = max ? new Thickness(7) : new Thickness(0);
        MaxIcon.Data = max ? Icons.Restore : Icons.Maximize;
        MaxBtn.ToolTip = max ? "Восстановить" : "Развернуть";
    }

    void OnLoaded()
    {
        var s = AppSettings.Current;
        foreach (var path in s.Tabs ?? new List<string>())
            if (Directory.Exists(path)) _tabs.Add(new FolderTab { Path = path });
        var active = Math.Clamp(s.ActiveTab, 0, Math.Max(0, _tabs.Count - 1));

        var args = Environment.GetCommandLineArgs();
        if (args.Length > 1 && Directory.Exists(args[1]))
        {
            _tabs.Add(new FolderTab { Path = Path.GetFullPath(args[1]) });
            active = _tabs.Count - 1;
        }
        if (_tabs.Count == 0)
        {
            var start = s.LastFolder is { } last && Directory.Exists(last) ? last
                : Environment.GetFolderPath(Environment.SpecialFolder.MyDocuments);
            _tabs.Add(new FolderTab { Path = start });
            active = 0;
        }
        _tab = _tabs[active];
        Show(_tab.Path);
    }

    void OnClosing()
    {
        CanvasArea.SaveNow();
        CanvasArea.FlushPins();
        var s = AppSettings.Current;
        s.SidebarWidth = SideColumn.ActualWidth;
        s.Maximized = WindowState == WindowState.Maximized;
        var r = WindowState == WindowState.Normal ? new Rect(Left, Top, Width, Height) : RestoreBounds;
        s.WinLeft = r.Left;
        s.WinTop = r.Top;
        s.WinWidth = r.Width;
        s.WinHeight = r.Height;
        s.LastFolder = _current;
        s.Tabs = _tabs.Select(t => t.Path).ToList();
        s.ActiveTab = _tabs.IndexOf(_tab);
        s.Save();
        _watcher?.Dispose();
    }

    // ---------- sidebar ----------

    void BuildSidebar()
    {
        AddSection("Избранное");
        AddSideItem("Рабочий стол", Icons.Desktop, Environment.GetFolderPath(Environment.SpecialFolder.Desktop));
        AddSideItem("Загрузки", Icons.Download, KnownFolder(DownloadsId));
        AddSideItem("Документы", Icons.Doc, Environment.GetFolderPath(Environment.SpecialFolder.MyDocuments));
        AddSideItem("Изображения", Icons.Image, Environment.GetFolderPath(Environment.SpecialFolder.MyPictures));
        AddSideItem("Музыка", Icons.Music, Environment.GetFolderPath(Environment.SpecialFolder.MyMusic));
        AddSideItem("Видео", Icons.Video, Environment.GetFolderPath(Environment.SpecialFolder.MyVideos));
        AddSideItem("Пользователь", Icons.Home, Environment.GetFolderPath(Environment.SpecialFolder.UserProfile));

        AddSection("Расположения");
        foreach (var d in DriveInfo.GetDrives())
        {
            if (!d.IsReady) continue;
            AddSideItem(DriveLabel(d), Icons.Drive, d.RootDirectory.FullName);
        }
    }

    static string? KnownFolder(Guid id) =>
        Native.SHGetKnownFolderPath(id, 0, IntPtr.Zero, out var path) == 0 ? path : null;

    static string DriveLabel(DriveInfo d)
    {
        var letter = d.Name.TrimEnd('\\');
        string label;
        try { label = d.VolumeLabel; } catch { label = ""; }
        if (string.IsNullOrWhiteSpace(label))
            label = d.DriveType switch
            {
                DriveType.Removable => "Съёмный диск",
                DriveType.Network => "Сетевой диск",
                DriveType.CDRom => "Дисковод",
                _ => "Локальный диск",
            };
        return $"{label} ({letter})";
    }

    void AddSection(string title)
    {
        var tb = new TextBlock
        {
            Text = title,
            FontSize = 11,
            FontWeight = FontWeights.SemiBold,
            Margin = new Thickness(8, SidePanel.Children.Count == 0 ? 0 : 14, 0, 4),
        };
        tb.SetResourceReference(TextBlock.ForegroundProperty, "MutedBrush");
        SidePanel.Children.Add(tb);
    }

    void AddSideItem(string label, Geometry icon, string? path)
    {
        if (string.IsNullOrEmpty(path) || !Directory.Exists(path)) return;
        var content = new DockPanel();
        var ic = new UiIcon(icon, 15) { Margin = new Thickness(0, 0, 8, 0), VerticalAlignment = VerticalAlignment.Center };
        ic.SetResourceReference(UiIcon.ForegroundProperty, "SubBrush");
        DockPanel.SetDock(ic, Dock.Left);
        content.Children.Add(ic);
        content.Children.Add(new TextBlock { Text = label, VerticalAlignment = VerticalAlignment.Center, TextTrimming = TextTrimming.CharacterEllipsis });

        var rb = new RadioButton
        {
            Content = content,
            Tag = path,
            GroupName = "Side",
            Style = (Style)FindResource("SideItem"),
            ToolTip = path,
        };
        ToolTipService.SetInitialShowDelay(rb, 1200);
        rb.Click += (_, _) => NavigateTo(path);
        rb.PreviewMouseDown += (_, e) =>
        {
            if (e.ChangedButton != MouseButton.Middle) return;
            OpenInNewTab(path);
            e.Handled = true;
        };
        _sideItems.Add(rb);
        SidePanel.Children.Add(rb);
    }

    void HighlightSidebar()
    {
        foreach (var rb in _sideItems)
        {
            var on = Shell.SameFolder(rb.Tag as string, _current);
            rb.IsChecked = on;
            if (rb.Content is DockPanel dp && dp.Children.Count > 0 && dp.Children[0] is UiIcon ic)
                ic.SetResourceReference(UiIcon.ForegroundProperty, on ? "AccentBrush" : "SubBrush");
        }
    }

    // ---------- navigation ----------

    public void NavigateTo(string path) => NavigateTo(path, addHistory: true);

    void NavigateTo(string path, bool addHistory)
    {
        string full;
        try { full = Path.GetFullPath(path); }
        catch { return; }
        if (!Directory.Exists(full))
        {
            if (File.Exists(full)) { Shell.Open(full); return; }
            System.Media.SystemSounds.Beep.Play();
            return;
        }
        if (_current != null && Shell.SameFolder(_current, full)) return;
        if (addHistory && _current != null)
        {
            _tab.Back.Push(_current);
            _tab.Forward.Clear();
        }
        Show(full);
    }

    void Show(string folder)
    {
        CanvasArea.SaveNow();
        _current = folder;
        _tab.Path = folder;
        Title = FolderName(folder);
        RenderTabs();
        SearchBox.Text = "";
        UpdateCrumbs();
        UpdateNavButtons();
        HighlightSidebar();
        Watch(folder);
        _ = LoadItemsAsync(keepCanvas: false);
    }

    void GoBack()
    {
        if (_tab.Back.Count == 0 || _current == null) return;
        _tab.Forward.Push(_current);
        Show(_tab.Back.Pop());
    }

    void GoForward()
    {
        if (_tab.Forward.Count == 0 || _current == null) return;
        _tab.Back.Push(_current);
        Show(_tab.Forward.Pop());
    }

    public void GoUp()
    {
        if (_current == null) return;
        var parent = Directory.GetParent(_current);
        if (parent == null) return;
        var child = Path.GetFileName(_current.TrimEnd('\\'));
        NavigateTo(parent.FullName);
        _pendingReveal = child;
    }

    string? _pendingReveal;

    void UpdateNavButtons()
    {
        BackBtn.IsEnabled = _tab.Back.Count > 0;
        ForwardBtn.IsEnabled = _tab.Forward.Count > 0;
        UpBtn.IsEnabled = _current != null && Directory.GetParent(_current) != null;
    }

    async Task LoadItemsAsync(bool keepCanvas)
    {
        var folder = _current;
        if (folder == null) return;
        var version = ++_loadVersion;
        var keep = keepCanvas ? new HashSet<string>(_list.Selected.Select(i => i.Name), StringComparer.OrdinalIgnoreCase) : null;
        List<FileItem> items;
        string? error = null;
        try
        {
            items = await Task.Run(() => Enumerate(folder));
        }
        catch (UnauthorizedAccessException)
        {
            items = new List<FileItem>();
            error = "Нет доступа к этой папке";
        }
        catch (Exception ex)
        {
            items = new List<FileItem>();
            error = "Не удалось прочитать папку\n" + ex.Message;
        }
        if (version != _loadVersion) return;

        _list.SetItems(items, keep);
        if (error != null) _list.ShowMessage(error);
        if (keepCanvas) CanvasArea.SyncItems(items);
        else CanvasArea.LoadFolder(folder, items);
        SyncPinnedFlags();

        if (_pendingReveal != null)
        {
            _list.Reveal(_pendingReveal);
            _pendingReveal = null;
        }
        else if (!keepCanvas) _list.FocusList();
    }

    static List<FileItem> Enumerate(string folder)
    {
        var options = new EnumerationOptions
        {
            IgnoreInaccessible = true,
            AttributesToSkip = FileAttributes.Hidden | FileAttributes.System,
            RecurseSubdirectories = false,
        };
        var dir = new DirectoryInfo(folder);
        // Enumerating also throws UnauthorizedAccessException for the folder itself, which the caller reports.
        var list = dir.EnumerateFileSystemInfos("*", options)
            .Where(i => i.Name != LayoutStore.FileName && !i.Name.EndsWith(".~tmp", StringComparison.OrdinalIgnoreCase))
            .Select(i => new FileItem(i))
            .ToList();
        list.Sort((a, b) => a.IsFolder != b.IsFolder ? (a.IsFolder ? -1 : 1) : Native.StrCmpLogicalW(a.Name, b.Name));
        return list;
    }

    void SyncPinnedFlags()
    {
        var pinned = new HashSet<string>(CanvasArea.PinnedNames, StringComparer.OrdinalIgnoreCase);
        foreach (var it in _list.Items) it.IsPinned = pinned.Contains(it.Name);
        _list.PinnedCount = pinned.Count;
        _list.UpdateCount();
    }

    // ---------- watcher ----------

    void Watch(string folder)
    {
        _watcher?.Dispose();
        _watcher = null;
        try
        {
            var w = new FileSystemWatcher(folder)
            {
                IncludeSubdirectories = false,
                NotifyFilter = NotifyFilters.FileName | NotifyFilters.DirectoryName | NotifyFilters.LastWrite | NotifyFilters.Size,
            };
            w.Created += OnFsEvent;
            w.Deleted += OnFsEvent;
            w.Changed += OnFsEvent;
            w.Renamed += (_, e) => Dispatcher.BeginInvoke(() => OnRenamed(e.OldName, e.Name, e.FullPath));
            w.EnableRaisingEvents = true;
            _watcher = w;
        }
        catch
        {
            // Some locations (e.g. certain network shares) can't be watched; F5 still works.
        }
    }

    void OnFsEvent(object sender, FileSystemEventArgs e)
    {
        if (e.Name == null || e.Name == LayoutStore.FileName || e.Name.EndsWith(".~tmp", StringComparison.OrdinalIgnoreCase)) return;
        Dispatcher.BeginInvoke(() => { _refreshTimer.Stop(); _refreshTimer.Start(); });
    }

    void OnRenamed(string? oldName, string? newName, string fullPath)
    {
        if (oldName != null && newName != null && File.Exists(fullPath))
            CanvasArea.RenameFile(oldName, new FileItem(new FileInfo(fullPath)));
        _refreshTimer.Stop();
        _refreshTimer.Start();
    }

    // ---------- breadcrumb & path editing ----------

    static string FolderName(string path)
    {
        var name = Path.GetFileName(path.TrimEnd('\\'));
        return string.IsNullOrEmpty(name) ? DriveLabel(new DriveInfo(path)) : name;
    }

    void UpdateCrumbs()
    {
        Crumbs.Children.Clear();
        if (_current == null) return;
        var parts = new List<string>();
        for (var d = new DirectoryInfo(_current); d != null; d = d.Parent) parts.Add(d.FullName);
        parts.Reverse();

        for (var i = 0; i < parts.Count; i++)
        {
            var p = parts[i];
            if (i > 0)
            {
                var chev = new UiIcon(Icons.Chevron, 11, 2.4) { VerticalAlignment = VerticalAlignment.Center, Margin = new Thickness(1, 0, 1, 0) };
                chev.SetResourceReference(UiIcon.ForegroundProperty, "FaintBrush");
                Crumbs.Children.Add(chev);
            }
            var last = i == parts.Count - 1;
            var btn = new Button
            {
                Content = FolderName(p),
                Style = (Style)FindResource("CrumbButton"),
                FontWeight = last ? FontWeights.SemiBold : FontWeights.Normal,
                ToolTip = p,
            };
            ToolTipService.SetInitialShowDelay(btn, 1200);
            if (last) btn.SetResourceReference(ForegroundProperty, "TextBrush");
            btn.Click += (_, _) => NavigateTo(p);
            Crumbs.Children.Add(btn);
        }
        FitCrumbs();
    }

    /// <summary>Drops leading segments (replaced by "…") until the path fits the bar.</summary>
    void FitCrumbs()
    {
        var available = PathBar.ActualWidth - 16;
        if (available <= 0) return;
        var trimmed = false;
        while (true)
        {
            Crumbs.Measure(new Size(double.PositiveInfinity, double.PositiveInfinity));
            if (Crumbs.DesiredSize.Width <= available) break;
            var start = trimmed ? 1 : 0;
            // Keep at least the last segment; remove a segment and its chevron.
            if (Crumbs.Children.Count - start <= 1) break;
            Crumbs.Children.RemoveAt(start);
            if (Crumbs.Children.Count > start && Crumbs.Children[start] is UiIcon) Crumbs.Children.RemoveAt(start);
            if (!trimmed)
            {
                AddCrumbText("…", 0);
                trimmed = true;
            }
        }
    }

    void AddCrumbText(string text, int index)
    {
        var tb = new TextBlock { Text = text, VerticalAlignment = VerticalAlignment.Center, Margin = new Thickness(4, 0, 2, 0) };
        tb.SetResourceReference(TextBlock.ForegroundProperty, "MutedBrush");
        Crumbs.Children.Insert(index, tb);
    }

    void BeginPathEdit()
    {
        PathEdit.Text = _current ?? "";
        Crumbs.Visibility = Visibility.Collapsed;
        PathEdit.Visibility = Visibility.Visible;
        PathEdit.Focus();
        PathEdit.SelectAll();
    }

    void EndPathEdit()
    {
        PathEdit.Visibility = Visibility.Collapsed;
        Crumbs.Visibility = Visibility.Visible;
    }

    void OnPathKey(object sender, KeyEventArgs e)
    {
        if (e.Key == Key.Enter)
        {
            var text = Environment.ExpandEnvironmentVariables(PathEdit.Text.Trim().Trim('"'));
            EndPathEdit();
            if (text.Length > 0) NavigateTo(text);
            _list.FocusList();
            e.Handled = true;
        }
        else if (e.Key == Key.Escape)
        {
            EndPathEdit();
            _list.FocusList();
            e.Handled = true;
        }
    }

    void OnWindowKey(object sender, KeyEventArgs e)
    {
        var key = e.Key == Key.System ? e.SystemKey : e.Key;
        var mods = Keyboard.Modifiers;
        if (mods == ModifierKeys.Alt && key == Key.Left) GoBack();
        else if (mods == ModifierKeys.Alt && key == Key.Right) GoForward();
        else if (mods == ModifierKeys.Alt && key == Key.Up) GoUp();
        else if (key == Key.F5) RefreshFolder();
        else if ((mods == ModifierKeys.Control && key == Key.L) || key == Key.F4) BeginPathEdit();
        else if (mods == ModifierKeys.Control && key == Key.F) { SearchBox.Focus(); SearchBox.SelectAll(); }
        else if (mods == ModifierKeys.Control && key == Key.T) { if (_current != null) OpenInNewTab(_current); }
        else if (mods == ModifierKeys.Control && key == Key.W) CloseTab(_tab);
        else if (mods.HasFlag(ModifierKeys.Control) && key == Key.Tab && _tabs.Count > 1)
        {
            var step = mods.HasFlag(ModifierKeys.Shift) ? -1 : 1;
            SwitchTo(_tabs[(_tabs.IndexOf(_tab) + step + _tabs.Count) % _tabs.Count]);
        }
        else return;
        e.Handled = true;
    }

    // ---------- tabs ----------

    public void OpenInNewTab(string path)
    {
        if (!Directory.Exists(path)) return;
        var tab = new FolderTab { Path = Path.GetFullPath(path) };
        _tabs.Insert(_tabs.IndexOf(_tab) + 1, tab);
        SwitchTo(tab);
    }

    void SwitchTo(FolderTab tab)
    {
        if (tab == _tab) return;
        CanvasArea.SaveNow();
        _tab = tab;
        Show(tab.Path);
    }

    void CloseTab(FolderTab tab)
    {
        if (_tabs.Count == 1)
        {
            Close();
            return;
        }
        var index = _tabs.IndexOf(tab);
        _tabs.Remove(tab);
        if (tab == _tab) SwitchTo(_tabs[Math.Min(index, _tabs.Count - 1)]);
        else RenderTabs();
    }

    void RenderTabs()
    {
        TabStrip.Children.Clear();
        foreach (var tab in _tabs)
            TabStrip.Children.Add(MakeTab(tab));
        var add = new Button
        {
            Style = (Style)FindResource("IconButton"),
            Width = 28,
            Height = 28,
            ToolTip = "Новая вкладка (Ctrl+T)",
            Content = new UiIcon(Icons.Plus, 13, 2),
            Margin = new Thickness(2, 0, 0, 0),
        };
        add.SetResourceReference(ForegroundProperty, "SubBrush");
        WindowChrome.SetIsHitTestVisibleInChrome(add, true);
        add.Click += (_, _) => { if (_current != null) OpenInNewTab(_current); };
        TabStrip.Children.Add(add);
    }

    FrameworkElement MakeTab(FolderTab tab)
    {
        var active = tab == _tab;
        var icon = new UiIcon(Icons.Folder, 13) { VerticalAlignment = VerticalAlignment.Center, Margin = new Thickness(0, 0, 7, 0) };
        icon.SetResourceReference(UiIcon.ForegroundProperty, active ? "AccentBrush" : "MutedBrush");
        var title = new TextBlock
        {
            Text = FolderName(tab.Path),
            VerticalAlignment = VerticalAlignment.Center,
            TextTrimming = TextTrimming.CharacterEllipsis,
            FontWeight = active ? FontWeights.SemiBold : FontWeights.Normal,
        };
        title.SetResourceReference(TextBlock.ForegroundProperty, active ? "TextBrush" : "SubBrush");
        var close = new Button
        {
            Style = (Style)FindResource("IconButton"),
            Width = 18,
            Height = 18,
            ToolTip = "Закрыть вкладку (Ctrl+W)",
            Content = new UiIcon(Icons.Close, 10, 2.4),
            Margin = new Thickness(6, 0, 0, 0),
            Visibility = active ? Visibility.Visible : Visibility.Hidden,
        };
        close.SetResourceReference(ForegroundProperty, "SubBrush");
        close.Click += (_, _) => CloseTab(tab);

        var dock = new DockPanel();
        DockPanel.SetDock(icon, Dock.Left);
        DockPanel.SetDock(close, Dock.Right);
        dock.Children.Add(icon);
        dock.Children.Add(close);
        dock.Children.Add(title);

        var border = new Border
        {
            Height = 30,
            MinWidth = 96,
            MaxWidth = 210,
            CornerRadius = new CornerRadius(6),
            Padding = new Thickness(10, 0, 5, 0),
            Margin = new Thickness(0, 0, 4, 0),
            BorderThickness = new Thickness(1),
            Child = dock,
            ToolTip = tab.Path,
        };
        ToolTipService.SetInitialShowDelay(border, 1000);
        border.SetResourceReference(Border.BackgroundProperty, active ? "PanelBrush" : "BgBrush");
        border.SetResourceReference(Border.BorderBrushProperty, active ? "LineBrush" : "BgBrush");
        WindowChrome.SetIsHitTestVisibleInChrome(border, true);
        border.MouseEnter += (_, _) =>
        {
            close.Visibility = Visibility.Visible;
            if (!active) border.SetResourceReference(Border.BackgroundProperty, "RowHoverBrush");
        };
        border.MouseLeave += (_, _) =>
        {
            if (!active)
            {
                close.Visibility = Visibility.Hidden;
                border.SetResourceReference(Border.BackgroundProperty, "BgBrush");
            }
        };
        border.MouseLeftButtonDown += (_, e) => { SwitchTo(tab); e.Handled = true; };
        border.MouseUp += (_, e) =>
        {
            if (e.ChangedButton == MouseButton.Middle) { CloseTab(tab); e.Handled = true; }
        };
        return border;
    }

    // ---------- IExplorerHost ----------

    public void OpenItem(FileItem item)
    {
        if (item.IsFolder) NavigateTo(item.FullPath);
        else Shell.Open(item.FullPath);
    }

    public void PinItems(IEnumerable<FileItem> items) => CanvasArea.Pin(items);

    public void CreateCanvas() => CanvasArea.CreateCanvas();

    public void RemoveCanvas()
    {
        if (CanvasArea.PinnedNames.Any() &&
            MessageBox.Show(this, "Убрать холст? Все закреплённые окна будут откреплены. Файлы не удаляются.", "Холст",
                MessageBoxButton.OKCancel, MessageBoxImage.Question) != MessageBoxResult.OK)
            return;
        CanvasArea.RemoveCanvas();
    }

    public void RefreshFolder() => _ = LoadItemsAsync(keepCanvas: true);

    public void RenameItem(FileItem item)
    {
        var dot = item.IsFolder ? -1 : item.Name.LastIndexOf('.');
        var name = InputDialog.Ask(this, "Переименовать", item.Name, dot > 0 ? dot : -1);
        if (string.IsNullOrEmpty(name) || name == item.Name) return;
        if (name.IndexOfAny(Path.GetInvalidFileNameChars()) >= 0)
        {
            MessageBox.Show(this, "Имя содержит недопустимые символы.", "Переименование", MessageBoxButton.OK, MessageBoxImage.Warning);
            return;
        }
        var dest = Path.Combine(Path.GetDirectoryName(item.FullPath)!, name);
        try
        {
            if (item.IsFolder) Directory.Move(item.FullPath, dest);
            else File.Move(item.FullPath, dest);
            _pendingReveal = name;
        }
        catch (Exception ex)
        {
            MessageBox.Show(this, ex.Message, "Переименование", MessageBoxButton.OK, MessageBoxImage.Warning);
        }
    }

    public void CreateFolder()
    {
        if (_current == null) return;
        var name = "Новая папка";
        for (var i = 2; Directory.Exists(Path.Combine(_current, name)) || File.Exists(Path.Combine(_current, name)); i++)
            name = $"Новая папка ({i})";
        try
        {
            Directory.CreateDirectory(Path.Combine(_current, name));
            _pendingReveal = name;
            RefreshFolder();
        }
        catch (Exception ex)
        {
            MessageBox.Show(this, ex.Message, "Новая папка", MessageBoxButton.OK, MessageBoxImage.Warning);
        }
    }
}

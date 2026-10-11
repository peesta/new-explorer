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

public partial class MainWindow : Window, IExplorerHost, ISidebarHost
{
    readonly FileListView _list = new();
    readonly Sidebar _sidebar;
    readonly DispatcherTimer _drivesTimer = new() { Interval = TimeSpan.FromMilliseconds(700) };
    readonly List<FolderTab> _tabs = new();
    FolderTab _tab = new();
    FolderTab? _justOpened;
    readonly DispatcherTimer _tabHoverTimer = new() { Interval = TimeSpan.FromMilliseconds(550) };
    FolderTab? _tabHoverTarget;
    readonly DispatcherTimer _refreshTimer;
    readonly DispatcherTimer _settingsTimer;
    readonly HashSet<string> _changedFiles = new(StringComparer.OrdinalIgnoreCase);
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
        _refreshTimer.Tick += (_, _) =>
        {
            _refreshTimer.Stop();
            _ = LoadItemsAsync(keepCanvas: true);
            foreach (var name in _changedFiles) CanvasArea.FileChanged(name);
            _changedFiles.Clear();
        };
        _settingsTimer = new DispatcherTimer { Interval = TimeSpan.FromSeconds(1) };
        _settingsTimer.Tick += (_, _) => { _settingsTimer.Stop(); SaveSettings(); };

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
        TabStrip.SizeChanged += (_, _) => FitTabs();
        _tabHoverTimer.Tick += (_, _) =>
        {
            _tabHoverTimer.Stop();
            if (_tabHoverTarget != null && _tabs.Contains(_tabHoverTarget)) SwitchTo(_tabHoverTarget);
        };
        SideGrip.DragDelta += (_, e) =>
            SideColumn.Width = new GridLength(Math.Clamp(SideColumn.ActualWidth + e.HorizontalChange, 150, 420));
        SideGrip.DragCompleted += (_, _) => ScheduleSettingsSave();
        LocationChanged += (_, _) => ScheduleSettingsSave();
        SizeChanged += (_, _) => ScheduleSettingsSave();
        // The folder may have been deleted or renamed elsewhere while the window was in the background.
        Activated += (_, _) => { if (_current != null && !IsTag(_current) && !Directory.Exists(_current)) RefreshFolder(); };

        StateChanged += (_, _) => { OnStateChanged(); ScheduleSettingsSave(); };
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
        _sidebar = new Sidebar(SidePanel, SideBorder, this);
        _sidebar.Build();
        SideStack.Children.Add(new MiniPlayer(ShowInFolder));
        _drivesTimer.Tick += (_, _) => { _drivesTimer.Stop(); _sidebar.Build(); };
        TagStore.Current.Changed += (_, _) => Dispatcher.BeginInvoke(OnTagsChanged);
    }

    /// <summary>The real folder shown; null while a tag view is open.</summary>
    public string? CurrentFolder => _current != null && !IsTag(_current) ? _current : null;
    public string? CurrentLocation => _current;
    public bool IsVirtualView => _current != null && IsTag(_current);
    public Window OwnerWindow => this;
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
        HwndSource.FromHwnd(_hwnd)?.AddHook(WndProc);
    }

    IntPtr WndProc(IntPtr hwnd, int msg, IntPtr wParam, IntPtr lParam, ref bool handled)
    {
        const int WM_DEVICECHANGE = 0x0219, DBT_DEVICEARRIVAL = 0x8000, DBT_DEVICEREMOVECOMPLETE = 0x8004;
        // A flash drive or network drive came or went: rebuild the drive list (debounced, events come in bursts).
        if (msg == WM_DEVICECHANGE && (wParam == DBT_DEVICEARRIVAL || wParam == DBT_DEVICEREMOVECOMPLETE))
        {
            _drivesTimer.Stop();
            _drivesTimer.Start();
        }
        return IntPtr.Zero;
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
            if (LocationExists(path)) _tabs.Add(new FolderTab { Path = path });
        var active = Math.Clamp(s.ActiveTab, 0, Math.Max(0, _tabs.Count - 1));

        var args = Environment.GetCommandLineArgs();
        if (args.Length > 1 && Directory.Exists(args[1]))
        {
            // A folder that is already open in a tab is brought to front, not opened twice.
            var path = Path.GetFullPath(args[1]);
            var open = _tabs.FindIndex(t => Shell.SameFolder(t.Path, path));
            if (open >= 0) active = open;
            else
            {
                _tabs.Add(new FolderTab { Path = path });
                active = _tabs.Count - 1;
            }
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
        AudioEngine.Current.Close();
        _settingsTimer.Stop();
        SaveSettings();
        _watcher?.Dispose();
    }

    /// <summary>Settings are written shortly after every change, so a crash does not lose tabs and sizes.</summary>
    void ScheduleSettingsSave()
    {
        if (!IsLoaded) return;
        _settingsTimer.Stop();
        _settingsTimer.Start();
    }

    void SaveSettings()
    {
        var s = AppSettings.Current;
        s.SidebarWidth = SideColumn.ActualWidth;
        s.Maximized = WindowState == WindowState.Maximized;
        var r = WindowState == WindowState.Normal ? new Rect(Left, Top, Width, Height) : RestoreBounds;
        s.WinLeft = r.Left;
        s.WinTop = r.Top;
        s.WinWidth = r.Width;
        s.WinHeight = r.Height;
        s.LastFolder = CurrentFolder ?? s.LastFolder;
        s.Tabs = _tabs.Select(t => t.Path).ToList();
        s.ActiveTab = _tabs.IndexOf(_tab);
        s.Save();
    }

    // ---------- locations: folders and tag views ----------

    static bool IsTag(string location) => location.StartsWith(Sidebar.TagPrefix, StringComparison.Ordinal);

    static TagDef? TagOf(string location) => IsTag(location) ? TagStore.Current.Find(location[Sidebar.TagPrefix.Length..]) : null;

    static bool LocationExists(string location) => IsTag(location) ? TagOf(location) != null : Directory.Exists(location);

    /// <summary>Tags were created, changed or (un)assigned: update marks, counts, an open tag view.</summary>
    void OnTagsChanged()
    {
        _list.RefreshTags();
        CanvasArea.RefreshTags();
        if (_current != null && IsTag(_current))
        {
            if (TagOf(_current) == null) return;
            Title = LocationTitle(_current);
            UpdateCrumbs();
            RefreshFolder();
        }
        RenderTabs();
    }

    public bool IsFavorite(string path) => Sidebar.IsFavorite(path);

    public void ShowInFolder(string path)
    {
        if (Path.GetDirectoryName(Path.TrimEndingDirectorySeparator(path)) is not { } folder) return;
        NavigateTo(folder);
        _pendingReveal = Path.GetFileName(Path.TrimEndingDirectorySeparator(path));
    }

    public void AddFavorite(string path) => _sidebar.AddFavorite(path);

    public void NewTag(IReadOnlyList<string> assignTo) => _sidebar.NewTag(null, assignTo);

    public void SetFolderGrid(bool grid)
    {
        if (_current != null) AppSettings.Current.SetGrid(_current, grid);
    }

    // ---------- navigation ----------

    public void NavigateTo(string path) => NavigateTo(path, addHistory: true);

    void NavigateTo(string path, bool addHistory)
    {
        if (IsTag(path))
        {
            if (TagOf(path) == null || path == _current) return;
            if (addHistory && _current != null)
            {
                _tab.Back.Push(_current);
                _tab.Forward.Clear();
            }
            Show(path);
            return;
        }
        string full;
        try { full = Path.GetFullPath(path); }
        catch { return; }
        if (!Directory.Exists(full))
        {
            if (File.Exists(full)) { Shell.Open(full); return; }
            System.Media.SystemSounds.Beep.Play();
            return;
        }
        if (_current != null && !IsTag(_current) && Shell.SameFolder(_current, full)) return;
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
        // Going to a folder above the current one (Up, Back, a path segment) selects the folder we came from.
        if (_current != null && ChildOnTheWay(folder, _current) is { } child) _pendingReveal = child;
        _current = folder;
        _tab.Path = folder;
        Title = LocationTitle(folder);
        RenderTabs();
        SearchBox.Text = "";
        UpdateCrumbs();
        UpdateNavButtons();
        _sidebar.Highlight();
        // Each folder remembers whether it is shown as a list or as tiles.
        _list.ShowAsGrid(AppSettings.Current.IsGrid(folder));
        if (IsTag(folder))
        {
            _watcher?.Dispose();
            _watcher = null;
        }
        else Watch(folder);
        _changedFiles.Clear();
        _ = LoadItemsAsync(keepCanvas: false);
        ScheduleSettingsSave();
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
        if (_current == null || IsTag(_current)) return;
        var parent = Directory.GetParent(_current);
        if (parent == null) return;
        NavigateTo(parent.FullName);
    }

    string? _pendingReveal;

    void UpdateNavButtons()
    {
        BackBtn.IsEnabled = _tab.Back.Count > 0;
        ForwardBtn.IsEnabled = _tab.Forward.Count > 0;
        UpBtn.IsEnabled = _current != null && !IsTag(_current) && Directory.GetParent(_current) != null;
    }

    async Task LoadItemsAsync(bool keepCanvas)
    {
        var folder = _current;
        if (folder == null) return;
        var version = ++_loadVersion;
        var keep = keepCanvas ? new HashSet<string>(_list.Selected.Select(i => i.Name), StringComparer.OrdinalIgnoreCase) : null;
        List<FileItem> items;
        string? error = null;
        var tag = TagOf(folder);
        try
        {
            items = tag != null ? await Task.Run(() => EnumerateTag(tag.Id)) : await Task.Run(() => Enumerate(folder));
        }
        catch (UnauthorizedAccessException)
        {
            items = new List<FileItem>();
            error = "Нет доступа к этой папке";
        }
        catch (DirectoryNotFoundException) when (tag == null && !Directory.Exists(folder))
        {
            // The open folder was deleted or renamed elsewhere: step up to the nearest folder that still exists.
            if (version == _loadVersion && folder == _current && NearestExisting(folder) is { } up) Show(up);
            return;
        }
        catch (Exception ex)
        {
            items = new List<FileItem>();
            error = "Не удалось прочитать папку\n" + ex.Message;
        }
        if (version != _loadVersion) return;

        _list.SetItems(items, keep);
        if (!keepCanvas) _list.PlayEnter();
        if (error != null) _list.ShowMessage(error);
        else if (tag != null && items.Count == 0) _list.ShowMessage("Нет файлов с тегом «" + tag.Name + "».\nНазначьте его через меню файла «Теги» или перетащите файлы на тег.");
        if (tag != null)
        {
            // Files from many folders: no canvas here.
            if (!keepCanvas) CanvasArea.LoadVirtual();
        }
        else if (keepCanvas) CanvasArea.SyncItems(items);
        else CanvasArea.LoadFolder(folder, items);
        SyncPinnedFlags();

        if (_pendingReveal != null)
        {
            _list.Reveal(_pendingReveal);
            _pendingReveal = null;
        }
        else if (!keepCanvas) _list.FocusList();
    }

    /// <summary>The first path segment of <paramref name="inner"/> below <paramref name="outer"/>, if it lies inside it.</summary>
    internal static string? ChildOnTheWay(string outer, string inner)
    {
        var o = Path.EndsInDirectorySeparator(outer) ? outer : outer + Path.DirectorySeparatorChar;
        if (!inner.StartsWith(o, StringComparison.OrdinalIgnoreCase) || inner.Length <= o.Length) return null;
        var rest = inner[o.Length..];
        var cut = rest.IndexOf(Path.DirectorySeparatorChar);
        return cut < 0 ? rest : rest[..cut];
    }

    static string? NearestExisting(string folder)
    {
        for (var d = Directory.GetParent(folder); d != null; d = d.Parent)
            if (d.Exists) return d.FullName;
        return null;
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
        var infos = dir.EnumerateFileSystemInfos("*", options)
            .Where(i => i.Name != LayoutStore.FileName && !i.Name.EndsWith(".~tmp", StringComparison.OrdinalIgnoreCase))
            .ToList();
        // Tags live in the files themselves: files brought here by other programs bring theirs.
        TagStore.Current.Reconcile(folder, infos.Select(i => i.FullName));
        var list = infos.Select(i => new FileItem(i) { Tags = TagMark.For(i.FullName) }).ToList();
        SortItems(list);
        return list;
    }

    static void SortItems(List<FileItem> list) =>
        list.Sort((a, b) => a.IsFolder != b.IsFolder ? (a.IsFolder ? -1 : 1) : Native.StrCmpLogicalW(a.Name, b.Name));

    /// <summary>Everything that carries the tag, from any folder.</summary>
    static List<FileItem> EnumerateTag(string tagId)
    {
        var list = new List<FileItem>();
        foreach (var path in TagStore.Current.PathsOf(tagId))
        {
            try
            {
                FileSystemInfo info = Directory.Exists(path) ? new DirectoryInfo(path) : new FileInfo(path);
                if (info.Exists) list.Add(new FileItem(info) { Tags = TagMark.For(path) });
            }
            catch { }
        }
        SortItems(list);
        return list;
    }

    void SyncPinnedFlags()
    {
        var pinned = new HashSet<string>(CanvasArea.PinnedNames, StringComparer.OrdinalIgnoreCase);
        var count = 0;
        foreach (var it in _list.Items)
        {
            it.IsPinned = pinned.Contains(it.Name);
            if (it.IsPinned) count++;
        }
        // Pins kept for files that are gone (renamed, deleted, drive offline) are not counted.
        _list.PinnedCount = count;
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
            // Buffer overflow or the folder itself vanished: re-read everything.
            w.Error += (_, _) => Dispatcher.BeginInvoke(() => { _refreshTimer.Stop(); _refreshTimer.Start(); });
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
        var name = e.Name;
        if (name == null || name == LayoutStore.FileName || name.EndsWith(".~tmp", StringComparison.OrdinalIgnoreCase)) return;
        var written = e.ChangeType is WatcherChangeTypes.Changed or WatcherChangeTypes.Created;
        var folder = Path.GetDirectoryName(e.FullPath);
        Dispatcher.BeginInvoke(() =>
        {
            if (written && Shell.SameFolder(folder, _current)) _changedFiles.Add(name);
            _refreshTimer.Stop();
            _refreshTimer.Start();
        });
    }

    void OnRenamed(string? oldName, string? newName, string fullPath)
    {
        // Tags follow a file renamed by any program.
        if (oldName != null && newName != null && Path.GetDirectoryName(fullPath) is { } dir)
            TagStore.Current.Move(Path.Combine(dir, oldName), fullPath);
        // Editors often save by writing a temporary file and renaming it over the original.
        var replaced = newName != null && oldName != null && CanvasArea.PinnedNames.Contains(newName, StringComparer.OrdinalIgnoreCase);
        if (oldName != null && newName != null && File.Exists(fullPath) && !replaced)
            CanvasArea.RenameFile(oldName, new FileItem(new FileInfo(fullPath)));
        if (replaced) _changedFiles.Add(newName!);
        _refreshTimer.Stop();
        _refreshTimer.Start();
    }

    // ---------- breadcrumb & path editing ----------

    static string FolderName(string path)
    {
        var name = Path.GetFileName(path.TrimEnd('\\'));
        if (!string.IsNullOrEmpty(name)) return name;
        try { return Sidebar.DriveLabel(new DriveInfo(path)); }
        catch { return path; }
    }

    /// <summary>Tab and window title: a folder name, or the tag's name for a tag view.</summary>
    static string LocationTitle(string location) =>
        IsTag(location) ? TagOf(location)?.Name ?? "Тег" : FolderName(location);

    void UpdateCrumbs()
    {
        Crumbs.Children.Clear();
        if (_current == null) return;
        if (TagOf(_current) is { } tag)
        {
            AddCrumbText("Теги", 0);
            var chevron = new UiIcon(Icons.Chevron, 11, 2.4) { VerticalAlignment = VerticalAlignment.Center, Margin = new Thickness(1, 0, 5, 0) };
            chevron.SetResourceReference(UiIcon.ForegroundProperty, "FaintBrush");
            Crumbs.Children.Add(chevron);
            var mark = TagMark.Visual(tag, 9, 13);
            mark.Margin = new Thickness(0, 0, 6, 0);
            Crumbs.Children.Add(mark);
            var name = new TextBlock { Text = tag.Name, FontWeight = FontWeights.SemiBold, VerticalAlignment = VerticalAlignment.Center };
            name.SetResourceReference(TextBlock.ForegroundProperty, "TextBrush");
            Crumbs.Children.Add(name);
            return;
        }
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
        PathEdit.Text = CurrentFolder ?? "";
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
        if (!LocationExists(path)) return;
        var tab = new FolderTab { Path = IsTag(path) ? path : Path.GetFullPath(path) };
        _tabs.Insert(_tabs.IndexOf(_tab) + 1, tab);
        _justOpened = tab;
        SwitchTo(tab);
    }

    void SwitchTo(FolderTab tab)
    {
        if (tab == _tab) return;
        PathEdit.Visibility = Visibility.Collapsed;
        Crumbs.Visibility = Visibility.Visible;
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
        _justOpened = null;
        FitTabs();
    }

    /// <summary>Tabs share the strip: they shrink when there are many.</summary>
    void FitTabs()
    {
        var available = TabStrip.ActualWidth - 36;
        if (available <= 0 || _tabs.Count == 0) return;
        var width = Math.Clamp(available / _tabs.Count - 4, 64, 210);
        foreach (var b in TabStrip.Children.OfType<Border>()) b.MaxWidth = width;
    }

    FrameworkElement MakeTab(FolderTab tab)
    {
        var active = tab == _tab;
        var tabTag = TagOf(tab.Path);
        FrameworkElement icon;
        if (tabTag != null) icon = TagMark.Visual(tabTag, 9, 13);
        else
        {
            var folderIcon = new UiIcon(Icons.Folder, 13);
            folderIcon.SetResourceReference(UiIcon.ForegroundProperty, active ? "AccentBrush" : "MutedBrush");
            icon = folderIcon;
        }
        icon.VerticalAlignment = VerticalAlignment.Center;
        icon.Margin = new Thickness(0, 0, 7, 0);
        var title = new TextBlock
        {
            Text = LocationTitle(tab.Path),
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

        // Files dragged onto a tab: hovering switches to it, dropping moves/copies into its folder.
        border.AllowDrop = true;
        border.DragEnter += (_, e) =>
        {
            if (!e.Data.GetDataPresent(DataFormats.FileDrop) || tab == _tab) return;
            _tabHoverTarget = tab;
            _tabHoverTimer.Stop();
            _tabHoverTimer.Start();
        };
        border.DragLeave += (_, _) => { if (_tabHoverTarget == tab) _tabHoverTimer.Stop(); };
        border.DragOver += (_, e) =>
        {
            e.Effects = e.Data.GetData(DataFormats.FileDrop) is not string[] files ? DragDropEffects.None
                : tabTag != null ? DragDropEffects.Link
                : Shell.DropEffect(files, tab.Path, e.KeyStates, e.AllowedEffects);
            e.Handled = true;
        };
        border.Drop += (_, e) =>
        {
            _tabHoverTimer.Stop();
            // Files dropped on a tag tab get the tag; on a folder tab they are moved or copied there.
            if (tabTag != null && e.Data.GetData(DataFormats.FileDrop) is string[] files)
            {
                TagStore.Current.Set(files, tabTag.Id, true);
                e.Effects = DragDropEffects.Link;
            }
            else e.Effects = Shell.Drop(e, tab.Path, _hwnd);
            e.Handled = true;
        };

        if (tab == _justOpened)
        {
            var scale = new ScaleTransform(0.9, 1);
            border.RenderTransformOrigin = new Point(0, 0.5);
            border.RenderTransform = scale;
            var d = TimeSpan.FromMilliseconds(Anim.Short);
            border.BeginAnimation(OpacityProperty, new System.Windows.Media.Animation.DoubleAnimation(0, 1, d));
            scale.BeginAnimation(ScaleTransform.ScaleXProperty, new System.Windows.Media.Animation.DoubleAnimation(0.9, 1, d));
        }
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
        var folder = Path.GetDirectoryName(item.FullPath)!;
        var dot = item.IsFolder ? -1 : item.Name.LastIndexOf('.');
        var name = InputDialog.Ask(this, "Переименовать", item.Name, dot > 0 ? dot : -1, s => NameProblem(s, folder, item.Name));
        if (name == null) return;
        name = CleanName(name);
        if (name == item.Name) return;
        if (Rename(item.FullPath, name, item.IsFolder)) _pendingReveal = name;
    }

    /// <summary>Windows drops trailing dots and spaces from names; do it up front so the name shown is the name stored.</summary>
    static string CleanName(string name) => name.Trim().TrimEnd('.', ' ');

    static readonly HashSet<string> ReservedNames = new(StringComparer.OrdinalIgnoreCase)
    {
        "CON", "PRN", "AUX", "NUL", "COM1", "COM2", "COM3", "COM4", "COM5", "COM6", "COM7", "COM8", "COM9",
        "LPT1", "LPT2", "LPT3", "LPT4", "LPT5", "LPT6", "LPT7", "LPT8", "LPT9",
    };

    /// <summary>Why <paramref name="raw"/> can't be used as a name in <paramref name="folder"/>, or null if it can.</summary>
    internal static string? NameProblem(string raw, string folder, string? currentName)
    {
        var name = CleanName(raw);
        if (name.Length == 0) return "Введите имя.";
        if (name.IndexOfAny(Path.GetInvalidFileNameChars()) >= 0) return "Имя не может содержать символы \\ / : * ? \" < > |";
        if (ReservedNames.Contains(Path.GetFileNameWithoutExtension(name))) return "Это имя зарезервировано Windows.";
        if (string.Equals(name, currentName, StringComparison.OrdinalIgnoreCase)) return null;
        var target = Path.Combine(folder, name);
        if (File.Exists(target) || Directory.Exists(target)) return "Файл или папка с таким именем уже есть.";
        return null;
    }

    /// <summary>Renames a file or folder in place; reports a failure to the user.</summary>
    bool Rename(string path, string newName, bool isFolder)
    {
        var dest = Path.Combine(Path.GetDirectoryName(path)!, newName);
        try
        {
            if (isFolder)
            {
                // A folder can't be moved onto its own name in another letter case directly.
                if (string.Equals(path, dest, StringComparison.OrdinalIgnoreCase))
                {
                    var step = dest + ".~ren" + Environment.TickCount;
                    Directory.Move(path, step);
                    Directory.Move(step, dest);
                }
                else Directory.Move(path, dest);
            }
            else File.Move(path, dest);
            TagStore.Current.Move(path, dest);
            return true;
        }
        catch (Exception ex)
        {
            MessageBox.Show(this, ex.Message, "Переименование", MessageBoxButton.OK, MessageBoxImage.Warning);
            return false;
        }
    }

    public void CreateFolder()
    {
        if (CurrentFolder == null || _current == null) return;
        var name = "Новая папка";
        for (var i = 2; Directory.Exists(Path.Combine(_current, name)) || File.Exists(Path.Combine(_current, name)); i++)
            name = $"Новая папка ({i})";
        try
        {
            Directory.CreateDirectory(Path.Combine(_current, name));
        }
        catch (Exception ex)
        {
            MessageBox.Show(this, ex.Message, "Новая папка", MessageBoxButton.OK, MessageBoxImage.Warning);
            return;
        }
        _pendingReveal = AskNewName(name, isFolder: true);
        RefreshFolder();
    }

    public void CreateFile(string baseName, string extension)
    {
        if (CurrentFolder == null || _current == null) return;
        var name = baseName + extension;
        for (var i = 2; File.Exists(Path.Combine(_current, name)) || Directory.Exists(Path.Combine(_current, name)); i++)
            name = $"{baseName} ({i}){extension}";
        try
        {
            File.WriteAllBytes(Path.Combine(_current, name), Array.Empty<byte>());
        }
        catch (Exception ex)
        {
            MessageBox.Show(this, ex.Message, "Новый файл", MessageBoxButton.OK, MessageBoxImage.Warning);
            return;
        }
        _pendingReveal = AskNewName(name, isFolder: false);
        RefreshFolder();
    }

    /// <summary>Offers to rename a just-created item; returns its final name.</summary>
    string AskNewName(string name, bool isFolder)
    {
        var folder = _current!;
        var dot = isFolder ? -1 : name.LastIndexOf('.');
        var wanted = InputDialog.Ask(this, isFolder ? "Имя новой папки" : "Имя нового файла", name, dot > 0 ? dot : -1,
            s => NameProblem(s, folder, name));
        if (wanted == null) return name;
        wanted = CleanName(wanted);
        if (wanted == name) return name;
        return Rename(Path.Combine(folder, name), wanted, isFolder) ? wanted : name;
    }
}

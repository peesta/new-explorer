using System.IO;
using System.Windows;
using System.Windows.Automation;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media;
using CanvasExplorer.Models;
using CanvasExplorer.Services;

namespace CanvasExplorer.UI;

/// <summary>What the sidebar needs from the main window.</summary>
public interface ISidebarHost
{
    /// <summary>What is shown now: a folder path or a tag view ("tag:&lt;id&gt;").</summary>
    string? CurrentLocation { get; }
    Window OwnerWindow { get; }
    IntPtr Hwnd { get; }
    void NavigateTo(string location);
    void OpenInNewTab(string location);
}

/// <summary>
/// The left panel: favorites (editable: add, remove, rename, reorder by dragging or from the menu), drives
/// (refreshed when a drive comes or goes) and tags (with counts and a "Новый тег" button). Every row and the
/// empty space have their own context menu.
/// </summary>
public sealed class Sidebar
{
    public const string TagPrefix = "tag:";
    const string FavoriteFormat = "CanvasExplorer.Favorite";
    static readonly Guid DownloadsId = new("374DE290-123F-4565-9164-39C4925E467B");

    readonly StackPanel _panel;
    readonly ISidebarHost _host;
    readonly List<RadioButton> _rows = new();
    Point? _dragFrom;
    bool _dragged;

    public Sidebar(StackPanel panel, FrameworkElement area, ISidebarHost host)
    {
        _panel = panel;
        _host = host;
        // Right click on empty space (or a section title) opens the panel menu.
        area.ContextMenu = new ContextMenu();
        area.ContextMenuOpening += (_, e) =>
        {
            if (FindRow(e.OriginalSource as DependencyObject) != null) return;
            area.ContextMenu = PanelMenu();
        };
        TagStore.Current.Changed += (_, _) => _panel.Dispatcher.BeginInvoke(Build);
    }

    // ---------- favorites ----------

    static List<Favorite> DefaultFavorites()
    {
        var list = new List<Favorite>();
        void Add(string? path) { if (!string.IsNullOrEmpty(path)) list.Add(new Favorite { Path = path }); }
        Add(Environment.GetFolderPath(Environment.SpecialFolder.Desktop));
        Add(Native.SHGetKnownFolderPath(DownloadsId, 0, IntPtr.Zero, out var downloads) == 0 ? downloads : null);
        Add(Environment.GetFolderPath(Environment.SpecialFolder.MyDocuments));
        Add(Environment.GetFolderPath(Environment.SpecialFolder.MyPictures));
        Add(Environment.GetFolderPath(Environment.SpecialFolder.MyMusic));
        Add(Environment.GetFolderPath(Environment.SpecialFolder.MyVideos));
        Add(Environment.GetFolderPath(Environment.SpecialFolder.UserProfile));
        return list;
    }

    /// <summary>The favorites in order (the standard set until the user changes something).</summary>
    public static List<Favorite> Favorites => AppSettings.Current.Favorites ??= DefaultFavorites();

    public static bool IsFavorite(string path) => Favorites.Any(f => Shell.SameFolder(f.Path, path));

    public void AddFavorite(string path, int index = -1)
    {
        if (!Directory.Exists(path) || IsFavorite(path)) return;
        var list = Favorites;
        list.Insert(index < 0 || index > list.Count ? list.Count : index, new Favorite { Path = Path.TrimEndingDirectorySeparator(path) });
        Changed();
    }

    void RemoveFavorite(string path)
    {
        Favorites.RemoveAll(f => Shell.SameFolder(f.Path, path));
        Changed();
    }

    void MoveFavorite(string path, int to)
    {
        var list = Favorites;
        var i = list.FindIndex(f => Shell.SameFolder(f.Path, path));
        if (i < 0) return;
        var fav = list[i];
        list.RemoveAt(i);
        list.Insert(Math.Clamp(to > i ? to - 1 : to, 0, list.Count), fav);
        Changed();
    }

    void RenameFavorite(Favorite fav)
    {
        var shown = DisplayName(fav);
        var name = InputDialog.Ask(_host.OwnerWindow, "Название в избранном", shown,
            validate: s => s.Length == 0 ? "Введите название." : null);
        if (name == null || name == shown) return;
        // The folder's own name again means "no custom name".
        fav.Name = name == FolderTitle(fav.Path) ? null : name;
        Changed();
    }

    void Changed()
    {
        AppSettings.ScheduleSave();
        Build();
    }

    static readonly Dictionary<string, (string Label, Geometry Icon)> Known = new(StringComparer.OrdinalIgnoreCase);

    static (string Label, Geometry Icon) KnownLook(string path)
    {
        if (Known.Count == 0)
        {
            void K(string? p, string label, Geometry icon) { if (!string.IsNullOrEmpty(p)) Known[Path.TrimEndingDirectorySeparator(p)] = (label, icon); }
            K(Environment.GetFolderPath(Environment.SpecialFolder.Desktop), "Рабочий стол", Icons.Desktop);
            K(Native.SHGetKnownFolderPath(DownloadsId, 0, IntPtr.Zero, out var d) == 0 ? d : null, "Загрузки", Icons.Download);
            K(Environment.GetFolderPath(Environment.SpecialFolder.MyDocuments), "Документы", Icons.Doc);
            K(Environment.GetFolderPath(Environment.SpecialFolder.MyPictures), "Изображения", Icons.Image);
            K(Environment.GetFolderPath(Environment.SpecialFolder.MyMusic), "Музыка", Icons.Music);
            K(Environment.GetFolderPath(Environment.SpecialFolder.MyVideos), "Видео", Icons.Video);
            K(Environment.GetFolderPath(Environment.SpecialFolder.UserProfile), "Пользователь", Icons.Home);
        }
        return Known.TryGetValue(Path.TrimEndingDirectorySeparator(path), out var look) ? look : (FolderTitle(path), Icons.Folder);
    }

    static string FolderTitle(string path)
    {
        var name = Path.GetFileName(Path.TrimEndingDirectorySeparator(path));
        return string.IsNullOrEmpty(name) ? path : name;
    }

    static string DisplayName(Favorite f) => f.Name ?? KnownLook(f.Path).Label;

    // ---------- drives ----------

    public static string DriveLabel(DriveInfo d)
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

    // ---------- building ----------

    public void Build()
    {
        _panel.Children.Clear();
        _rows.Clear();

        var favHeader = Section("Избранное");
        // Folders dropped on the section title become favorites.
        favHeader.AllowDrop = true;
        favHeader.Background = Brushes.Transparent;
        favHeader.DragOver += (_, e) =>
        {
            e.Effects = DroppedFolders(e).Count > 0 || e.Data.GetDataPresent(FavoriteFormat) ? DragDropEffects.Link : DragDropEffects.None;
            e.Handled = true;
        };
        favHeader.Drop += (_, e) =>
        {
            if (e.Data.GetData(FavoriteFormat) is string moving) MoveFavorite(moving, 0);
            else foreach (var f in DroppedFolders(e)) AddFavorite(f, 0);
            e.Handled = true;
        };
        foreach (var fav in Favorites) AddFavoriteRow(fav);

        Section("Расположения");
        foreach (var d in DriveInfo.GetDrives())
        {
            bool ready;
            try { ready = d.IsReady; } catch { ready = false; }
            if (!ready) continue;
            var path = d.RootDirectory.FullName;
            var row = Row(DriveLabel(d), new Icon(Icons.Drive, 15), path, null, path);
            row.ContextMenu = new ContextMenu();
            row.ContextMenuOpening += (_, _) => row.ContextMenu = Menus.Build(
                Menus.Item("Открыть", Icons.Open, () => _host.NavigateTo(path)),
                Menus.Item("Открыть в новой вкладке", Icons.Plus, () => _host.OpenInNewTab(path)),
                Menus.Sep(),
                IsFavorite(path) ? null : Menus.Item("Добавить в избранное", Icons.Star, () => AddFavorite(path)),
                Menus.Item("Копировать путь", Icons.Path, () => Shell.SetClipboardText(path)),
                Menus.Item("Свойства", Icons.Info, () => Shell.ShowProperties(path)));
            AcceptFileDrops(row, path);
        }

        Section("Теги");
        var store = TagStore.Current;
        foreach (var tag in store.Tags) AddTagRow(tag, store.Count(tag.Id));
        _panel.Children.Add(NewTagButton());

        Highlight();
    }

    TextBlock Section(string title)
    {
        var tb = new TextBlock
        {
            Text = title,
            FontSize = 11,
            FontWeight = FontWeights.SemiBold,
            Margin = new Thickness(8, _panel.Children.Count == 0 ? 0 : 14, 0, 4),
            Padding = new Thickness(0, 0, 0, 0),
        };
        tb.SetResourceReference(TextBlock.ForegroundProperty, "MutedBrush");
        _panel.Children.Add(tb);
        return tb;
    }

    RadioButton Row(string label, FrameworkElement mark, string key, string? count, string? tip)
    {
        var content = new DockPanel();
        var markBox = new Border { Width = 15, Height = 15, Margin = new Thickness(0, 0, 8, 0), VerticalAlignment = VerticalAlignment.Center, Child = mark };
        if (mark is Icon ic) ic.SetResourceReference(Icon.ForegroundProperty, "SubBrush");
        DockPanel.SetDock(markBox, Dock.Left);
        content.Children.Add(markBox);
        if (count != null)
        {
            var c = new TextBlock { Text = count, FontSize = 11, VerticalAlignment = VerticalAlignment.Center, Margin = new Thickness(6, 0, 0, 0) };
            c.SetResourceReference(TextBlock.ForegroundProperty, "MutedBrush");
            DockPanel.SetDock(c, Dock.Right);
            content.Children.Add(c);
        }
        content.Children.Add(new TextBlock { Text = label, VerticalAlignment = VerticalAlignment.Center, TextTrimming = TextTrimming.CharacterEllipsis });

        var rb = new RadioButton
        {
            Content = content,
            Tag = key,
            GroupName = "Side",
            Style = (Style)Application.Current.FindResource("SideItem"),
            ToolTip = tip,
        };
        ToolTipService.SetInitialShowDelay(rb, 1200);
        AutomationProperties.SetName(rb, label);
        rb.Click += (_, _) =>
        {
            if (_dragged) { _dragged = false; Highlight(); return; }
            _host.NavigateTo(key);
        };
        rb.PreviewMouseDown += (_, e) =>
        {
            if (e.ChangedButton != MouseButton.Middle) return;
            _host.OpenInNewTab(key);
            e.Handled = true;
        };
        _rows.Add(rb);
        _panel.Children.Add(rb);
        return rb;
    }

    void AddFavoriteRow(Favorite fav)
    {
        var path = fav.Path;
        var exists = Directory.Exists(path);
        var row = Row(DisplayName(fav), new Icon(KnownLook(path).Icon, 15), path, null, exists ? path : path + "\nПапка не найдена");
        if (!exists) row.Opacity = 0.5;
        row.ContextMenu = new ContextMenu();
        row.ContextMenuOpening += (_, _) =>
        {
            var i = Favorites.IndexOf(fav);
            row.ContextMenu = Menus.Build(
                Menus.Item("Открыть", Icons.Open, () => _host.NavigateTo(path), enabled: exists),
                Menus.Item("Открыть в новой вкладке", Icons.Plus, () => _host.OpenInNewTab(path), enabled: exists),
                Menus.Sep(),
                Menus.Item("Переименовать…", Icons.Rename, () => RenameFavorite(fav)),
                Menus.Item("Переместить вверх", Icons.ArrowUp, () => MoveFavorite(path, i - 1), enabled: i > 0),
                Menus.Item("Переместить вниз", Icons.ArrowDown, () => MoveFavorite(path, i + 2), enabled: i < Favorites.Count - 1),
                Menus.Item("Убрать из избранного", Icons.Close, () => RemoveFavorite(path), danger: true),
                Menus.Sep(),
                Menus.Item("Копировать путь", Icons.Path, () => Shell.SetClipboardText(path)),
                Menus.Item("Свойства", Icons.Info, () => Shell.ShowProperties(path), enabled: exists));
        };
        if (exists) AcceptFileDrops(row, path);

        // Dragging a favorite onto another one reorders them.
        row.PreviewMouseLeftButtonDown += (_, e) => { _dragFrom = e.GetPosition(_panel); _dragged = false; };
        row.PreviewMouseMove += (_, e) =>
        {
            if (_dragFrom is not { } from || e.LeftButton != MouseButtonState.Pressed) return;
            var p = e.GetPosition(_panel);
            if (Math.Abs(p.Y - from.Y) < SystemParameters.MinimumVerticalDragDistance && Math.Abs(p.X - from.X) < SystemParameters.MinimumHorizontalDragDistance) return;
            _dragFrom = null;
            _dragged = true;
            var data = new DataObject(FavoriteFormat, path);
            // Also a folder: it can be dropped into the file list or another program.
            if (exists) data.SetFileDropList(new System.Collections.Specialized.StringCollection { path });
            DragDrop.DoDragDrop(row, data, DragDropEffects.Move | DragDropEffects.Copy | DragDropEffects.Link);
            row.Dispatcher.BeginInvoke(() => _dragged = false, System.Windows.Threading.DispatcherPriority.Input);
        };
        row.PreviewMouseLeftButtonUp += (_, _) => _dragFrom = null;
    }

    void AddTagRow(TagDef tag, int count)
    {
        var key = TagPrefix + tag.Id;
        var row = Row(tag.Name, TagMark.Visual(tag), key, count.ToString(), null);
        row.ToolTip = "Показать " + Format.Plural(count, "файл", "файла", "файлов") + " с тегом «" + tag.Name + "»";
        row.ContextMenu = new ContextMenu();
        row.ContextMenuOpening += (_, _) => row.ContextMenu = Menus.Build(
            Menus.Item("Показать файлы", Icons.Open, () => _host.NavigateTo(key)),
            Menus.Item("Открыть в новой вкладке", Icons.Plus, () => _host.OpenInNewTab(key)),
            Menus.Sep(),
            Menus.Item("Переименовать…", Icons.Rename, () => TagDialog.Show(_host.OwnerWindow, row, tag)),
            Menus.Item(tag.Icon != null ? "Изменить иконку…" : "Изменить цвет…", tag.Icon != null ? Icons.Star : Icons.Tag, () => TagDialog.Show(_host.OwnerWindow, row, tag)),
            Menus.Item("Удалить тег", Icons.Trash, () => DeleteTag(tag), danger: true),
            Menus.Sep(),
            Menus.Item("Новый тег…", Icons.Plus, () => NewTag(row)));

        // Files dropped on a tag get it.
        row.AllowDrop = true;
        row.DragOver += (_, e) =>
        {
            e.Effects = e.Data.GetData(DataFormats.FileDrop) is string[] { Length: > 0 } ? DragDropEffects.Link : DragDropEffects.None;
            e.Handled = true;
        };
        row.Drop += (_, e) =>
        {
            if (e.Data.GetData(DataFormats.FileDrop) is string[] files) TagStore.Current.Set(files, tag.Id, true);
            e.Effects = DragDropEffects.Link;
            e.Handled = true;
        };
    }

    void DeleteTag(TagDef tag)
    {
        var count = TagStore.Current.Count(tag.Id);
        if (count > 0 && MessageBox.Show(_host.OwnerWindow,
                $"Удалить тег «{tag.Name}»? Он будет снят с {Format.Plural(count, "файла", "файлов", "файлов")}. Сами файлы не удаляются.",
                "Удалить тег", MessageBoxButton.OKCancel, MessageBoxImage.Question) != MessageBoxResult.OK)
            return;
        var showing = _host.CurrentLocation == TagPrefix + tag.Id;
        TagStore.Current.Delete(tag.Id);
        if (showing) _host.NavigateTo(Environment.GetFolderPath(Environment.SpecialFolder.UserProfile));
    }

    Button NewTagButton()
    {
        var plus = new Icon(Icons.Plus, 12, 2.4) { VerticalAlignment = VerticalAlignment.Center };
        var content = new StackPanel { Orientation = Orientation.Horizontal };
        content.Children.Add(new Border { Width = 15, Margin = new Thickness(0, 0, 8, 0), Child = plus });
        content.Children.Add(new TextBlock { Text = "Новый тег", VerticalAlignment = VerticalAlignment.Center });
        var b = new Button
        {
            Content = content,
            Style = (Style)Application.Current.FindResource("SideButton"),
            ToolTip = "Создать тег",
        };
        b.SetResourceReference(Control.ForegroundProperty, "SubBrush");
        AutomationProperties.SetAutomationId(b, "NewTagBtn");
        AutomationProperties.SetName(b, "Новый тег");
        b.Click += (_, _) => NewTag(b);
        return b;
    }

    /// <summary>Opens the "new tag" popup next to <paramref name="anchor"/> and assigns it to <paramref name="files"/>.</summary>
    public TagDef? NewTag(FrameworkElement? anchor, IReadOnlyList<string>? files = null)
    {
        var tag = TagDialog.Show(_host.OwnerWindow, anchor ?? _panel.Children.OfType<Button>().LastOrDefault());
        if (tag != null && files is { Count: > 0 }) TagStore.Current.Set(files, tag.Id, true);
        return tag;
    }

    ContextMenu PanelMenu()
    {
        var current = _host.CurrentLocation;
        var canAdd = current != null && !current.StartsWith(TagPrefix) && Directory.Exists(current) && !IsFavorite(current);
        return Menus.Build(
            Menus.Item("Новый тег…", Icons.Tag, () => NewTag(null)),
            canAdd ? Menus.Item("Добавить текущую папку в избранное", Icons.Star, () => AddFavorite(current!)) : null,
            Menus.Sep(),
            Menus.Item("Восстановить стандартное избранное", Icons.Refresh, () =>
            {
                AppSettings.Current.Favorites = null;
                Changed();
            }));
    }

    void AcceptFileDrops(RadioButton row, string path)
    {
        row.AllowDrop = true;
        row.DragOver += (_, e) =>
        {
            if (e.Data.GetData(FavoriteFormat) is string)
            {
                e.Effects = DragDropEffects.Move;
                e.Handled = true;
                return;
            }
            e.Effects = e.Data.GetData(DataFormats.FileDrop) is string[] files
                ? Shell.DropEffect(files, path, e.KeyStates, e.AllowedEffects)
                : DragDropEffects.None;
            e.Handled = true;
        };
        row.Drop += (_, e) =>
        {
            if (e.Data.GetData(FavoriteFormat) is string moving)
            {
                // A favorite dropped on a row lands before it (or after the last one, when dropped on a drive).
                var target = Favorites.FindIndex(f => Shell.SameFolder(f.Path, path));
                MoveFavorite(moving, target < 0 ? Favorites.Count : target);
                e.Effects = DragDropEffects.Move;
            }
            else e.Effects = Shell.Drop(e, path, _host.Hwnd);
            e.Handled = true;
        };
    }

    static List<string> DroppedFolders(DragEventArgs e) =>
        e.Data.GetData(DataFormats.FileDrop) is string[] files ? files.Where(Directory.Exists).ToList() : new List<string>();

    static RadioButton? FindRow(DependencyObject? d)
    {
        for (; d != null; d = d is Visual ? VisualTreeHelper.GetParent(d) : LogicalTreeHelper.GetParent(d))
            if (d is RadioButton rb) return rb;
        return null;
    }

    public void Highlight()
    {
        var current = _host.CurrentLocation;
        foreach (var rb in _rows)
        {
            var key = (string)rb.Tag;
            var on = key.StartsWith(TagPrefix) ? key == current : Shell.SameFolder(key, current);
            rb.IsChecked = on;
            if (rb.Content is DockPanel { Children.Count: > 0 } dp && dp.Children[0] is Border { Child: Icon ic })
                ic.SetResourceReference(Icon.ForegroundProperty, on ? "AccentBrush" : "SubBrush");
        }
    }
}

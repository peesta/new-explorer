using System.Diagnostics;
using System.IO;
using System.Runtime.InteropServices;
using System.Text.Json;
using System.Windows;
using System.Windows.Automation;
using CanvasExplorer.Models;

namespace CanvasExplorer.Tests.Infrastructure;

/// <summary>
/// Starts the real CanvasExplorer.exe on a scratch folder with its own settings, and drives it the way a person
/// would: real mouse and keyboard, state read back through UI Automation and from the files the app writes.
/// </summary>
sealed class AppSession : IDisposable
{
    static readonly JsonSerializerOptions Json = new() { PropertyNameCaseInsensitive = true };

    Process? _process;

    public AppSession()
    {
        Root = TestSetup.NewFolder("ui");
        Work = Path.Combine(Root, "Папка");
        Data = Path.Combine(Root, "data");
        Directory.CreateDirectory(Work);
        Directory.CreateDirectory(Data);
        // Fixed window geometry on the primary screen, so coordinates are predictable.
        File.WriteAllText(Path.Combine(Data, "settings.json"),
            """{ "WinLeft": 60, "WinTop": 40, "WinWidth": 1180, "WinHeight": 740, "SidebarWidth": 196, "Density": 1, "GridView": false }""");
    }

    /// <summary>Scratch root of this session.</summary>
    public string Root { get; }
    /// <summary>The folder the app opens.</summary>
    public string Work { get; }
    /// <summary>Settings folder (CANVAS_EXPLORER_DATA).</summary>
    public string Data { get; }

    public AutomationElement Window { get; private set; } = null!;
    public IntPtr Hwnd { get; private set; }
    public bool IsRunning => _process is { HasExited: false };

    // ---------- files ----------

    public string WriteFile(string relative, string content = "")
    {
        var path = Path.Combine(Work, relative);
        Directory.CreateDirectory(Path.GetDirectoryName(path)!);
        File.WriteAllText(path, content);
        return path;
    }

    public string MakeDir(string relative)
    {
        var path = Path.Combine(Work, relative);
        Directory.CreateDirectory(path);
        return path;
    }

    public string LayoutPath(string? folder = null) => Path.Combine(folder ?? Work, ".canvas.json");

    public FolderLayout? Layout(string? folder = null)
    {
        var path = LayoutPath(folder);
        for (var i = 0; i < 5; i++)
        {
            try
            {
                if (!File.Exists(path)) return null;
                using var fs = new FileStream(path, FileMode.Open, FileAccess.Read, FileShare.ReadWrite | FileShare.Delete);
                return JsonSerializer.Deserialize<FolderLayout>(fs, Json);
            }
            catch (IOException) { Thread.Sleep(50); }
            catch (JsonException) { Thread.Sleep(50); }
        }
        return null;
    }

    public void WriteLayout(FolderLayout layout, string? folder = null)
    {
        var path = LayoutPath(folder);
        File.WriteAllText(path, JsonSerializer.Serialize(layout, new JsonSerializerOptions { PropertyNamingPolicy = JsonNamingPolicy.CamelCase }));
        File.SetAttributes(path, FileAttributes.Hidden);
    }

    /// <summary>Replaces the seeded settings (before <see cref="Launch"/>); window geometry is kept fixed.</summary>
    public void SeedSettings(string extraJsonProperties)
    {
        File.WriteAllText(Path.Combine(Data, "settings.json"),
            "{ \"WinLeft\": 60, \"WinTop\": 40, \"WinWidth\": 1180, \"WinHeight\": 740, \"SidebarWidth\": 196, " + extraJsonProperties + " }");
    }

    public string TagsPath => Path.Combine(Data, "tags.json");

    /// <summary>Tags known before launch: (id, name, colour or null, icon or null).</summary>
    public void SeedTags(params (string Id, string Name, string? Color, string? Icon)[] tags)
    {
        var json = JsonSerializer.Serialize(new
        {
            tags = tags.Select(t => new { id = t.Id, name = t.Name, color = t.Color, icon = t.Icon }),
            files = new Dictionary<string, string[]>(),
        });
        File.WriteAllText(TagsPath, json);
    }

    public JsonElement Tags()
    {
        for (var i = 0; ; i++)
        {
            try
            {
                using var doc = JsonDocument.Parse(File.ReadAllText(TagsPath));
                return doc.RootElement.Clone();
            }
            catch (Exception) when (i < 10) { Thread.Sleep(50); }
        }
    }

    /// <summary>Tag ids on a file, from tags.json.</summary>
    public IReadOnlyList<string> TagIdsOf(string path)
    {
        if (!File.Exists(TagsPath)) return Array.Empty<string>();
        var files = Tags().GetProperty("files");
        foreach (var f in files.EnumerateObject())
            if (string.Equals(f.Name, path, StringComparison.OrdinalIgnoreCase))
                return f.Value.EnumerateArray().Select(v => v.GetString()!).ToList();
        return Array.Empty<string>();
    }

    /// <summary>A row of the left panel (favorite, drive or tag) by its label.</summary>
    public AutomationElement? FindSideItem(string name) => FindByName(name, ControlType.RadioButton);

    public AutomationElement SideItem(string name) => Wait.For(() => FindSideItem(name), 5000, $"в левой панели нет «{name}»");

    /// <summary>Empty space of the left panel, under its last row ("Новый тег").</summary>
    public Point SidebarEmptySpot()
    {
        var b = Bounds(Get("NewTagBtn"));
        return new Point(b.X + b.Width / 2, b.Bottom + 40);
    }

    /// <summary>The small number at the right of a tag row.</summary>
    public string? SideCount(string tagName) =>
        SideItem(tagName).FindAll(TreeScope.Descendants, new PropertyCondition(AutomationElement.ControlTypeProperty, ControlType.Text))
            .Cast<AutomationElement>().Select(e => e.Current.Name).FirstOrDefault(n => n.All(char.IsDigit));

    public JsonElement Settings()
    {
        using var doc = JsonDocument.Parse(File.ReadAllText(Path.Combine(Data, "settings.json")));
        return doc.RootElement.Clone();
    }

    // ---------- lifecycle ----------

    public static string ExePath
    {
        get
        {
            var local = Path.Combine(AppContext.BaseDirectory, "CanvasExplorer.exe");
            if (File.Exists(local)) return local;
            throw new FileNotFoundException("CanvasExplorer.exe not found next to the tests", local);
        }
    }

    public AppSession Launch(string? folder = null)
    {
        var psi = new ProcessStartInfo(ExePath) { UseShellExecute = false, WorkingDirectory = Root };
        psi.ArgumentList.Add(folder ?? Work);
        psi.Environment["CANVAS_EXPLORER_DATA"] = Data;
        _process = Process.Start(psi)!;
        Wait.Until(() =>
        {
            _process.Refresh();
            return _process.MainWindowHandle != IntPtr.Zero;
        }, 20000, "главное окно приложения не появилось");
        Hwnd = _process.MainWindowHandle;
        Window = AutomationElement.FromHandle(Hwnd);
        try
        {
            // Taken too early, the element is a plain Win32 proxy with no WPF content: ask again until it has it.
            Wait.Until(() =>
            {
                Window = AutomationElement.FromHandle(Hwnd);
                return Find("List") != null;
            }, 10000, "список файлов не появился");
        }
        catch (Xunit.Sdk.XunitException ex)
        {
            _process.Refresh();
            var kids = Window.FindAll(TreeScope.Children, System.Windows.Automation.Condition.TrueCondition).Count;
            throw new Xunit.Sdk.XunitException(ex.Message + $" [окно «{Window.Current.Name}» class={Window.Current.ClassName} " +
                $"hwnd={Hwnd} main={_process.MainWindowHandle} детей={kids} exited={_process.HasExited}]");
        }
        Activate();
        Thread.Sleep(400);
        return this;
    }

    /// <summary>Closes the window normally (as the close button would) and waits for the process to exit.</summary>
    public void Close()
    {
        if (!IsRunning) return;
        try { ((WindowPattern)Window.GetCurrentPattern(WindowPattern.Pattern)).Close(); }
        catch { PostMessage(Hwnd, 0x0010, IntPtr.Zero, IntPtr.Zero); }
        if (!_process!.WaitForExit(8000)) throw new TimeoutException("приложение не закрылось");
    }

    /// <summary>Simulates a crash: the process dies without any chance to save.</summary>
    public void Kill()
    {
        if (!IsRunning) return;
        _process!.Kill();
        _process.WaitForExit(5000);
    }

    public void Dispose()
    {
        try
        {
            if (IsRunning)
            {
                // Dialogs left open would block a normal close; the scratch folder goes away anyway.
                _process!.Kill();
                _process.WaitForExit(5000);
            }
        }
        catch { }
        _process?.Dispose();
        TestSetup.DeleteFolder(Root);
    }

    public void Activate()
    {
        if (GetForegroundWindow() == Hwnd) return;
        SetForegroundWindow(Hwnd);
        Thread.Sleep(150);
        if (GetForegroundWindow() != Hwnd)
        {
            // Windows refuses focus requests from background processes; a click on the empty
            // caption area right of the tabs activates the window like a person would.
            var r = Window.Current.BoundingRectangle;
            Input.Click(new Point(r.X + r.Width * 0.62, r.Y + 22));
        }
        Wait.Until(() => GetForegroundWindow() == Hwnd || OwnedDialog() != null, 3000, "окно приложения не стало активным");
    }

    // ---------- elements ----------

    public AutomationElement? Find(string automationId, AutomationElement? scope = null) =>
        (scope ?? Window).FindFirst(TreeScope.Descendants, new PropertyCondition(AutomationElement.AutomationIdProperty, automationId));

    public AutomationElement Get(string automationId, AutomationElement? scope = null) =>
        Wait.For(() => Find(automationId, scope), 5000, $"нет элемента «{automationId}»");

    public AutomationElement? FindByName(string name, ControlType? type = null, AutomationElement? scope = null)
    {
        System.Windows.Automation.Condition c = new PropertyCondition(AutomationElement.NameProperty, name);
        if (type != null) c = new AndCondition(c, new PropertyCondition(AutomationElement.ControlTypeProperty, type));
        return (scope ?? Window).FindFirst(TreeScope.Descendants, c);
    }

    public string Title => Window.Current.Name;

    public AutomationElement List => Get("List");

    public IReadOnlyList<string> Names() =>
        List.FindAll(TreeScope.Children, new PropertyCondition(AutomationElement.ControlTypeProperty, ControlType.ListItem))
            .Cast<AutomationElement>().Select(e => e.Current.Name).ToList();

    public AutomationElement Row(string name) =>
        Wait.For(() => List.FindFirst(TreeScope.Children, new AndCondition(
            new PropertyCondition(AutomationElement.ControlTypeProperty, ControlType.ListItem),
            new PropertyCondition(AutomationElement.NameProperty, name))), 5000, $"в списке нет «{name}»");

    public IReadOnlyList<string> Selected() =>
        ((SelectionPattern)List.GetCurrentPattern(SelectionPattern.Pattern)).Current.GetSelection().Select(e => e.Current.Name).ToList();

    public string CountText => Get("CountText").Current.Name;

    /// <summary>Text of the list's overlay message ("Папка пуста", "Ничего не найдено"), or null when hidden.</summary>
    public string? Message
    {
        get
        {
            var m = Find("Message");
            return m == null || m.Current.IsOffscreen || string.IsNullOrEmpty(m.Current.Name) ? null : m.Current.Name;
        }
    }

    public IReadOnlyList<AutomationElement> PinnedWindows() =>
        Window.FindAll(TreeScope.Descendants, new PropertyCondition(AutomationElement.AutomationIdProperty, "PinnedWindow"))
            .Cast<AutomationElement>().ToList();

    public AutomationElement Pinned(string name) =>
        Wait.For(() => PinnedWindows().FirstOrDefault(w => w.Current.Name == name), 5000, $"на холсте нет окна «{name}»");

    public static Rect Bounds(AutomationElement e) => e.Current.BoundingRectangle;

    public static Point Center(AutomationElement e)
    {
        var r = e.Current.BoundingRectangle;
        return new Point(r.X + r.Width / 2, r.Y + r.Height / 2);
    }

    // ---------- actions ----------

    public void Click(AutomationElement e, Input.Button button = Input.Button.Left) => Input.Click(Center(e), button);

    public void DoubleClick(AutomationElement e) => Input.Click(Center(e), count: 2);

    /// <summary>Clicks a list row on its name (the "grab" part that selects and drags the file).</summary>
    public void ClickRow(string name, Input.Button button = Input.Button.Left)
    {
        var r = Bounds(Row(name));
        Input.Click(new Point(r.X + 48, r.Y + r.Height / 2), button);
    }

    public void Invoke(AutomationElement e) => ((InvokePattern)e.GetCurrentPattern(InvokePattern.Pattern)).Invoke();

    /// <summary>Picks a context-menu item by its text (the menu must be open).</summary>
    public void MenuItem(string header)
    {
        var item = Wait.For(() => FindInPopups(header, ControlType.MenuItem), 4000, $"нет пункта меню «{header}»");
        if (item.TryGetCurrentPattern(ExpandCollapsePattern.Pattern, out var ec) &&
            ((ExpandCollapsePattern)ec).Current.ExpandCollapseState != ExpandCollapseState.LeafNode)
        {
            ((ExpandCollapsePattern)ec).Expand();
            Thread.Sleep(250);
            return;
        }
        Invoke(item);
        Thread.Sleep(250);
    }

    public AutomationElement? FindInPopups(string name, ControlType type)
    {
        var cond = new AndCondition(new PropertyCondition(AutomationElement.NameProperty, name),
            new PropertyCondition(AutomationElement.ControlTypeProperty, type));
        foreach (AutomationElement top in AutomationElement.RootElement.FindAll(TreeScope.Children,
                     new PropertyCondition(AutomationElement.ProcessIdProperty, _process!.Id)))
        {
            if (top.FindFirst(TreeScope.Descendants, cond) is { } hit) return hit;
        }
        return null;
    }

    /// <summary>A modal window of the app (input dialog, message box), if one is open.</summary>
    public AutomationElement? OwnedDialog()
    {
        if (_process == null) return null;
        foreach (AutomationElement top in AutomationElement.RootElement.FindAll(TreeScope.Children,
                     new PropertyCondition(AutomationElement.ProcessIdProperty, _process.Id)))
        {
            if (top.Current.NativeWindowHandle != (int)Hwnd && top.Current.ControlType == ControlType.Window &&
                !top.Current.IsOffscreen && top.Current.ClassName != "Popup")
                return top;
            // A message box can also appear as a child of the main window.
            if (top.Current.NativeWindowHandle == (int)Hwnd &&
                top.FindFirst(TreeScope.Children, new PropertyCondition(AutomationElement.ControlTypeProperty, ControlType.Window)) is { } child)
                return child;
        }
        return null;
    }

    public AutomationElement Dialog(string because = "диалог не появился") => Wait.For(OwnedDialog, 5000, because);

    public void PressDialogButton(AutomationElement dialog, string name)
    {
        var b = Wait.For(() => dialog.FindFirst(TreeScope.Descendants, new AndCondition(
            new PropertyCondition(AutomationElement.ControlTypeProperty, ControlType.Button),
            new PropertyCondition(AutomationElement.NameProperty, name))), 3000, $"нет кнопки «{name}»");
        Invoke(b);
        Thread.Sleep(300);
    }

    public string EditorText(AutomationElement scope)
    {
        var edit = Wait.For(() => scope.FindFirst(TreeScope.Descendants,
            new PropertyCondition(AutomationElement.ControlTypeProperty, ControlType.Edit)), 5000, "нет поля текста");
        return ((ValuePattern)edit.GetCurrentPattern(ValuePattern.Pattern)).Current.Value;
    }

    /// <summary>The colour on screen at a point (R, G, B).</summary>
    public static (int R, int G, int B) PixelAt(Point p)
    {
        var dc = GetDC(IntPtr.Zero);
        try
        {
            var c = GetPixel(dc, (int)p.X, (int)p.Y);
            return ((int)(c & 0xFF), (int)((c >> 8) & 0xFF), (int)((c >> 16) & 0xFF));
        }
        finally { ReleaseDC(IntPtr.Zero, dc); }
    }

    [DllImport("user32.dll")] static extern IntPtr GetDC(IntPtr hwnd);
    [DllImport("user32.dll")] static extern int ReleaseDC(IntPtr hwnd, IntPtr dc);
    [DllImport("gdi32.dll")] static extern uint GetPixel(IntPtr dc, int x, int y);

    // ---------- Win32 ----------

    [DllImport("user32.dll")] static extern bool SetForegroundWindow(IntPtr h);
    [DllImport("user32.dll")] static extern IntPtr GetForegroundWindow();
    [DllImport("user32.dll")] static extern bool PostMessage(IntPtr h, uint msg, IntPtr w, IntPtr l);
    [DllImport("user32.dll")] static extern void keybd_event(byte vk, byte scan, uint flags, IntPtr extra);
}

static class Wait
{
    public static void Until(Func<bool> condition, int timeoutMs = 5000, string? because = null)
    {
        var end = DateTime.UtcNow.AddMilliseconds(timeoutMs);
        Exception? last = null;
        while (true)
        {
            try
            {
                if (condition()) return;
            }
            catch (Exception ex) when (ex is ElementNotAvailableException or InvalidOperationException or IOException or COMException)
            {
                last = ex;
            }
            if (DateTime.UtcNow > end)
                throw new Xunit.Sdk.XunitException("Не дождались: " + (because ?? "условие") + (last != null ? " (" + last.Message + ")" : ""));
            Thread.Sleep(50);
        }
    }

    public static T For<T>(Func<T?> get, int timeoutMs = 5000, string? because = null) where T : class
    {
        T? value = null;
        Until(() => (value = get()) != null, timeoutMs, because);
        return value!;
    }

    /// <summary>Asserts that a condition stays false for a while (e.g. no error dialog pops up).</summary>
    public static void Never(Func<bool> condition, int forMs, string because)
    {
        var end = DateTime.UtcNow.AddMilliseconds(forMs);
        while (DateTime.UtcNow < end)
        {
            try
            {
                if (condition()) throw new Xunit.Sdk.XunitException(because);
            }
            catch (ElementNotAvailableException) { }
            Thread.Sleep(50);
        }
    }
}

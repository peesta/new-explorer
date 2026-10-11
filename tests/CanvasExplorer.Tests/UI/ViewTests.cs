using System.IO;
using System.Windows;
using System.Windows.Automation;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using CanvasExplorer.Tests.Infrastructure;
using static CanvasExplorer.Tests.Infrastructure.Input;

namespace CanvasExplorer.Tests.UI;

/// <summary>List/grid per folder, pictures in the tiles, the player's volume.</summary>
[Collection("UI"), Trait("Category", "UI")]
public class ViewTests : IDisposable
{
    readonly AppSession _app = new();

    public void Dispose() => _app.Dispose();

    bool IsOn(string id) => ((SelectionItemPattern)_app.Get(id).GetCurrentPattern(SelectionItemPattern.Pattern)).Current.IsSelected;

    void WritePng(string relative, Color color)
    {
        var path = Path.Combine(_app.Work, relative);
        Sta.Run(() =>
        {
            var pixels = new byte[160 * 100 * 4];
            for (var i = 0; i < pixels.Length; i += 4) { pixels[i] = color.B; pixels[i + 1] = color.G; pixels[i + 2] = color.R; pixels[i + 3] = 255; }
            var bmp = BitmapSource.Create(160, 100, 96, 96, PixelFormats.Bgra32, null, pixels, 160 * 4);
            var enc = new PngBitmapEncoder();
            enc.Frames.Add(BitmapFrame.Create(bmp));
            using var fs = File.Create(path);
            enc.Save(fs);
        });
    }

    [Fact]
    public void Grid_or_list_is_remembered_for_each_folder_separately()
    {
        _app.MakeDir("Другая");
        _app.WriteFile(@"Другая\x.txt");
        _app.WriteFile("a.txt");
        _app.Launch();
        Assert.True(IsOn("ViewList"));

        _app.Click(_app.Get("ViewGrid"));
        Wait.Until(() => IsOn("ViewGrid"), because: "сетка включена");

        _app.DoubleClick(_app.Row("Другая"));
        Wait.Until(() => _app.Title == "Другая");
        Wait.Until(() => IsOn("ViewList"), because: "другая папка осталась списком");

        Keys(Alt, Left);
        Wait.Until(() => _app.Title == "Папка");
        Wait.Until(() => IsOn("ViewGrid"), because: "первая папка снова сеткой");

        _app.Close();
        Assert.Contains(_app.Settings().GetProperty("GridFolders").EnumerateArray(),
            f => string.Equals(f.GetString(), _app.Work, StringComparison.OrdinalIgnoreCase));
        _app.Launch();
        Wait.Until(() => IsOn("ViewGrid"), because: "после перезапуска папка открылась сеткой");
    }

    [Fact]
    public void Tiles_show_pictures_of_images_and_chips_for_other_files()
    {
        WritePng("закат.png", Colors.OrangeRed);
        WritePng("море.png", Colors.SteelBlue);
        _app.WriteFile("заметка.txt", "текст");
        _app.Launch();
        _app.Click(_app.Get("ViewGrid"));

        Wait.Until(() => _app.List.FindAll(TreeScope.Descendants,
            new PropertyCondition(AutomationElement.AutomationIdProperty, "Thumb")).Count == 2, 8000, "у двух картинок есть превью");
        var text = _app.Row("заметка.txt");
        Assert.Null(text.FindFirst(TreeScope.Descendants, new PropertyCondition(AutomationElement.AutomationIdProperty, "Thumb")));
        var thumb = _app.Row("закат.png").FindFirst(TreeScope.Descendants, new PropertyCondition(AutomationElement.AutomationIdProperty, "Thumb"));
        Assert.NotNull(thumb);
        Assert.True(AppSession.Bounds(thumb).Width > 40, "превью видно в плитке");
    }

    [Fact]
    public void Ctrl_wheel_resizes_the_tiles_and_not_the_canvas()
    {
        WritePng("фото.png", Colors.SeaGreen);
        for (var i = 0; i < 6; i++) _app.WriteFile($"файл {i}.txt");
        _app.Launch();
        _app.Click(_app.Get("ViewGrid"));
        Wait.Until(() => IsOn("ViewGrid"));
        var w0 = AppSession.Bounds(_app.Row("фото.png")).Width;

        var over = AppSession.Center(_app.Row("файл 3.txt"));
        With(Ctrl, () => Input.Wheel(over, 3));
        Wait.Until(() => AppSession.Bounds(_app.Row("фото.png")).Width > w0 * 1.35, because: "плитки стали крупнее");
        Assert.False(File.Exists(_app.LayoutPath()), "холст не создан и не масштабирован");

        With(Ctrl, () => Input.Wheel(over, -6));
        Wait.Until(() => AppSession.Bounds(_app.Row("фото.png")).Width < w0 * 0.85, because: "плитки стали мельче");
        Wait.Until(() => _app.Settings().GetProperty("TileScale").GetDouble() < 0.9, because: "размер плиток запомнен");

        // The picture follows the tile size.
        var thumb = Wait.For(() => _app.Row("фото.png").FindFirst(TreeScope.Descendants,
            new PropertyCondition(AutomationElement.AutomationIdProperty, "Thumb")), 5000, "превью");
        Assert.True(AppSession.Bounds(thumb).Width <= AppSession.Bounds(_app.Row("фото.png")).Width);
    }

    [Fact]
    public void List_scroll_bar_is_close_to_the_edge_and_the_edge_still_resizes()
    {
        for (var i = 0; i < 80; i++) _app.WriteFile($"файл {i:00}.txt");
        _app.Launch();
        var frame = AppSession.Bounds(_app.Get("ListWindow"));
        var list = AppSession.Bounds(_app.List);
        Input.MoveTo(new Point(frame.X + 40, frame.Y + 40)); // not hovering the thumb (it changes colour)
        Thread.Sleep(300);
        // Find the scroll thumb (#3A3A3A) on screen, walking left from the window's right edge.
        var dpi = GetDpiForWindow(_app.Hwnd) / 96.0;
        var row = (int)(list.Y + 30 * dpi);
        int? thumbRight = null;
        var dc = GetDC(IntPtr.Zero);
        try
        {
            for (var x = (int)frame.Right - 1; x > frame.Right - 30 * dpi; x--)
            {
                var c = GetPixel(dc, x, row);
                int r = (int)(c & 0xFF), g = (int)((c >> 8) & 0xFF), b = (int)((c >> 16) & 0xFF);
                if (Math.Abs(r - 0x3A) <= 3 && Math.Abs(g - 0x3A) <= 3 && Math.Abs(b - 0x3A) <= 3) { thumbRight = x; break; }
            }
        }
        finally { ReleaseDC(IntPtr.Zero, dc); }
        Assert.NotNull(thumbRight);
        var gap = (frame.Right - thumbRight!.Value) / dpi;
        // Was 9–14 px from the edge; now right after the 4 px resize zone.
        Assert.InRange(gap, 4, 7);

        // The outermost pixels still belong to the window edge: pulling it inward makes a canvas.
        var y = frame.Y + frame.Height / 2;
        Input.Drag(Button.Left, 100, new Point(frame.Right - 2, y), new Point(frame.Right - 300, y));
        Wait.Until(() => _app.Layout() != null, because: "правый край списка по-прежнему меняет размер");
    }

    [Fact]
    public void Audio_player_starts_at_the_last_volume_set_in_any_player()
    {
        File.Copy(Path.Combine(RepoRoot(), "samples", "Пример", "Голосовая.wav"), Path.Combine(_app.Work, "голос.wav"));
        File.Copy(Path.Combine(RepoRoot(), "samples", "Пример", "Голосовая.wav"), Path.Combine(_app.Work, "второй.wav"));
        _app.Launch();
        _app.ClickRow("голос.wav", Button.Right);
        _app.MenuItem("Закрепить на холсте");
        var volume = Wait.For(() => _app.Pinned("голос.wav").FindFirst(TreeScope.Descendants,
            new PropertyCondition(AutomationElement.AutomationIdProperty, "Volume")), 5000, "регулятор громкости");
        Assert.Equal(80, Range(volume));

        // Set 25 % with the mouse.
        var r = AppSession.Bounds(volume);
        Input.Click(new Point(r.X + r.Width * 0.25, r.Y + r.Height / 2));
        Wait.Until(() => Math.Abs(Range(volume) - 25) <= 3, because: "громкость 25 %");
        Wait.Until(() => Math.Abs(_app.Settings().GetProperty("AudioVolume").GetDouble() - 0.25) < 0.04, because: "громкость сохранена");

        // Another file dropped on the canvas starts at that level, not at the default.
        _app.ClickRow("второй.wav", Button.Right);
        _app.MenuItem("Закрепить на холсте");
        var second = Wait.For(() => _app.Pinned("второй.wav").FindFirst(TreeScope.Descendants,
            new PropertyCondition(AutomationElement.AutomationIdProperty, "Volume")), 5000, "регулятор второго плеера");
        Assert.InRange(Range(second), 22, 28);

        // And after a restart too.
        _app.Close();
        _app.Launch();
        var again = Wait.For(() => _app.Pinned("голос.wav").FindFirst(TreeScope.Descendants,
            new PropertyCondition(AutomationElement.AutomationIdProperty, "Volume")), 5000, "регулятор после перезапуска");
        Assert.InRange(Range(again), 22, 28);
    }

    [System.Runtime.InteropServices.DllImport("user32.dll")]
    static extern uint GetDpiForWindow(IntPtr hwnd);
    [System.Runtime.InteropServices.DllImport("user32.dll")]
    static extern IntPtr GetDC(IntPtr hwnd);
    [System.Runtime.InteropServices.DllImport("user32.dll")]
    static extern int ReleaseDC(IntPtr hwnd, IntPtr dc);
    [System.Runtime.InteropServices.DllImport("gdi32.dll")]
    static extern uint GetPixel(IntPtr dc, int x, int y);

    static double Range(AutomationElement e) => ((RangeValuePattern)e.GetCurrentPattern(RangeValuePattern.Pattern)).Current.Value;

    static string RepoRoot()
    {
        for (var d = new DirectoryInfo(AppContext.BaseDirectory); d != null; d = d.Parent)
            if (Directory.Exists(Path.Combine(d.FullName, "samples"))) return d.FullName;
        throw new DirectoryNotFoundException("samples");
    }
}

using System.IO;
using System.Windows;
using System.Windows.Automation;
using CanvasExplorer.Models;
using CanvasExplorer.Tests.Infrastructure;
using static CanvasExplorer.Tests.Infrastructure.Input;

namespace CanvasExplorer.Tests.UI;

/// <summary>The canvas: creating it, pinning windows, the keyboard on pinned windows, persistence.</summary>
[Collection("UI"), Trait("Category", "UI")]
public class CanvasTests : IDisposable
{
    readonly AppSession _app = new();

    public void Dispose() => _app.Dispose();

    /// <summary>A canvas with the list at the left and the given files pinned to its right.</summary>
    void SeedCanvas(params string[] pinned)
    {
        var layout = new FolderLayout { List = new RectState { X = 0, Y = 0, W = 380, H = 520 } };
        var x = 420;
        foreach (var name in pinned)
        {
            layout.Items.Add(new PinState { Name = name, X = x, Y = 20, W = 300, H = 240 });
            x += 330;
        }
        _app.WriteLayout(layout);
    }

    static Point HeaderOf(AutomationElement window)
    {
        var r = AppSession.Bounds(window);
        // On the title text, clear of the extension chip and the header buttons.
        return new Point(r.X + r.Width * 0.45, r.Y + 14);
    }

    static Point BodyOf(AutomationElement window)
    {
        var r = AppSession.Bounds(window);
        return new Point(r.X + r.Width / 2, r.Y + r.Height * 0.6);
    }

    /// <summary>A point on empty canvas right of everything.</summary>
    Point EmptyCanvas()
    {
        var c = AppSession.Bounds(_app.Get("CanvasArea"));
        return new Point(c.Right - 60, c.Y + c.Height / 2);
    }

    [Fact]
    public void Canvas_is_created_from_the_folder_menu_and_removed_from_the_canvas_menu()
    {
        _app.WriteFile("a.txt");
        _app.Launch();
        Assert.False(File.Exists(_app.LayoutPath()));

        var list = AppSession.Bounds(_app.List);
        Input.Click(new Point(list.X + list.Width / 2, list.Bottom - 30), Button.Right);
        _app.MenuItem("Создать холст");
        Wait.Until(() => _app.Layout() != null, because: "файл раскладки создан");
        Wait.Until(() => AppSession.Bounds(_app.List).Width < list.Width - 100, because: "список стал окном на холсте");
        Assert.True(File.GetAttributes(_app.LayoutPath()).HasFlag(FileAttributes.Hidden));

        Input.Click(EmptyCanvas(), Button.Right);
        _app.MenuItem("Убрать холст");
        Wait.Until(() => !File.Exists(_app.LayoutPath()), because: "холст убран");
        Wait.Until(() => AppSession.Bounds(_app.List).Width > list.Width - 20, because: "список снова на всю ширину");
    }

    [Fact]
    public void Dragging_the_list_edge_inward_creates_the_canvas()
    {
        _app.WriteFile("a.txt");
        _app.Launch();
        var w = AppSession.Bounds(_app.Get("ListWindow"));
        Input.Drag(Button.Left, 100, new Point(w.X + 2, w.Y + w.Height / 2), new Point(w.X + 320, w.Y + w.Height / 2));
        Wait.Until(() => _app.Layout() != null, because: "холст создан перетаскиванием края");
    }

    [Fact]
    public void Pin_from_the_file_menu_shows_a_window_and_counts_it()
    {
        _app.WriteFile("заметки.txt", "привет");
        _app.WriteFile("другое.txt");
        _app.Launch();
        _app.ClickRow("заметки.txt", Button.Right);
        _app.MenuItem("Закрепить на холсте");

        var pin = _app.Pinned("заметки.txt");
        Wait.Until(() => _app.EditorText(pin) == "привет", because: "окно показывает текст файла");
        Wait.Until(() => _app.Layout()?.Items.Any(i => i.Name == "заметки.txt") == true, because: "закрепление сохранено");
        Wait.Until(() => _app.CountText.Contains("1 на холсте"), because: "счётчик закреплённых");

        // The new window lies inside the visible canvas.
        var canvas = AppSession.Bounds(_app.Get("CanvasArea"));
        var r = AppSession.Bounds(pin);
        Assert.True(canvas.Contains(r.TopLeft) && canvas.Contains(r.BottomRight), $"окно {r} вне холста {canvas}");
    }

    [Fact]
    public void Delete_on_a_selected_window_unpins_it_and_never_deletes_the_file()
    {
        _app.WriteFile("важное.txt", "не удалять");
        SeedCanvas("важное.txt");
        _app.Launch();
        var pin = _app.Pinned("важное.txt");

        Input.Click(HeaderOf(pin));
        Keys(Delete);
        Wait.Until(() => _app.PinnedWindows().Count == 0, because: "окно откреплено");
        Wait.Until(() => _app.Layout()?.Items.Count == 0, because: "раскладка сохранена без окна");
        Assert.Equal("не удалять", File.ReadAllText(Path.Combine(_app.Work, "важное.txt")));
    }

    [Fact]
    public void Delete_after_clicking_into_the_list_does_not_unpin_the_previously_selected_window()
    {
        _app.WriteFile("окно.txt", "x");
        SeedCanvas("окно.txt");
        _app.Launch();
        var pin = _app.Pinned("окно.txt");
        Input.Click(HeaderOf(pin));

        // Click empty space inside the list (nothing selected there), then press Delete.
        var list = AppSession.Bounds(_app.List);
        Input.Click(new Point(list.X + list.Width / 2, list.Bottom - 20));
        Keys(Delete);
        Thread.Sleep(800);
        Assert.Single(_app.PinnedWindows());
        Assert.True(File.Exists(Path.Combine(_app.Work, "окно.txt")));
    }

    [Fact]
    public void Pins_of_missing_files_are_kept_and_return_when_the_file_does()
    {
        _app.WriteFile("есть.txt", "1");
        _app.WriteFile("ещё.txt", "2");
        SeedCanvas("есть.txt", "пропал.txt");
        _app.Launch();
        _app.Pinned("есть.txt");
        Assert.Single(_app.PinnedWindows());
        // Only real files are counted.
        Wait.Until(() => _app.CountText.Contains("1 на холсте"), because: "отсутствующий файл не считается");

        // Any change saves the layout; the missing pin must survive it.
        _app.ClickRow("ещё.txt", Button.Right);
        _app.MenuItem("Закрепить на холсте");
        Wait.Until(() => _app.Layout()?.Items.Count == 3, because: "раскладка сохранена");
        Assert.Contains(_app.Layout()!.Items, i => i.Name == "пропал.txt" && i.X == 750);

        _app.WriteFile("пропал.txt", "вернулся");
        _app.Pinned("пропал.txt");
        Wait.Until(() => _app.CountText.Contains("3 на холсте"), because: "вернувшийся файл снова на холсте");
    }

    [Fact]
    public void Pinned_text_window_shows_changes_made_by_another_program()
    {
        _app.WriteFile("журнал.txt", "версия 1");
        SeedCanvas("журнал.txt");
        _app.Launch();
        var pin = _app.Pinned("журнал.txt");
        Wait.Until(() => _app.EditorText(pin) == "версия 1");

        File.WriteAllText(Path.Combine(_app.Work, "журнал.txt"), "версия 2");
        Wait.Until(() => _app.EditorText(pin) == "версия 2", 4000, "окно перечитало файл");
    }

    [Fact]
    public void Unsaved_edits_never_silently_overwrite_another_programs_changes()
    {
        var path = _app.WriteFile("общий.txt", "исходный");
        SeedCanvas("общий.txt");
        _app.Launch();
        var pin = _app.Pinned("общий.txt");
        Wait.Until(() => _app.EditorText(pin) == "исходный");

        Input.Click(BodyOf(pin));
        Keys(Ctrl, End);
        Type(" + мои правки");
        File.WriteAllText(path, "чужая версия");

        // The app asks; "Нет" drops the edits and shows the other program's version.
        var dialog = _app.Dialog("вопрос о конфликте правок");
        Assert.Equal("Файл изменён", dialog.Current.Name);
        _app.PressDialogButton(dialog, "Нет");
        Wait.Until(() => _app.EditorText(pin) == "чужая версия", 4000, "окно показало версию с диска");
        Thread.Sleep(1200);
        Assert.Equal("чужая версия", File.ReadAllText(path));
    }

    [Fact]
    public void Keeping_own_edits_in_a_conflict_writes_them()
    {
        var path = _app.WriteFile("общий.txt", "исходный");
        SeedCanvas("общий.txt");
        _app.Launch();
        var pin = _app.Pinned("общий.txt");
        Wait.Until(() => _app.EditorText(pin) == "исходный");

        Input.Click(BodyOf(pin));
        Keys(Ctrl, End);
        Type("!");
        File.WriteAllText(path, "чужая версия");
        var dialog = _app.Dialog("вопрос о конфликте правок");
        _app.PressDialogButton(dialog, "Да");
        Wait.Until(() => File.ReadAllText(path) == "исходный!", 4000, "сохранены правки пользователя");
    }

    [Fact]
    public void Edits_in_a_pinned_text_window_are_saved_automatically()
    {
        var path = _app.WriteFile("список.txt", "молоко");
        SeedCanvas("список.txt");
        _app.Launch();
        var pin = _app.Pinned("список.txt");
        Wait.Until(() => _app.EditorText(pin) == "молоко");
        Input.Click(BodyOf(pin));
        Keys(Ctrl, End);
        Type(", хлеб");
        Wait.Until(() => File.ReadAllText(path) == "молоко, хлеб", 4000, "автосохранение");
        Wait.Never(() => _app.OwnedDialog() != null, 1000, "собственное сохранение приняли за чужое изменение");
    }

    [Fact]
    public void Renaming_a_pinned_file_elsewhere_renames_its_window()
    {
        _app.WriteFile("старое имя.txt", "x");
        SeedCanvas("старое имя.txt");
        _app.Launch();
        _app.Pinned("старое имя.txt");
        File.Move(Path.Combine(_app.Work, "старое имя.txt"), Path.Combine(_app.Work, "новое имя.txt"));
        _app.Pinned("новое имя.txt");
        Wait.Until(() => _app.Layout()?.Items.SingleOrDefault()?.Name == "новое имя.txt", because: "раскладка с новым именем");
    }

    [Fact]
    public void Middle_click_on_a_folder_opens_it_in_a_new_tab_on_a_canvas_too()
    {
        _app.MakeDir("Вложенная");
        _app.WriteFile("a.txt");
        SeedCanvas();
        _app.Launch();
        _app.Row("Вложенная");

        var r = AppSession.Bounds(_app.Row("Вложенная"));
        Input.Click(new Point(r.X + 48, r.Y + r.Height / 2), Button.Middle);
        Wait.Until(() => _app.Title == "Вложенная", because: "средняя кнопка открыла папку во вкладке");
        _app.Close();
        Assert.Equal(2, _app.Settings().GetProperty("Tabs").GetArrayLength());
    }

    [Fact]
    public void List_footer_buttons_in_the_bottom_right_corner_stay_clickable()
    {
        _app.WriteFile("a.png");
        _app.Launch();
        // Pull the left edge inward: the list now ends in the bottom-right corner of the canvas,
        // where the hidden zoom panel lives.
        var w = AppSession.Bounds(_app.Get("ListWindow"));
        Input.Drag(Button.Left, 100, new Point(w.X + 2, w.Y + w.Height / 2), new Point(w.X + 360, w.Y + w.Height / 2));
        Wait.Until(() => _app.Layout() != null, because: "холст создан");

        var grid = _app.Get("ViewGrid");
        _app.Click(grid);
        Wait.Until(() => ((SelectionItemPattern)grid.GetCurrentPattern(SelectionItemPattern.Pattern)).Current.IsSelected,
            3000, "кнопка «Сетка» в правом нижнем углу нажимается");
    }

    [Fact]
    public void Zoom_panel_appears_in_the_empty_corner_and_zooms()
    {
        _app.WriteFile("a.txt");
        SeedCanvas();
        _app.Launch();
        var canvas = AppSession.Bounds(_app.Get("CanvasArea"));
        // Approach the corner over empty canvas, then use the panel that shows up there.
        Input.MoveTo(new Point(canvas.Right - 300, canvas.Bottom - 30));
        Input.MoveTo(new Point(canvas.Right - 120, canvas.Bottom - 25));
        Thread.Sleep(300);
        _app.Click(_app.Get("ZoomInBtn"));
        Wait.Until(() => _app.Get("ZoomText").Current.Name == "120%", because: "масштаб холста увеличен кнопкой в углу");
        Keys(Ctrl, Letter('0'));
        Wait.Until(() => _app.Get("ZoomText").Current.Name == "100%", because: "Ctrl+0 вернул масштаб");
    }

    [Fact]
    public void Canvas_survives_a_restart_with_window_positions()
    {
        _app.WriteFile("фото.txt", "x");
        SeedCanvas("фото.txt");
        _app.Launch();
        var before = AppSession.Bounds(_app.Pinned("фото.txt"));

        // Move the window by its header and check the move is saved.
        var from = HeaderOf(_app.Pinned("фото.txt"));
        Input.Drag(Button.Left, 50, from, new Point(from.X + 60, from.Y + 40));
        Wait.Until(() => _app.Layout()?.Items.Single().Y > 20, because: "перенос окна сохранён");
        _app.Close();

        _app.Launch();
        var after = AppSession.Bounds(_app.Pinned("фото.txt"));
        Assert.InRange(after.Y - before.Y, 25, 55);
    }
}

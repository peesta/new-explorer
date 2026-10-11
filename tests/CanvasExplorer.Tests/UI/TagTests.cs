using System.IO;
using System.Windows;
using System.Windows.Automation;
using CanvasExplorer.Tests.Infrastructure;
using static CanvasExplorer.Tests.Infrastructure.Input;

namespace CanvasExplorer.Tests.UI;

/// <summary>Tags: creating (sidebar button, sidebar menu, file menu), assigning, the tag view, deleting.</summary>
[Collection("UI"), Trait("Category", "UI")]
public class TagTests : IDisposable
{
    readonly AppSession _app = new();

    public void Dispose() => _app.Dispose();

    AutomationElement DialogPart(AutomationElement dialog, string name, ControlType type) =>
        Wait.For(() => dialog.FindFirst(TreeScope.Descendants, new AndCondition(
            new PropertyCondition(AutomationElement.NameProperty, name),
            new PropertyCondition(AutomationElement.ControlTypeProperty, type))), 3000, $"в окне тега нет «{name}»");

    static void Select(AutomationElement e) => ((SelectionItemPattern)e.GetCurrentPattern(SelectionItemPattern.Pattern)).Select();

    [Fact]
    public void New_tag_button_after_the_drives_creates_a_colour_tag()
    {
        _app.Launch();
        _app.Click(_app.Get("NewTagBtn"));
        var dialog = _app.Dialog("окно «Новый тег»");
        Assert.Equal("Новый тег", dialog.Current.Name);
        Type("Срочно");
        // Pick the colour with the real mouse, like a person.
        _app.Click(DialogPart(dialog, "Красный", ControlType.RadioButton));
        _app.PressDialogButton(dialog, "Создать");

        _app.SideItem("Срочно");
        var tag = _app.Tags().GetProperty("tags").EnumerateArray().Single();
        Assert.Equal("Срочно", tag.GetProperty("name").GetString());
        Assert.Equal("#E5484D", tag.GetProperty("color").GetString());
        Assert.Equal("0", _app.SideCount("Срочно"));
    }

    [Fact]
    public void Sidebar_menu_creates_an_icon_tag()
    {
        _app.Launch();
        // Right click on empty space at the bottom of the left panel.

        Input.Click(_app.SidebarEmptySpot(), Button.Right);
        _app.MenuItem("Новый тег…");
        var dialog = _app.Dialog("окно «Новый тег»");
        Type("Идеи");
        Select(DialogPart(dialog, "Иконка", ControlType.RadioButton));
        Select(DialogPart(dialog, "Идея", ControlType.RadioButton));
        _app.PressDialogButton(dialog, "Создать");

        _app.SideItem("Идеи");
        var tag = _app.Tags().GetProperty("tags").EnumerateArray().Single();
        Assert.Equal("bulb", tag.GetProperty("icon").GetString());
    }

    [Fact]
    public void Empty_or_duplicate_name_is_refused_inside_the_popup()
    {
        _app.SeedTags(("t1", "Есть", "#E5484D", null));
        _app.Launch();
        _app.Click(_app.Get("NewTagBtn"));
        var dialog = _app.Dialog();
        _app.PressDialogButton(dialog, "Создать");
        Wait.For(() => _app.Find("TagError", dialog), 3000, "ошибка «Введите название»");
        Type("есть");
        _app.PressDialogButton(dialog, "Создать");
        Wait.Until(() => _app.Find("TagError", dialog)?.Current.Name.Contains("уже есть") == true, because: "ошибка «уже есть»");
        Keys(Esc);
        Wait.Until(() => _app.OwnedDialog() == null, because: "Esc закрывает окно тега");
        Assert.Single(_app.Tags().GetProperty("tags").EnumerateArray());
    }

    [Fact]
    public void File_menu_assigns_a_tag_and_the_tag_view_lists_the_file()
    {
        _app.SeedTags(("t1", "Срочно", "#E5484D", null), ("t2", "Идеи", null, "bulb"));
        var file = _app.WriteFile("смета.xlsx");
        _app.WriteFile("другое.txt");
        _app.Launch();

        _app.ClickRow("смета.xlsx", Button.Right);
        _app.MenuItem("Теги");
        _app.MenuItem("Срочно");
        Wait.Until(() => _app.TagIdsOf(file).SequenceEqual(new[] { "t1" }), because: "тег назначен");
        Wait.Until(() => _app.SideCount("Срочно") == "1", because: "счётчик тега");

        // The tag in the sidebar shows every file that carries it.
        _app.Click(_app.SideItem("Срочно"));
        Wait.Until(() => _app.Title == "Срочно", because: "открыт просмотр тега");
        Wait.Until(() => _app.Names().SequenceEqual(new[] { "смета.xlsx" }), because: "в просмотре только файл с тегом");

        // Taking the tag off in the tag view removes the file from it.
        _app.ClickRow("смета.xlsx", Button.Right);
        _app.MenuItem("Теги");
        _app.MenuItem("Срочно");
        Wait.Until(() => _app.Names().Count == 0, because: "файл ушёл из просмотра тега");
        Assert.Contains("Нет файлов", _app.Message);

        Keys(Alt, Left);
        Wait.Until(() => _app.Title == "Папка", because: "«Назад» из просмотра тега");
    }

    [Fact]
    public void Tag_view_shows_files_from_other_folders_and_leads_back_to_them()
    {
        _app.SeedTags(("t1", "Отчёты", "#0099BC", null));
        _app.MakeDir("Глубже");
        _app.WriteFile(@"Глубже\отчёт.docx");
        _app.Launch(Path.Combine(_app.Work, "Глубже"));
        _app.ClickRow("отчёт.docx", Button.Right);
        _app.MenuItem("Теги");
        _app.MenuItem("Отчёты");
        _app.Close();
        _app.Launch(); // start again in the parent folder: the tag still knows the file
        _app.Click(_app.SideItem("Отчёты"));
        Wait.Until(() => _app.Names().SequenceEqual(new[] { "отчёт.docx" }), because: "файл из другой папки в просмотре тега");

        _app.ClickRow("отчёт.docx", Button.Right);
        _app.MenuItem("Показать в папке");
        Wait.Until(() => _app.Title == "Глубже", because: "открыта папка файла");
        Wait.Until(() => _app.Selected().SequenceEqual(new[] { "отчёт.docx" }), because: "файл выделен в своей папке");
    }

    [Fact]
    public void New_tag_from_the_file_menu_goes_onto_the_file()
    {
        var file = _app.WriteFile("план.pdf");
        _app.Launch();
        _app.ClickRow("план.pdf", Button.Right);
        _app.MenuItem("Теги");
        _app.MenuItem("Новый тег…");
        var dialog = _app.Dialog("окно «Новый тег»");
        Type("Готово");
        _app.PressDialogButton(dialog, "Создать");
        Wait.Until(() => _app.TagIdsOf(file).Count == 1, because: "новый тег сразу на файле");
        Wait.Until(() => _app.SideCount("Готово") == "1");
    }

    [Fact]
    public void Dropping_files_on_a_tag_in_the_sidebar_tags_them()
    {
        _app.SeedTags(("t1", "Мебель", "#8764B8", null));
        var a = _app.WriteFile("стол.jpg");
        var b = _app.WriteFile("стул.jpg");
        _app.Launch();
        _app.ClickRow("стол.jpg");
        With(Ctrl, () => _app.ClickRow("стул.jpg"));
        // Not where the last click was: a press there within the double-click time would open the file.
        Thread.Sleep(600);
        var from = AppSession.Bounds(_app.Row("стол.jpg"));
        var to = AppSession.Center(_app.SideItem("Мебель"));
        Input.Drag(Button.Left, 300, new Point(from.X + 48, from.Y + from.Height / 2), new Point(from.X - 60, from.Y), to);
        Wait.Until(() => _app.TagIdsOf(a).Count == 1 && _app.TagIdsOf(b).Count == 1, because: "оба файла получили тег");
        Assert.True(File.Exists(a) && File.Exists(b), "файлы никуда не переместились");
    }

    [Fact]
    public void Tag_follows_a_file_renamed_in_the_app()
    {
        _app.SeedTags(("t1", "Срочно", "#E5484D", null));
        var file = _app.WriteFile("черновик.txt");
        _app.Launch();
        _app.ClickRow("черновик.txt", Button.Right);
        _app.MenuItem("Теги");
        _app.MenuItem("Срочно");
        Wait.Until(() => _app.TagIdsOf(file).Count == 1);

        _app.ClickRow("черновик.txt");
        Keys(F2);
        _app.Dialog();
        Keys(Ctrl, Letter('A'));
        Type("итог.txt");
        Keys(Enter);
        var renamed = Path.Combine(_app.Work, "итог.txt");
        Wait.Until(() => _app.TagIdsOf(renamed).Count == 1, because: "тег переехал на новое имя");
        Assert.Empty(_app.TagIdsOf(file));
    }

    [Fact]
    public void Tag_travels_inside_the_file_when_another_program_moves_it()
    {
        _app.SeedTags(("t1", "Срочно", "#E5484D", null));
        var file = _app.WriteFile("счёт.pdf");
        var target = _app.MakeDir("Архив");
        _app.Launch();
        _app.ClickRow("счёт.pdf", Button.Right);
        _app.MenuItem("Теги");
        _app.MenuItem("Срочно");
        Wait.Until(() => File.Exists(file + ":canvas.tags"), because: "тег записан в сам файл");

        // Moved by "Explorer" while the app is closed.
        _app.Close();
        var moved = Path.Combine(target, "счёт.pdf");
        File.Move(file, moved);
        _app.Launch(target);
        Wait.Until(() => _app.TagIdsOf(moved).SequenceEqual(new[] { "t1" }), because: "тег найден на новом месте");
        Wait.Until(() => _app.SideCount("Срочно") == "1", because: "счётчик тега");
        Assert.Empty(_app.TagIdsOf(file));
    }

    [Fact]
    public void Deleting_a_tag_from_its_menu_asks_and_removes_it()
    {
        _app.SeedTags(("t1", "Старый", "#7A7A7A", null));
        var file = _app.WriteFile("a.txt");
        _app.Launch();
        _app.ClickRow("a.txt", Button.Right);
        _app.MenuItem("Теги");
        _app.MenuItem("Старый");
        Wait.Until(() => _app.SideCount("Старый") == "1");

        _app.Click(_app.SideItem("Старый"), Button.Right);
        _app.MenuItem("Удалить тег");
        var confirm = _app.Dialog("вопрос «Удалить тег?»");
        _app.PressDialogButton(confirm, "ОК");
        Wait.Until(() => _app.FindSideItem("Старый") == null, because: "тег исчез из панели");
        Assert.Empty(_app.TagIdsOf(file));
        Assert.True(File.Exists(file));
    }
}

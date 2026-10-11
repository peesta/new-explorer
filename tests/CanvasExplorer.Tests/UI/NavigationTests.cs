using System.IO;
using CanvasExplorer.Tests.Infrastructure;
using static CanvasExplorer.Tests.Infrastructure.Input;

namespace CanvasExplorer.Tests.UI;

/// <summary>Opening folders, history, path bar, search and live updates of the list.</summary>
[Collection("UI"), Trait("Category", "UI")]
public class NavigationTests : IDisposable
{
    readonly AppSession _app = new();

    public void Dispose() => _app.Dispose();

    [Fact]
    public void Folder_from_command_line_lists_folders_first_in_natural_order()
    {
        _app.WriteFile("file10.txt");
        _app.WriteFile("file2.txt");
        _app.WriteFile("Яблоко.md");
        _app.MakeDir("Подпапка");
        _app.Launch();

        Assert.Equal("Папка", _app.Title);
        Wait.Until(() => _app.Names().Count == 4, because: "в списке 4 элемента");
        Assert.Equal(new[] { "Подпапка", "file2.txt", "file10.txt", "Яблоко.md" }, _app.Names());
        Assert.Equal("4 объекта", _app.CountText);
    }

    [Fact]
    public void Empty_folder_says_so()
    {
        _app.Launch();
        Wait.Until(() => _app.Message == "Папка пуста", because: "сообщение «Папка пуста»");
        Assert.Equal("0 объектов", _app.CountText);
    }

    [Fact]
    public void Enter_opens_folder_and_back_returns_with_that_folder_selected()
    {
        _app.MakeDir("Альфа");
        _app.MakeDir("Бета");
        _app.WriteFile(@"Бета\внутри.txt");
        _app.Launch();

        _app.ClickRow("Бета");
        Keys(Enter);
        Wait.Until(() => _app.Title == "Бета", because: "переход в «Бета»");
        Wait.Until(() => _app.Names().SequenceEqual(new[] { "внутри.txt" }));

        Keys(Alt, Left);
        Wait.Until(() => _app.Title == "Папка", because: "возврат назад");
        // Like Explorer: the folder we came back from is selected, so Enter goes straight back in.
        Wait.Until(() => _app.Selected().SequenceEqual(new[] { "Бета" }), because: "после «Назад» выделена «Бета»");

        Keys(Alt, Right);
        Wait.Until(() => _app.Title == "Бета", because: "переход вперёд");

        _app.Click(_app.Get("UpBtn"));
        Wait.Until(() => _app.Title == "Папка", because: "кнопка «Вверх»");
        Wait.Until(() => _app.Selected().SequenceEqual(new[] { "Бета" }), because: "после «Вверх» выделена «Бета»");
    }

    [Fact]
    public void Back_and_forward_buttons_are_enabled_only_when_there_is_history()
    {
        _app.MakeDir("Дальше");
        _app.Launch();
        Assert.False(_app.Get("BackBtn").Current.IsEnabled);
        Assert.False(_app.Get("ForwardBtn").Current.IsEnabled);

        _app.DoubleClick(_app.Row("Дальше"));
        Wait.Until(() => _app.Title == "Дальше");
        Wait.Until(() => _app.Get("BackBtn").Current.IsEnabled, because: "«Назад» доступна");
        Assert.False(_app.Get("ForwardBtn").Current.IsEnabled);

        _app.Click(_app.Get("BackBtn"));
        Wait.Until(() => _app.Get("ForwardBtn").Current.IsEnabled, because: "«Вперёд» доступна");
    }

    [Fact]
    public void Typed_path_navigates_and_bad_path_keeps_the_folder()
    {
        var target = _app.MakeDir(@"Глубоко\Внутри");
        _app.Launch();

        Keys(Ctrl, Letter('L'));
        Thread.Sleep(200);
        Keys(Ctrl, Letter('A'));
        Type(target);
        Keys(Enter);
        Wait.Until(() => _app.Title == "Внутри", because: "переход по введённому пути");

        Keys(Ctrl, Letter('L'));
        Thread.Sleep(200);
        Keys(Ctrl, Letter('A'));
        Type(Path.Combine(_app.Work, "нет такой папки"));
        Keys(Enter);
        Thread.Sleep(600);
        Assert.Equal("Внутри", _app.Title);
        // The path field closed again.
        var edit = _app.Find("PathEdit");
        Assert.True(edit == null || edit.Current.IsOffscreen);
    }

    [Fact]
    public void Search_filters_counts_matches_and_escape_clears()
    {
        _app.WriteFile("отчёт 2024.txt");
        _app.WriteFile("отчёт 2025.txt");
        _app.WriteFile("фото.png");
        _app.Launch();
        Wait.Until(() => _app.Names().Count == 3);

        Keys(Ctrl, Letter('F'));
        Type("ОТЧЁТ");
        Wait.Until(() => _app.Names().Count == 2, because: "фильтр без учёта регистра");
        Assert.Equal("найдено 2 из 3 объектов", _app.CountText);

        Type("9");
        Wait.Until(() => _app.Message == "Ничего не найдено", because: "сообщение «Ничего не найдено»");

        Keys(Esc);
        Wait.Until(() => _app.Names().Count == 3, because: "Esc сбрасывает поиск");
        Assert.Null(_app.Message);
        Assert.Equal("3 объекта", _app.CountText);
    }

    [Fact]
    public void Files_changed_by_other_programs_appear_and_disappear()
    {
        _app.WriteFile("старый.txt");
        _app.Launch();
        Wait.Until(() => _app.Names().Count == 1);

        _app.WriteFile("новый.txt", "x");
        Wait.Until(() => _app.Names().Contains("новый.txt"), because: "новый файл появился");
        File.Delete(Path.Combine(_app.Work, "старый.txt"));
        Wait.Until(() => !_app.Names().Contains("старый.txt"), because: "удалённый файл исчез");
        Assert.Equal("1 объект", _app.CountText);
    }

    [Fact]
    public void Deleted_open_folder_falls_back_to_its_parent()
    {
        var inner = _app.MakeDir("Временная");
        _app.WriteFile(@"Временная\a.txt");
        _app.WriteFile("рядом.txt");
        _app.Launch(inner);
        Wait.Until(() => _app.Names().Count == 1);

        Directory.Delete(inner, true);
        Wait.Until(() => _app.Title == "Папка", 6000, "приложение вернулось в родительскую папку");
        Wait.Until(() => _app.Names().SequenceEqual(new[] { "рядом.txt" }), because: "показано содержимое родительской папки");
        Assert.Null(_app.Message);
    }

    [Fact]
    public void Ctrl_A_selects_everything_and_escape_clears_selection()
    {
        _app.WriteFile("a.txt");
        _app.WriteFile("b.txt");
        _app.WriteFile("c.txt");
        _app.Launch();
        _app.ClickRow("a.txt");
        Keys(Ctrl, Letter('A'));
        Wait.Until(() => _app.Selected().Count == 3, because: "Ctrl+A выделяет всё");
        Assert.Contains("выбрано 3", _app.CountText);
        Keys(Esc);
        Wait.Until(() => _app.Selected().Count == 0, because: "Esc снимает выделение");
    }

    [Fact]
    public void Shift_click_selects_a_range_and_ctrl_click_toggles()
    {
        foreach (var n in new[] { "1.txt", "2.txt", "3.txt", "4.txt" }) _app.WriteFile(n);
        _app.Launch();
        _app.ClickRow("1.txt");
        With(Shift, () => _app.ClickRow("3.txt"));
        Wait.Until(() => _app.Selected().Count == 3, because: "Shift+клик выделяет диапазон");
        With(Ctrl, () => _app.ClickRow("2.txt"));
        Wait.Until(() => _app.Selected().OrderBy(s => s).SequenceEqual(new[] { "1.txt", "3.txt" }), because: "Ctrl+клик снимает один");
    }
}

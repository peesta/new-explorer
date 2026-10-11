using System.IO;
using System.Windows.Automation;
using CanvasExplorer.Tests.Infrastructure;
using static CanvasExplorer.Tests.Infrastructure.Input;

namespace CanvasExplorer.Tests.UI;

/// <summary>Creating, renaming, copying and pasting — always inside the scratch folder.</summary>
[Collection("UI"), Trait("Category", "UI")]
public class FileOperationTests : IDisposable
{
    readonly AppSession _app = new();

    public void Dispose() => _app.Dispose();

    [Fact]
    public void Ctrl_Shift_N_creates_a_folder_with_the_typed_name_and_selects_it()
    {
        _app.WriteFile("a.txt");
        _app.Launch();
        _app.ClickRow("a.txt");

        Keys(Ctrl, Shift, Letter('N'));
        var dialog = _app.Dialog("диалог имени новой папки");
        Keys(Ctrl, Letter('A'));
        Type("Проекты");
        Keys(Enter);

        Wait.Until(() => Directory.Exists(Path.Combine(_app.Work, "Проекты")), because: "папка создана с введённым именем");
        Assert.False(Directory.Exists(Path.Combine(_app.Work, "Новая папка")));
        Wait.Until(() => _app.Selected().SequenceEqual(new[] { "Проекты" }), because: "новая папка выделена");
    }

    [Fact]
    public void Rename_with_bad_characters_keeps_the_dialog_open_with_a_reason()
    {
        _app.WriteFile("черновик.txt", "текст");
        _app.Launch();
        _app.ClickRow("черновик.txt");

        Keys(F2);
        var dialog = _app.Dialog("диалог переименования");
        Keys(Ctrl, Letter('A'));
        Type("плохое:имя.txt");
        Keys(Enter);

        // The dialog must stay, explain the problem, and keep what was typed.
        var error = Wait.For(() => _app.Find("InputError", dialog), 3000, "сообщение об ошибке в диалоге");
        Assert.Contains("не может содержать", error.Current.Name);
        Assert.Equal("плохое:имя.txt", ((ValuePattern)_app.Get("InputBox", dialog).GetCurrentPattern(ValuePattern.Pattern)).Current.Value);

        Keys(Ctrl, Letter('A'));
        Type("итог.txt");
        Keys(Enter);
        Wait.Until(() => File.Exists(Path.Combine(_app.Work, "итог.txt")), because: "файл переименован");
        Assert.Equal("текст", File.ReadAllText(Path.Combine(_app.Work, "итог.txt")));
        Wait.Until(() => _app.Selected().SequenceEqual(new[] { "итог.txt" }), because: "переименованный файл выделен");
    }

    [Fact]
    public void Rename_onto_an_existing_name_is_refused_inside_the_dialog()
    {
        _app.WriteFile("один.txt", "1");
        _app.WriteFile("два.txt", "2");
        _app.Launch();
        _app.ClickRow("один.txt");

        Keys(F2);
        var dialog = _app.Dialog();
        Keys(Ctrl, Letter('A'));
        Type("два.txt");
        Keys(Enter);
        Wait.For(() => _app.Find("InputError", dialog), 3000, "ошибка «уже есть»");
        Keys(Esc);
        Wait.Until(() => _app.OwnedDialog() == null, because: "Esc закрывает диалог");
        Assert.Equal("1", File.ReadAllText(Path.Combine(_app.Work, "один.txt")));
        Assert.Equal("2", File.ReadAllText(Path.Combine(_app.Work, "два.txt")));
    }

    [Fact]
    public void Renaming_a_folder_only_in_letter_case_works()
    {
        _app.MakeDir("документы");
        _app.Launch();
        _app.ClickRow("документы");
        Keys(F2);
        _app.Dialog();
        Keys(Ctrl, Letter('A'));
        Type("Документы");
        Keys(Enter);
        Wait.Until(() => new DirectoryInfo(_app.Work).GetDirectories().Select(d => d.Name).SequenceEqual(new[] { "Документы" }),
            because: "папка переименована с заглавной буквы");
        Wait.Never(() => _app.OwnedDialog() != null, 800, "появилось сообщение об ошибке");
    }

    [Fact]
    public void Cut_and_paste_into_the_same_folder_does_nothing_and_shows_no_error()
    {
        _app.WriteFile("файл.txt", "данные");
        _app.Launch();
        _app.ClickRow("файл.txt");
        Keys(Ctrl, Letter('X'));
        Keys(Ctrl, Letter('V'));

        Wait.Never(() => _app.OwnedDialog() != null, 1500, "вставка в ту же папку показала окно ошибки");
        Assert.Equal("данные", File.ReadAllText(Path.Combine(_app.Work, "файл.txt")));
        Assert.Single(Directory.GetFiles(_app.Work));
    }

    [Fact]
    public void Copy_and_paste_into_the_same_folder_makes_a_copy_and_keeps_the_original()
    {
        _app.WriteFile("смета.txt", "100");
        _app.Launch();
        _app.ClickRow("смета.txt");
        Keys(Ctrl, Letter('C'));
        Keys(Ctrl, Letter('V'));
        Wait.Until(() => Directory.GetFiles(_app.Work).Length == 2, 8000, "появилась копия");
        Assert.Equal("100", File.ReadAllText(Path.Combine(_app.Work, "смета.txt")));
        Wait.Until(() => _app.Names().Count == 2, because: "копия видна в списке");
    }

    [Fact]
    public void New_text_file_from_the_folder_menu_gets_the_typed_name()
    {
        _app.WriteFile("есть.txt");
        _app.Launch();
        // Right click on empty space under the rows opens the folder menu.
        var list = AppSession.Bounds(_app.List);
        Input.Click(new System.Windows.Point(list.X + list.Width / 2, list.Bottom - 30), Button.Right);
        _app.MenuItem("Создать");
        _app.MenuItem("Текстовый документ");
        _app.Dialog("диалог имени файла");
        // The base name is preselected, so typing replaces it and keeps the extension.
        Type("заметка");
        Keys(Enter);
        Wait.Until(() => File.Exists(Path.Combine(_app.Work, "заметка.txt")), because: "создан «заметка.txt»");
        Wait.Until(() => _app.Selected().SequenceEqual(new[] { "заметка.txt" }), because: "новый файл выделен");
    }
}

using System.IO;
using System.Text.Json;
using System.Windows;
using CanvasExplorer.Tests.Infrastructure;
using static CanvasExplorer.Tests.Infrastructure.Input;

namespace CanvasExplorer.Tests.UI;

/// <summary>The left panel: editable favorites and the context menus of its rows and empty space.</summary>
[Collection("UI"), Trait("Category", "UI")]
public class SidebarTests : IDisposable
{
    readonly AppSession _app = new();

    public void Dispose() => _app.Dispose();

    IReadOnlyList<string> FavoritePaths() =>
        _app.Settings().TryGetProperty("Favorites", out var f) && f.ValueKind == JsonValueKind.Array
            ? f.EnumerateArray().Select(x => x.GetProperty("Path").GetString()!).ToList()
            : Array.Empty<string>();

    void SeedFavorites(params string[] paths) =>
        _app.SeedSettings("\"Favorites\": [" + string.Join(",", paths.Select(p => "{ \"Path\": " + JsonSerializer.Serialize(p) + " }")) + "]");

    [Fact]
    public void Folder_menu_adds_it_to_favorites()
    {
        var folder = _app.MakeDir("Проекты");
        _app.Launch();
        _app.ClickRow("Проекты", Button.Right);
        _app.MenuItem("Добавить в избранное");
        _app.SideItem("Проекты");
        Wait.Until(() => FavoritePaths().Contains(folder, StringComparer.OrdinalIgnoreCase), because: "избранное сохранено");

        // Already a favorite: the menu no longer offers it.
        _app.ClickRow("Проекты", Button.Right);
        Thread.Sleep(300);
        Assert.Null(_app.FindInPopups("Добавить в избранное", System.Windows.Automation.ControlType.MenuItem));
        Keys(Esc);
    }

    [Fact]
    public void Favorite_menu_removes_renames_and_reorders()
    {
        var a = _app.MakeDir("Альфа");
        var b = _app.MakeDir("Бета");
        SeedFavorites(a, b);
        _app.Launch();

        _app.Click(_app.SideItem("Бета"), Button.Right);
        _app.MenuItem("Переместить вверх");
        Wait.Until(() => FavoritePaths().SequenceEqual(new[] { b, a }), because: "«Бета» поднялась выше");

        _app.Click(_app.SideItem("Альфа"), Button.Right);
        _app.MenuItem("Переименовать…");
        _app.Dialog();
        Keys(Ctrl, Letter('A'));
        Type("Работа");
        Keys(Enter);
        _app.SideItem("Работа");
        Assert.True(Directory.Exists(a), "сама папка не переименована");

        _app.Click(_app.SideItem("Работа"), Button.Right);
        _app.MenuItem("Убрать из избранного");
        Wait.Until(() => _app.FindSideItem("Работа") == null, because: "убрано из панели");
        Wait.Until(() => FavoritePaths().SequenceEqual(new[] { b }), because: "избранное сохранено");
        Assert.True(Directory.Exists(a), "папка не удалена");
    }

    [Fact]
    public void Favorites_can_be_reordered_by_dragging()
    {
        var a = _app.MakeDir("Первая");
        var b = _app.MakeDir("Вторая");
        var c = _app.MakeDir("Третья");
        SeedFavorites(a, b, c);
        _app.Launch();
        var from = AppSession.Center(_app.SideItem("Третья"));
        var to = AppSession.Center(_app.SideItem("Первая"));
        Input.Drag(Button.Left, 300, from, new Point(from.X, from.Y - 12), to);
        Wait.Until(() => FavoritePaths().SequenceEqual(new[] { c, a, b }), because: "«Третья» встала первой");
        // A drag is not a click: the folder was not opened.
        Assert.Equal("Папка", _app.Title);
    }

    [Fact]
    public void Folder_dragged_onto_the_favorites_title_becomes_a_favorite()
    {
        var folder = _app.MakeDir("Входящие");
        SeedFavorites();
        _app.Launch();
        var from = AppSession.Bounds(_app.Row("Входящие"));
        var title = _app.FindByName("Избранное", System.Windows.Automation.ControlType.Text)!;
        Input.Drag(Button.Left, 300, new Point(from.X + 48, from.Y + from.Height / 2), new Point(from.X, from.Y), AppSession.Center(title));
        _app.SideItem("Входящие");
        Assert.True(Directory.Exists(folder), "папка осталась на месте");
    }

    [Fact]
    public void Empty_space_menu_adds_the_current_folder_and_restores_defaults()
    {
        SeedFavorites();
        _app.Launch();

        Input.Click(_app.SidebarEmptySpot(), Button.Right);
        _app.MenuItem("Добавить текущую папку в избранное");
        _app.SideItem("Папка");

        Input.Click(_app.SidebarEmptySpot(), Button.Right);
        _app.MenuItem("Восстановить стандартное избранное");
        Wait.Until(() => _app.FindSideItem("Папка") == null, because: "вернулось стандартное избранное");
        _app.SideItem("Рабочий стол");
    }

    [Fact]
    public void Drive_menu_opens_it_in_a_new_tab()
    {
        _app.Launch();
        var drive = _app.Window.FindAll(System.Windows.Automation.TreeScope.Descendants,
                new System.Windows.Automation.PropertyCondition(System.Windows.Automation.AutomationElement.ControlTypeProperty, System.Windows.Automation.ControlType.RadioButton))
            .Cast<System.Windows.Automation.AutomationElement>().First(e => e.Current.Name.EndsWith("(C:)"));
        _app.Click(drive, Button.Right);
        _app.MenuItem("Открыть в новой вкладке");
        Wait.Until(() => _app.Title.EndsWith("(C:)"), because: "диск открыт во вкладке");
        _app.Close();
        Assert.Equal(2, _app.Settings().GetProperty("Tabs").GetArrayLength());
    }
}

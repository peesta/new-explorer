using System.IO;
using CanvasExplorer.Tests.Infrastructure;
using static CanvasExplorer.Tests.Infrastructure.Input;

namespace CanvasExplorer.Tests.UI;

/// <summary>Tabs and the settings that remember them.</summary>
[Collection("UI"), Trait("Category", "UI")]
public class TabTests : IDisposable
{
    readonly AppSession _app = new();

    public void Dispose() => _app.Dispose();

    [Fact]
    public void Open_tabs_survive_a_crash()
    {
        _app.MakeDir("Вторая");
        _app.Launch();
        _app.ClickRow("Вторая");
        Keys(Ctrl, Letter('T'));
        Thread.Sleep(300);
        _app.DoubleClick(_app.Row("Вторая"));
        Wait.Until(() => _app.Title == "Вторая");

        // Settings are written shortly after a change, not only on a clean exit.
        Thread.Sleep(1800);
        _app.Kill();
        var tabs = _app.Settings().GetProperty("Tabs").EnumerateArray().Select(t => t.GetString()).ToList();
        Assert.Equal(2, tabs.Count);
        Assert.Contains(tabs, t => t!.EndsWith("Вторая"));
    }

    [Fact]
    public void Ctrl_W_closes_the_tab_and_the_last_one_closes_the_window()
    {
        _app.Launch();
        Keys(Ctrl, Letter('T'));
        Thread.Sleep(400);
        Keys(Ctrl, Letter('W'));
        Thread.Sleep(600);
        Assert.True(_app.IsRunning);
        Keys(Ctrl, Letter('W'));
        Wait.Until(() => !_app.IsRunning, 5000, "окно закрылось с последней вкладкой");
        Assert.Equal(1, _app.Settings().GetProperty("Tabs").GetArrayLength());
    }

    [Fact]
    public void Ctrl_Tab_switches_between_tabs()
    {
        _app.MakeDir("Другая");
        _app.Launch();
        _app.Row("Другая");
        var r = AppSession.Bounds(_app.Row("Другая"));
        Input.Click(new System.Windows.Point(r.X + 48, r.Y + r.Height / 2), Button.Middle);
        Wait.Until(() => _app.Title == "Другая");
        Keys(Ctrl, Tab);
        Wait.Until(() => _app.Title == "Папка", because: "Ctrl+Tab переключил вкладку");
        Keys(Ctrl, Shift, Tab);
        Wait.Until(() => _app.Title == "Другая", because: "Ctrl+Shift+Tab переключил обратно");
    }

    [Fact]
    public void A_restart_restores_tabs_and_the_active_one()
    {
        _app.MakeDir("Одна");
        _app.Launch();
        var r = AppSession.Bounds(_app.Row("Одна"));
        Input.Click(new System.Windows.Point(r.X + 48, r.Y + r.Height / 2), Button.Middle);
        Wait.Until(() => _app.Title == "Одна");
        _app.Close();

        // Started again on the first folder: both tabs come back, and the folder that is already
        // open is brought to front instead of being opened a second time.
        _app.Launch();
        Wait.Until(() => _app.Title == "Папка");
        _app.Close();
        Assert.Equal(2, _app.Settings().GetProperty("Tabs").GetArrayLength());
    }
}

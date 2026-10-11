using System.IO;
using System.Text.Json;
using System.Windows;
using System.Windows.Automation;
using CanvasExplorer.Models;
using CanvasExplorer.Tests.Infrastructure;
using static CanvasExplorer.Tests.Infrastructure.Input;

namespace CanvasExplorer.Tests.UI;

/// <summary>The app-wide player in the sidebar: it keeps playing across folders and tabs.</summary>
[Collection("UI"), Trait("Category", "UI")]
public class PlayerTests : IDisposable
{
    readonly AppSession _app = new();

    public void Dispose() => _app.Dispose();

    /// <summary>A quiet tone long enough to outlive the test steps.</summary>
    string WriteWav(string name, int seconds = 40)
    {
        var path = Path.Combine(_app.Work, name);
        const int rate = 8000;
        var samples = rate * seconds;
        using var w = new BinaryWriter(File.Create(path));
        w.Write("RIFF"u8); w.Write(36 + samples * 2); w.Write("WAVE"u8);
        w.Write("fmt "u8); w.Write(16); w.Write((short)1); w.Write((short)1); w.Write(rate); w.Write(rate * 2); w.Write((short)2); w.Write((short)16);
        w.Write("data"u8); w.Write(samples * 2);
        for (var i = 0; i < samples; i++) w.Write((short)(Math.Sin(i * 2 * Math.PI * 330 / rate) * 300));
        return path;
    }

    void PinOnCanvas(params string[] names)
    {
        var layout = new FolderLayout { List = new RectState { X = 0, Y = 0, W = 380, H = 480 } };
        var y = 20;
        foreach (var n in names)
        {
            layout.Items.Add(new PinState { Name = n, X = 410, Y = y, W = 460, H = 136 });
            y += 160;
        }
        _app.WriteLayout(layout);
    }

    AutomationElement? Mini() => _app.Find("MiniPlayer");

    string MiniPlayName => _app.Get("MiniPlay").Current.Name;

    TimeSpan MiniTime => TimeSpan.ParseExact(_app.Get("MiniTime").Current.Name.Split(" / ")[0], @"m\:ss", null);

    void PlayInWindow(string name)
    {
        var play = Wait.For(() => _app.Pinned(name).FindFirst(TreeScope.Descendants, new AndCondition(
            new PropertyCondition(AutomationElement.ControlTypeProperty, ControlType.Button),
            new PropertyCondition(AutomationElement.NameProperty, "Воспроизвести"))), 5000, "кнопка «Воспроизвести» в окне");
        Wait.Until(() => play.Current.IsEnabled, because: "файл прочитан");
        _app.Click(play);
    }

    [Fact]
    public void Playing_on_the_canvas_opens_the_sidebar_player_which_survives_folders_and_tabs()
    {
        WriteWav("музыка.wav");
        _app.MakeDir("Другая");
        PinOnCanvas("музыка.wav");
        _app.Launch();
        Assert.Null(Mini());

        PlayInWindow("музыка.wav");
        Wait.For(Mini, 4000, "плеер появился в левой панели");
        Assert.Equal("Пауза", MiniPlayName);
        Assert.Equal("музыка.wav", _app.Get("MiniTitle").Current.Name);

        // Another folder in a new tab, then the tab of the folder it plays from is closed.
        var row = AppSession.Bounds(_app.Row("Другая"));
        Input.Click(new Point(row.X + 48, row.Y + row.Height / 2), Button.Middle);
        Wait.Until(() => _app.Title == "Другая");
        Keys(Ctrl, Tab);
        Wait.Until(() => _app.Title == "Папка");
        Keys(Ctrl, Letter('W'));
        Wait.Until(() => _app.Title == "Другая", because: "вкладка с папкой плеера закрыта");

        var t1 = MiniTime;
        Thread.Sleep(2200);
        Assert.True(MiniTime > t1, "музыка продолжает играть");
        Assert.Equal("Пауза", MiniPlayName);
    }

    [Fact]
    public void Sidebar_player_pauses_seeks_and_closes_and_then_releases_the_file()
    {
        var file = WriteWav("трек.wav");
        PinOnCanvas("трек.wav");
        _app.Launch();
        PlayInWindow("трек.wav");
        Wait.For(Mini, 4000, "плеер появился");

        _app.Click(_app.Get("MiniPlay"));
        Wait.Until(() => MiniPlayName == "Воспроизвести", because: "пауза");
        var paused = MiniTime;
        Thread.Sleep(1200);
        Assert.Equal(paused, MiniTime);
        // The window on the canvas shows the same state.
        Assert.NotNull(_app.Pinned("трек.wav").FindFirst(TreeScope.Descendants, new PropertyCondition(AutomationElement.NameProperty, "Воспроизвести")));

        _app.Click(_app.Get("MiniClose"));
        Wait.Until(() => Mini() == null, because: "плеер закрыт");
        // Nothing holds the file any more: it can be renamed while its window stays pinned.
        var renamed = Path.Combine(_app.Work, "трек2.wav");
        File.Move(file, renamed);
        File.Move(renamed, file);
    }

    [Fact]
    public void A_pinned_audio_window_does_not_lock_its_file()
    {
        var file = WriteWav("запись.wav", 5);
        PinOnCanvas("запись.wav");
        _app.Launch();
        Wait.Until(() => _app.Pinned("запись.wav").FindFirst(TreeScope.Descendants, new AndCondition(
            new PropertyCondition(AutomationElement.NameProperty, "Воспроизвести"),
            new PropertyCondition(AutomationElement.IsEnabledProperty, true))) != null, because: "файл прочитан");
        File.Move(file, file + ".bak");
        File.Move(file + ".bak", file);
    }

    [Fact]
    public void Player_stays_put_under_the_tags_while_a_long_sidebar_scrolls()
    {
        var favs = Enumerable.Range(1, 40).Select(i => _app.MakeDir("Избранная " + i)).ToList();
        _app.SeedSettings("\"Favorites\": [" + string.Join(",", favs.Select(p => "{ \"Path\": " + JsonSerializer.Serialize(p) + " }")) + "]");
        WriteWav("музыка.wav");
        PinOnCanvas("музыка.wav");
        _app.Launch();
        PlayInWindow("музыка.wav");
        var mini = Wait.For(Mini, 4000, "плеер появился");

        var window = AppSession.Bounds(_app.Window);
        var before = AppSession.Bounds(mini);
        Assert.True(before.Bottom <= window.Bottom && before.Height > 60, "плеер целиком виден");

        var scroll = (ScrollPattern)_app.Get("SideScroll").GetCurrentPattern(ScrollPattern.Pattern);
        Assert.True(scroll.Current.VerticallyScrollable, "у списка избранного, дисков и тегов есть прокрутка");
        Input.Wheel(AppSession.Center(_app.SideItem("Избранная 3")), -15);
        Wait.Until(() => scroll.Current.VerticalScrollPercent > 50, because: "панель прокрутилась");
        Assert.Equal(before, AppSession.Bounds(mini));
        // The sections end where the player begins.
        Assert.True(AppSession.Bounds(_app.Get("SideScroll")).Bottom <= before.Top + 1);
    }
}

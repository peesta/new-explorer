using System.IO;
using System.Windows;
using System.Windows.Automation;
using CanvasExplorer.Models;
using CanvasExplorer.Tests.Infrastructure;
using static CanvasExplorer.Tests.Infrastructure.Input;

namespace CanvasExplorer.Tests.UI;

/// <summary>Video on the canvas and in the sidebar, two players together, per-file volume, the sidebar on the right.</summary>
[Collection("UI"), Trait("Category", "UI")]
public class VideoAndDockTests : IDisposable
{
    readonly AppSession _app = new();

    public void Dispose() => _app.Dispose();

    void Pin(params (string Name, double H)[] files)
    {
        var layout = new FolderLayout { List = new RectState { X = 0, Y = 0, W = 330, H = 480 } };
        var y = 10.0;
        foreach (var (name, h) in files)
        {
            layout.Items.Add(new PinState { Name = name, X = 350, Y = y, W = 440, H = h });
            y += h + 14;
        }
        _app.WriteLayout(layout);
    }

    AutomationElement PlayButton(string pinned, string name = "Воспроизвести") =>
        Wait.For(() => _app.Pinned(pinned).FindFirst(TreeScope.Descendants, new AndCondition(
            new PropertyCondition(AutomationElement.ControlTypeProperty, ControlType.Button),
            new PropertyCondition(AutomationElement.NameProperty, name))), 5000, $"кнопка «{name}» в окне «{pinned}»");

    static double Range(AutomationElement e) => ((RangeValuePattern)e.GetCurrentPattern(RangeValuePattern.Pattern)).Current.Value;

    AutomationElement VolumeIn(AutomationElement scope) =>
        Wait.For(() => scope.FindFirst(TreeScope.Descendants, new PropertyCondition(AutomationElement.AutomationIdProperty, "Volume")), 5000, "регулятор громкости");

    static TimeSpan Time(AutomationElement text) => TimeSpan.ParseExact(text.Current.Name.Split(" / ")[0], @"m\:ss", null);

    [Fact]
    public void Video_plays_on_the_canvas_and_goes_on_in_the_sidebar_after_leaving_the_folder()
    {
        TestMedia.Avi(Path.Combine(_app.Work, "клип.avi"), 30);
        _app.MakeDir("Другая");
        Pin(("клип.avi", 300));
        _app.Launch();
        Assert.Null(_app.Find("VideoPlayer"));

        _app.Click(PlayButton("клип.avi"));
        Wait.For(() => _app.Find("VideoPlayer"), 5000, "видеоплеер появился в левой панели");
        Wait.Until(() => Time(_app.Get("VideoWindowTime", _app.Pinned("клип.avi"))) >= TimeSpan.FromSeconds(1), 6000, "видео идёт в окне");
        Assert.Equal("Пауза", _app.Get("VideoPlay").Current.Name);
        // The window shows the picture (the test clip is green/blue, not black).
        var w = AppSession.Bounds(_app.Pinned("клип.avi"));
        Input.MoveTo(new Point(w.Right + 40, w.Bottom + 40));
        var (r, g, b) = AppSession.PixelAt(new Point(w.X + w.Width / 2, w.Y + w.Height * 0.35));
        Assert.True(r + g + b > 120, $"в окне видна картинка видео ({r},{g},{b})");
        // ...and so does the sidebar player.
        var side = AppSession.Bounds(_app.Get("VideoPlayer"));
        var (sr, sg, sb) = AppSession.PixelAt(new Point(side.X + side.Width / 2, side.Y + side.Height * 0.4));
        Assert.True(sr + sg + sb > 120, $"в видеоплеере панели видна картинка ({sr},{sg},{sb})");

        _app.DoubleClick(_app.Row("Другая"));
        Wait.Until(() => _app.Title == "Другая");
        var t = Time(_app.Get("VideoTime"));
        Thread.Sleep(1500);
        Assert.True(Time(_app.Get("VideoTime")) > t, "видео продолжает идти после ухода из папки");
        Assert.Equal("клип.avi", _app.Get("VideoTitle").Current.Name);
    }

    [Fact]
    public void Audio_and_video_players_live_together_and_starting_one_pauses_the_other()
    {
        TestMedia.Avi(Path.Combine(_app.Work, "клип.avi"), 30);
        TestMedia.Wav(Path.Combine(_app.Work, "звук.wav"), 30);
        Pin(("клип.avi", 260), ("звук.wav", 136));
        _app.Launch();

        _app.Click(PlayButton("звук.wav"));
        Wait.Until(() => _app.Find("MiniPlay")?.Current.Name == "Пауза", because: "играет аудио");
        _app.Click(PlayButton("клип.avi"));
        Wait.Until(() => _app.Find("VideoPlay")?.Current.Name == "Пауза", because: "играет видео");
        // Both players stay; the audio one is paused.
        Wait.Until(() => _app.Get("MiniPlay").Current.Name == "Воспроизвести", because: "аудио на паузе");
        Assert.NotNull(_app.Find("MiniPlayer"));

        _app.Click(_app.Get("MiniPlay"));
        Wait.Until(() => _app.Get("VideoPlay").Current.Name == "Воспроизвести", because: "видео на паузе после запуска аудио");
        Assert.Equal("Пауза", _app.Get("MiniPlay").Current.Name);
    }

    [Fact]
    public void Players_sit_at_the_very_bottom_of_the_sidebar()
    {
        TestMedia.Wav(Path.Combine(_app.Work, "звук.wav"), 20);
        _app.SeedSettings("\"Favorites\": []");
        Pin(("звук.wav", 136));
        _app.Launch();
        _app.Click(PlayButton("звук.wav"));
        var mini = AppSession.Bounds(Wait.For(() => _app.Find("MiniPlayer"), 5000, "плеер"));
        var tagButton = AppSession.Bounds(_app.Get("NewTagBtn"));
        var window = AppSession.Bounds(_app.Window);
        // Not right under the tags (there is room), but just above the lower edge.
        Assert.True(mini.Top - tagButton.Bottom > 100, "между тегами и плеером свободное место");
        Assert.InRange(window.Bottom - mini.Bottom, 0, 40);
    }

    [Fact]
    public void Volume_is_per_file_and_new_files_start_at_the_last_level()
    {
        TestMedia.Wav(Path.Combine(_app.Work, "a.wav"), 5);
        TestMedia.Wav(Path.Combine(_app.Work, "b.wav"), 5);
        TestMedia.Wav(Path.Combine(_app.Work, "c.wav"), 5);
        Pin(("a.wav", 136), ("b.wav", 136));
        _app.Launch();
        var a = VolumeIn(_app.Pinned("a.wav"));
        var b = VolumeIn(_app.Pinned("b.wav"));
        Assert.Equal(80, Range(a));
        Assert.Equal(80, Range(b));

        var r = AppSession.Bounds(a);
        Input.Click(new Point(r.X + r.Width * 0.3, r.Y + r.Height / 2));
        Wait.Until(() => Math.Abs(Range(a) - 30) <= 3, because: "громкость первого файла 30 %");
        Thread.Sleep(400);
        Assert.Equal(80, Range(b)); // the other file keeps its own

        _app.ClickRow("c.wav", Button.Right);
        _app.MenuItem("Закрепить на холсте");
        var c = VolumeIn(_app.Pinned("c.wav"));
        Assert.InRange(Range(c), 27, 33); // a new file starts at the last level set
    }

    [Fact]
    public void Sidebar_player_volume_changes_only_the_playing_file()
    {
        TestMedia.Wav(Path.Combine(_app.Work, "a.wav"), 20);
        TestMedia.Wav(Path.Combine(_app.Work, "b.wav"), 20);
        Pin(("a.wav", 136), ("b.wav", 136));
        _app.Launch();
        _app.Click(PlayButton("a.wav"));
        var mini = Wait.For(() => _app.Find("MiniPlayer"), 5000, "плеер");
        var bar = VolumeIn(mini);
        var r = AppSession.Bounds(bar);
        Input.Click(new Point(r.X + r.Width * 0.5, r.Y + r.Height / 2));
        Wait.Until(() => Math.Abs(Range(VolumeIn(_app.Pinned("a.wav"))) - 50) <= 4, because: "громкость играющего файла изменилась и в его окне");
        Assert.Equal(80, Range(VolumeIn(_app.Pinned("b.wav"))));
    }

    [Fact]
    public void Player_height_changes_by_its_top_edge_and_players_swap_by_dragging()
    {
        TestMedia.Avi(Path.Combine(_app.Work, "клип.avi"), 30);
        TestMedia.Wav(Path.Combine(_app.Work, "звук.wav"), 30);
        Pin(("клип.avi", 260), ("звук.wav", 136));
        _app.Launch();
        _app.Click(PlayButton("клип.avi"));
        _app.Click(PlayButton("звук.wav"));
        var video = Wait.For(() => _app.Find("VideoPlayer"), 5000, "видеоплеер");
        var audio = Wait.For(() => _app.Find("MiniPlayer"), 5000, "аудиоплеер");
        // Default order: video above audio.
        Assert.True(AppSession.Bounds(video).Top < AppSession.Bounds(audio).Top);

        // Taller by dragging the top edge up.
        var v = AppSession.Bounds(video);
        Input.Drag(Button.Left, 100, new Point(v.X + v.Width / 2, v.Top + 2), new Point(v.X + v.Width / 2, v.Top - 60));
        Wait.Until(() => AppSession.Bounds(video).Height > v.Height + 40, because: "видеоплеер стал выше");
        Wait.Until(() => _app.Settings().GetProperty("VideoPlayerHeight").GetDouble() > 250, because: "высота запомнена");

        // Drag the video player's title row below the audio player: they swap.
        var title = AppSession.Bounds(_app.Get("VideoTitle"));
        var a = AppSession.Bounds(audio);
        Input.Drag(Button.Left, 150, new Point(title.X + 20, title.Y + title.Height / 2), new Point(title.X + 20, title.Y + 20), new Point(title.X + 20, a.Bottom - 5));
        Wait.Until(() => AppSession.Bounds(video).Top > AppSession.Bounds(audio).Top, because: "плееры поменялись местами");
        Wait.Until(() => _app.Settings().GetProperty("PlayerOrder")[0].GetString() == "audio", because: "порядок запомнен");
    }

    [Fact]
    public void Sidebar_moves_to_the_right_by_dragging_and_back()
    {
        _app.WriteFile("a.txt");
        _app.Launch();
        var canvas = AppSession.Bounds(_app.Get("CanvasArea"));
        var spot = _app.SidebarEmptySpot();
        var window = AppSession.Bounds(_app.Window);
        Input.Drag(Button.Left, 200, spot, new Point(spot.X + 40, spot.Y), new Point(window.Right - 100, spot.Y));

        Wait.Until(() => AppSession.Bounds(_app.Get("NewTagBtn")).X > AppSession.Bounds(_app.Get("CanvasArea")).Right - 20,
            because: "панель справа от холста");
        var side = AppSession.Bounds(_app.Get("SideScroll"));
        var canvasNow = AppSession.Bounds(_app.Get("CanvasArea"));
        // As tall as the canvas (not the whole window), the canvas starts at the window's left now.
        Assert.InRange(Math.Abs(side.Top - canvasNow.Top), 0, 6);
        Assert.True(canvasNow.Left < canvas.Left - 100, "холст занял место слева");
        Wait.Until(() => _app.Settings().GetProperty("SidebarRight").GetBoolean(), because: "положение запомнено");

        // Comes back after a restart, and can be dragged back.
        _app.Close();
        _app.Launch();
        Wait.Until(() => AppSession.Bounds(_app.Get("NewTagBtn")).X > AppSession.Bounds(_app.Get("CanvasArea")).Right - 20, because: "после перезапуска справа");
        spot = _app.SidebarEmptySpot();
        Input.Drag(Button.Left, 200, spot, new Point(spot.X - 40, spot.Y), new Point(window.Left + 80, spot.Y));
        Wait.Until(() => AppSession.Bounds(_app.Get("NewTagBtn")).X < AppSession.Bounds(_app.Get("CanvasArea")).Left, because: "панель снова слева");
    }

    [Fact]
    public void Sidebar_on_the_right_still_resizes_from_its_inner_edge()
    {
        _app.SeedSettings("\"SidebarRight\": true");
        _app.Launch();
        var side = AppSession.Bounds(_app.Get("SideScroll"));
        var y = side.Y + side.Height / 2;
        Input.Drag(Button.Left, 100, new Point(side.Left - 3, y), new Point(side.Left - 83, y));
        Wait.Until(() => AppSession.Bounds(_app.Get("SideScroll")).Width > side.Width + 50, because: "панель стала шире");
    }
}

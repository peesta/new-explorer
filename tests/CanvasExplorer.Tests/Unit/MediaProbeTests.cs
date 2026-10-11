using System.IO;
using System.Windows.Media;
using System.Windows.Threading;
using CanvasExplorer.Tests.Infrastructure;

namespace CanvasExplorer.Tests.Unit;

/// <summary>The generated test video really plays with the player the app uses (WPF MediaPlayer).</summary>
public class MediaProbeTests : IDisposable
{
    readonly string _dir = TestSetup.NewFolder("media");

    public void Dispose() => TestSetup.DeleteFolder(_dir);

    /// <summary>For a manual look: set CANVAS_SAMPLES_DIR and run this test to get a clip and a sound there.</summary>
    [Fact]
    public void Samples_for_a_manual_look()
    {
        if (Environment.GetEnvironmentVariable("CANVAS_SAMPLES_DIR") is not { Length: > 0 } dir) return;
        Directory.CreateDirectory(dir);
        TestMedia.Avi(Path.Combine(dir, "клип.avi"), 40, 160, 90, 15);
        TestMedia.Wav(Path.Combine(dir, "звук.wav"), 40);
    }

    [Fact]
    public void Generated_avi_opens_with_the_right_size_and_length()
    {
        var path = Path.Combine(_dir, "clip.avi");
        TestMedia.Avi(path, 3);
        var (w, h, seconds) = Sta.Run(() =>
        {
            var player = new MediaPlayer();
            var frame = new DispatcherFrame();
            var failed = "";
            player.MediaOpened += (_, _) => frame.Continue = false;
            player.MediaFailed += (_, e) => { failed = e.ErrorException.Message; frame.Continue = false; };
            player.Open(new Uri(path));
            var timer = new DispatcherTimer { Interval = TimeSpan.FromSeconds(10) };
            timer.Tick += (_, _) => { failed = "timeout"; frame.Continue = false; };
            timer.Start();
            Dispatcher.PushFrame(frame);
            Assert.Equal("", failed);
            var r = (player.NaturalVideoWidth, player.NaturalVideoHeight, player.NaturalDuration.TimeSpan.TotalSeconds);
            player.Close();
            return r;
        });
        Assert.Equal(64, w);
        Assert.Equal(48, h);
        Assert.InRange(seconds, 2.8, 3.2);
    }
}

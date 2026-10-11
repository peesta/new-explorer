using System.Windows;
using System.Windows.Media;
using System.Windows.Threading;

namespace CanvasExplorer.Services;

/// <summary>
/// The one video player of the app (WPF MediaPlayer, i.e. Windows Media Foundation codecs). Like the audio player it
/// belongs to no window: the picture can be shown in several places at once (<see cref="CreateBrush"/>) — the window on
/// the canvas and the video player in the sidebar — and goes on when the window or its folder is closed.
/// Use from the UI thread.
/// </summary>
public sealed class VideoEngine : IMediaEngine
{
    public static VideoEngine Current { get; } = new();

    readonly VolumeMemory _volumes = new(() => AppSettings.Current.VideoVolume, v =>
    {
        AppSettings.Current.VideoVolume = v;
        AppSettings.ScheduleSave();
    });

    MediaPlayer? _player;
    DispatcherTimer? _tick;
    bool _playing;
    bool _loop;
    TimeSpan _pendingStart;

    public event EventHandler? Changed;
    public event EventHandler? Tick;
    public event EventHandler<string>? VolumeChanged;
    /// <summary>The picture size became known (first frame decoded).</summary>
    public event EventHandler? Opened;
    /// <summary>The file could not be played (argument: path).</summary>
    public event EventHandler<string>? Failed;

    public string? Path { get; private set; }
    /// <summary>Set once the current file is open (size and length known).</summary>
    public bool IsOpen { get; private set; }
    public bool IsPlaying => _player != null && _playing;
    public TimeSpan Position => _player?.Position ?? TimeSpan.Zero;
    public TimeSpan Duration => _player?.NaturalDuration is { HasTimeSpan: true } d ? d.TimeSpan : TimeSpan.Zero;
    public Size NaturalSize => _player is { NaturalVideoWidth: > 0 } p ? new Size(p.NaturalVideoWidth, p.NaturalVideoHeight) : Size.Empty;
    public MediaPlayer? Player => _player;

    public bool IsCurrent(string path) => Path != null && string.Equals(Path, path, StringComparison.OrdinalIgnoreCase);

    public bool Loop
    {
        get => _loop;
        set { if (_loop != value) { _loop = value; Raise(); } }
    }

    public double VolumeOf(string path) => _volumes.Of(path);

    public void SetVolume(string path, double volume)
    {
        if (!_volumes.Set(path, volume)) return;
        if (IsCurrent(path) && _player != null) _player.Volume = VolumeOf(path);
        VolumeChanged?.Invoke(this, path);
    }

    public bool Play(string path, TimeSpan start)
    {
        if (!IsCurrent(path))
        {
            Close(raise: false);
            var player = new MediaPlayer { ScrubbingEnabled = true, Volume = VolumeOf(path) };
            player.MediaOpened += (_, _) => OnOpened(player);
            player.MediaEnded += (_, _) => OnEnded(player);
            player.MediaFailed += (_, e) => OnFailed(player, path);
            _player = player;
            Path = path;
            IsOpen = false;
            _pendingStart = start;
            player.Open(new Uri(path));
        }
        if (_player == null) return false;
        if (IsOpen && Duration > TimeSpan.Zero && Position >= Duration - TimeSpan.FromMilliseconds(80)) _player.Position = TimeSpan.Zero;
        // One sound at a time: playing audio pauses.
        AudioEngine.Current.Pause();
        _player.Play();
        _playing = true;
        StartTick();
        Raise();
        return true;
    }

    public void Pause()
    {
        if (!IsPlaying) return;
        _player!.Pause();
        _playing = false;
        _tick?.Stop();
        Raise();
    }

    public void Toggle(string path, TimeSpan startIfNew)
    {
        if (IsCurrent(path) && IsPlaying) Pause();
        else Play(path, IsCurrent(path) ? Position : startIfNew);
    }

    public void Seek(TimeSpan to)
    {
        if (_player == null) return;
        var max = Duration > TimeSpan.Zero ? Duration : to;
        if (!IsOpen) _pendingStart = to;
        else _player.Position = TimeSpan.FromTicks(Math.Clamp(to.Ticks, 0, max.Ticks));
        Tick?.Invoke(this, EventArgs.Empty);
    }

    public void Close() => Close(raise: true);

    void Close(bool raise)
    {
        _tick?.Stop();
        var had = Path != null;
        _player?.Close();
        _player = null;
        _playing = false;
        IsOpen = false;
        Path = null;
        if (raise && had) Raise();
    }

    void OnOpened(MediaPlayer player)
    {
        if (player != _player) return;
        IsOpen = true;
        if (_pendingStart > TimeSpan.Zero && _pendingStart < Duration) player.Position = _pendingStart;
        _pendingStart = TimeSpan.Zero;
        Opened?.Invoke(this, EventArgs.Empty);
        Raise();
    }

    void OnEnded(MediaPlayer player)
    {
        if (player != _player) return;
        player.Position = TimeSpan.Zero;
        if (_loop)
        {
            player.Play();
            return;
        }
        player.Pause();
        _playing = false;
        _tick?.Stop();
        Raise();
        Tick?.Invoke(this, EventArgs.Empty);
    }

    void OnFailed(MediaPlayer player, string path)
    {
        if (player != _player) return;
        Close(raise: true);
        Failed?.Invoke(this, path);
    }

    /// <summary>A brush that paints the current picture (Uniform). Each view makes its own; all show the same frames.</summary>
    public Brush? CreateBrush()
    {
        if (_player == null) return null;
        var size = NaturalSize.IsEmpty ? new Size(16, 9) : NaturalSize;
        return new DrawingBrush(new VideoDrawing { Player = _player, Rect = new Rect(size) }) { Stretch = Stretch.Uniform };
    }

    void StartTick()
    {
        if (_tick == null)
        {
            _tick = new DispatcherTimer { Interval = TimeSpan.FromMilliseconds(40) };
            _tick.Tick += (_, _) => Tick?.Invoke(this, EventArgs.Empty);
        }
        _tick.Start();
    }

    void Raise() => Changed?.Invoke(this, EventArgs.Empty);
}

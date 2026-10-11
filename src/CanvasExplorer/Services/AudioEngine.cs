using System.IO;
using System.Windows.Threading;
using NAudio.Wave;

namespace CanvasExplorer.Services;

/// <summary>
/// The one audio player of the app. It belongs to no window: playback goes on when the pinned window that started
/// it is unpinned, its folder is left or its tab is closed. Windows on the canvas and the audio player in the sidebar
/// are views of it. Each file keeps its own volume; repeat is shared. Use from the UI thread.
/// </summary>
public sealed class AudioEngine : IMediaEngine
{
    public static AudioEngine Current { get; } = new();

    readonly VolumeMemory _volumes = new(() => AppSettings.Current.AudioVolume, v =>
    {
        AppSettings.Current.AudioVolume = v;
        AppSettings.ScheduleSave();
    });

    WaveStream? _reader;
    WaveOutEvent? _output;
    bool _closing;
    bool _loop;
    DispatcherTimer? _tick;

    /// <summary>Track, play/pause, volume or repeat changed.</summary>
    public event EventHandler? Changed;
    /// <summary>Raised ~25 times a second while playing (position moved).</summary>
    public event EventHandler? Tick;
    public event EventHandler<string>? VolumeChanged;

    /// <summary>The loaded file (playing or paused), or null when the player is closed.</summary>
    public string? Path { get; private set; }
    public bool IsPlaying => _output?.PlaybackState == PlaybackState.Playing;
    public TimeSpan Position => _reader?.CurrentTime ?? TimeSpan.Zero;
    public TimeSpan Duration => _reader?.TotalTime ?? TimeSpan.Zero;

    public bool IsCurrent(string path) => Path != null && string.Equals(Path, path, StringComparison.OrdinalIgnoreCase);

    public bool Loop
    {
        get => _loop;
        set { if (_loop != value) { _loop = value; Raise(); } }
    }

    public double VolumeOf(string path) => _volumes.Of(path);

    /// <summary>Volume of one file (only that file); also the level files not adjusted yet will start at.</summary>
    public void SetVolume(string path, double volume)
    {
        if (!_volumes.Set(path, volume)) return;
        if (IsCurrent(path) && _output != null) _output.Volume = (float)VolumeOf(path);
        VolumeChanged?.Invoke(this, path);
    }

    /// <summary>Starts <paramref name="path"/> at <paramref name="start"/> (the current track just resumes). False if it can't be played.</summary>
    public bool Play(string path, TimeSpan start)
    {
        if (!IsCurrent(path))
        {
            Close(raise: false);
            try
            {
                _reader = new MediaFoundationReader(path);
                _output = new WaveOutEvent { DesiredLatency = 120 };
                _output.Init(_reader);
                _output.Volume = (float)VolumeOf(path);
                // Raised on NAudio's thread; a late event of a replaced output must not touch the new track.
                var ui = Dispatcher.CurrentDispatcher;
                var output = _output;
                output.PlaybackStopped += (_, _) => ui.BeginInvoke(() => OnStopped(output));
                Path = path;
                if (start > TimeSpan.Zero && start < _reader.TotalTime) _reader.CurrentTime = start;
            }
            catch
            {
                Close(raise: true);
                return false;
            }
        }
        if (_reader == null || _output == null) return false;
        if (_reader.CurrentTime >= _reader.TotalTime - TimeSpan.FromMilliseconds(50)) _reader.CurrentTime = TimeSpan.Zero;
        // One sound at a time: a playing video pauses.
        VideoEngine.Current.Pause();
        _output.Play();
        StartTick();
        Raise();
        return true;
    }

    public void Pause()
    {
        if (!IsPlaying) return;
        _output!.Pause();
        _tick?.Stop();
        Raise();
    }

    /// <summary>Play/pause for <paramref name="path"/>; another track is replaced.</summary>
    public void Toggle(string path, TimeSpan startIfNew)
    {
        if (IsCurrent(path) && IsPlaying) Pause();
        else Play(path, IsCurrent(path) ? Position : startIfNew);
    }

    public void Seek(TimeSpan to)
    {
        if (_reader == null) return;
        _reader.CurrentTime = TimeSpan.FromTicks(Math.Clamp(to.Ticks, 0, _reader.TotalTime.Ticks));
        Tick?.Invoke(this, EventArgs.Empty);
    }

    /// <summary>Stops and releases the file; the mini player disappears.</summary>
    public void Close() => Close(raise: true);

    void Close(bool raise)
    {
        _tick?.Stop();
        _closing = true;
        try
        {
            _output?.Stop();
            _output?.Dispose();
            _reader?.Dispose();
        }
        catch { }
        _closing = false;
        _output = null;
        _reader = null;
        var had = Path != null;
        Path = null;
        if (raise && had) Raise();
    }

    void OnStopped(WaveOutEvent output)
    {
        if (_closing || _reader == null || output != _output) return;
        // Reached the end of the file.
        _reader.CurrentTime = TimeSpan.Zero;
        if (_loop)
        {
            output.Play();
            return;
        }
        _tick?.Stop();
        Raise();
        Tick?.Invoke(this, EventArgs.Empty);
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

    // ---------- file information (opened and released at once, so the file is not held) ----------

    public sealed record Info(TimeSpan Duration, int SampleRate, int Channels);

    public static Task<Info> ReadInfoAsync(string path) => Task.Run(() =>
    {
        using var reader = new MediaFoundationReader(path);
        return new Info(reader.TotalTime, reader.WaveFormat.SampleRate, reader.WaveFormat.Channels);
    });

    static readonly Dictionary<string, Task<float[]>> Peaks = new(StringComparer.OrdinalIgnoreCase);

    /// <summary>The waveform (normalized peaks), computed once per file version and shared by all views.</summary>
    public static Task<float[]> PeaksAsync(string path)
    {
        var key = path + "|" + File.GetLastWriteTimeUtc(path).Ticks;
        lock (Peaks)
        {
            if (Peaks.TryGetValue(key, out var t) && !t.IsFaulted) return t;
            if (Peaks.Count > 50) Peaks.Clear();
            return Peaks[key] = Task.Run(() => ComputePeaks(path, 800));
        }
    }

    static float[] ComputePeaks(string path, int buckets)
    {
        using var reader = new MediaFoundationReader(path);
        var provider = reader.ToSampleProvider();
        var bytesPerSample = Math.Max(1, reader.WaveFormat.BitsPerSample / 8);
        var totalSamples = Math.Max(1, reader.Length / bytesPerSample);
        var perBucket = Math.Max(1, totalSamples / buckets);
        var peaks = new float[buckets];
        var buffer = new float[8192];
        long index = 0;
        int read;
        while ((read = provider.Read(buffer, 0, buffer.Length)) > 0)
        {
            for (var i = 0; i < read; i++, index++)
            {
                var b = (int)Math.Min(buckets - 1, index / perBucket);
                var v = Math.Abs(buffer[i]);
                if (v > peaks[b]) peaks[b] = v;
            }
        }
        var max = peaks.Max();
        if (max > 0) for (var i = 0; i < peaks.Length; i++) peaks[i] /= max;
        return peaks;
    }

    public static string Format(TimeSpan t) => t.TotalHours >= 1 ? t.ToString(@"h\:mm\:ss") : t.ToString(@"m\:ss");
}

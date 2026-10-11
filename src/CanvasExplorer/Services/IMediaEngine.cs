namespace CanvasExplorer.Services;

/// <summary>
/// An app-wide player (audio or video). It belongs to no window: windows on the canvas and the players in the sidebar
/// are views of it. Only one of the players sounds at a time: starting one pauses the other.
/// </summary>
public interface IMediaEngine
{
    /// <summary>Track loaded or closed, play/pause, repeat changed.</summary>
    event EventHandler? Changed;
    /// <summary>Raised ~25 times a second while playing (position moved).</summary>
    event EventHandler? Tick;
    /// <summary>The volume of one file changed (argument: its path).</summary>
    event EventHandler<string>? VolumeChanged;

    string? Path { get; }
    bool IsPlaying { get; }
    TimeSpan Position { get; }
    TimeSpan Duration { get; }
    bool Loop { get; set; }

    bool IsCurrent(string path);
    bool Play(string path, TimeSpan start);
    void Pause();
    void Toggle(string path, TimeSpan startIfNew);
    void Seek(TimeSpan to);
    void Close();

    /// <summary>Each file has its own volume; a file never adjusted starts at the level set last (on any file).</summary>
    double VolumeOf(string path);
    void SetVolume(string path, double volume);
}

/// <summary>Per-file volumes with "the last level set is where new files start".</summary>
sealed class VolumeMemory
{
    readonly Dictionary<string, double> _byFile = new(StringComparer.OrdinalIgnoreCase);
    readonly Func<double> _getLast;
    readonly Action<double> _setLast;

    public VolumeMemory(Func<double> getLast, Action<double> setLast)
    {
        _getLast = getLast;
        _setLast = setLast;
    }

    public double Of(string path)
    {
        if (_byFile.TryGetValue(path, out var v)) return v;
        // The first time a file is seen it takes the last level and keeps it from then on.
        v = Math.Clamp(_getLast(), 0, 1);
        _byFile[path] = v;
        return v;
    }

    /// <summary>True if the value changed.</summary>
    public bool Set(string path, double volume)
    {
        var v = Math.Round(Math.Clamp(volume, 0, 1), 3);
        var changed = !_byFile.TryGetValue(path, out var old) || Math.Abs(old - v) > 0.0005;
        _byFile[path] = v;
        _setLast(v);
        return changed;
    }
}

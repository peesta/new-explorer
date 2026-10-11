using System.IO;
using System.Text.Json;
using System.Windows.Threading;

namespace CanvasExplorer.Services;

/// <summary>A folder in the sidebar's "Избранное" section.</summary>
public sealed class Favorite
{
    public string Path { get; set; } = "";
    /// <summary>Shown instead of the folder name; null = the folder's own name.</summary>
    public string? Name { get; set; }
}

/// <summary>Global preferences, stored in %AppData%\CanvasExplorer\settings.json.</summary>
public sealed class AppSettings
{
    public double SidebarWidth { get; set; } = 196;
    public int Density { get; set; } = 1;
    /// <summary>Folders shown as a grid of tiles; every other folder is a list. Keys are full paths.</summary>
    public List<string> GridFolders { get; set; } = new();
    /// <summary>Sidebar favorites in their order; null = the standard set (never edited yet).</summary>
    public List<Favorite>? Favorites { get; set; }
    /// <summary>Volume of the audio player (0…1); the last level set is used for every new player.</summary>
    public double AudioVolume { get; set; } = 0.8;
    public string? LastFolder { get; set; }
    public List<string>? Tabs { get; set; }
    public int ActiveTab { get; set; }
    public double WinLeft { get; set; } = double.NaN;
    public double WinTop { get; set; } = double.NaN;
    public double WinWidth { get; set; } = 1280;
    public double WinHeight { get; set; } = 800;
    public bool Maximized { get; set; }

    /// <summary>Overrides both data folders (settings and fallback layouts); used by the UI tests.</summary>
    public const string DataDirVariable = "CANVAS_EXPLORER_DATA";

    static readonly string? DataOverride = Environment.GetEnvironmentVariable(DataDirVariable) is { Length: > 0 } d ? d : null;

    /// <summary>Folder for roaming data: settings, tags (%AppData%\CanvasExplorer unless overridden).</summary>
    public static readonly string DataDir = DataOverride ?? Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData), "CanvasExplorer");

    /// <summary>Folder for machine-local data (%LocalAppData%\CanvasExplorer unless overridden).</summary>
    public static string LocalDir => DataOverride ?? Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "CanvasExplorer");
    static readonly string FilePath = Path.Combine(DataDir, "settings.json");
    static readonly JsonSerializerOptions Json = new() { WriteIndented = true, NumberHandling = System.Text.Json.Serialization.JsonNumberHandling.AllowNamedFloatingPointLiterals };

    public static AppSettings Current { get; } = Load();

    static AppSettings Load()
    {
        try
        {
            if (File.Exists(FilePath))
                return JsonSerializer.Deserialize<AppSettings>(File.ReadAllText(FilePath), Json) ?? new AppSettings();
        }
        catch { }
        return new AppSettings();
    }

    public bool IsGrid(string? folder) =>
        folder != null && GridFolders.Any(f => string.Equals(f, folder, StringComparison.OrdinalIgnoreCase));

    public void SetGrid(string folder, bool grid)
    {
        GridFolders.RemoveAll(f => string.Equals(f, folder, StringComparison.OrdinalIgnoreCase));
        if (grid) GridFolders.Add(folder);
        ScheduleSave();
    }

    public void Save()
    {
        try
        {
            Directory.CreateDirectory(DataDir);
            File.WriteAllText(FilePath, JsonSerializer.Serialize(this, Json));
        }
        catch { }
    }

    static DispatcherTimer? _saveTimer;

    /// <summary>Writes the settings shortly (debounced); call from the UI thread.</summary>
    public static void ScheduleSave()
    {
        if (_saveTimer == null || _saveTimer.Dispatcher != Dispatcher.CurrentDispatcher)
        {
            _saveTimer = new DispatcherTimer { Interval = TimeSpan.FromMilliseconds(600) };
            _saveTimer.Tick += (_, _) => { _saveTimer.Stop(); Current.Save(); };
        }
        _saveTimer.Stop();
        _saveTimer.Start();
    }
}

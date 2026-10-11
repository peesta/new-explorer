using System.IO;
using System.Text.Json;

namespace CanvasExplorer.Services;

/// <summary>Global preferences, stored in %AppData%\CanvasExplorer\settings.json.</summary>
public sealed class AppSettings
{
    public double SidebarWidth { get; set; } = 196;
    public int Density { get; set; } = 1;
    public bool GridView { get; set; }
    public string? LastFolder { get; set; }
    public List<string>? Tabs { get; set; }
    public int ActiveTab { get; set; }
    public double WinLeft { get; set; } = double.NaN;
    public double WinTop { get; set; } = double.NaN;
    public double WinWidth { get; set; } = 1280;
    public double WinHeight { get; set; } = 800;
    public bool Maximized { get; set; }

    static readonly string Dir = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData), "CanvasExplorer");
    static readonly string FilePath = Path.Combine(Dir, "settings.json");
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

    public void Save()
    {
        try
        {
            Directory.CreateDirectory(Dir);
            File.WriteAllText(FilePath, JsonSerializer.Serialize(this, Json));
        }
        catch { }
    }
}

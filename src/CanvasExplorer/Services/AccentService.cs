using System.Windows;
using System.Windows.Media;
using global::Windows.UI.ViewManagement;

namespace CanvasExplorer.Services;

/// <summary>Takes the accent colour from Windows settings and keeps it in sync.</summary>
public static class AccentService
{
    static UISettings? _settings;

    public static void Init()
    {
        try
        {
            _settings = new UISettings();
            Apply();
            _settings.ColorValuesChanged += (_, _) => Application.Current?.Dispatcher.BeginInvoke(Apply);
        }
        catch
        {
            // Keep the default accent from the theme.
        }
    }

    static void Apply()
    {
        if (_settings == null) return;
        // In dark mode Windows itself uses the lighter accent variant for text and controls.
        var c = _settings.GetColorValue(UIColorType.AccentLight2);
        var color = Color.FromRgb(c.R, c.G, c.B);
        var res = Application.Current.Resources;
        res["AccentBrush"] = Frozen(new SolidColorBrush(color));
        res["AccentSoftBrush"] = Frozen(new SolidColorBrush(Color.FromArgb(0x29, color.R, color.G, color.B)));
    }

    static Brush Frozen(Brush b)
    {
        b.Freeze();
        return b;
    }
}

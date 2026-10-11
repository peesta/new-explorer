using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;

namespace CanvasExplorer.UI;

static class Menus
{
    public static MenuItem Item(string header, Geometry? icon, Action onClick, string? gesture = null,
        bool enabled = true, bool danger = false)
    {
        var mi = new MenuItem
        {
            Header = header,
            InputGestureText = gesture ?? "",
            IsEnabled = enabled,
            Icon = icon == null ? null : new Icon(icon, 13),
        };
        if (danger) mi.SetResourceReference(Control.ForegroundProperty, "DangerBrush");
        mi.Click += (_, _) => onClick();
        return mi;
    }

    /// <summary>An item with a check mark on the right (e.g. a tag the file has).</summary>
    public static MenuItem Check(string header, object? icon, bool isChecked, Action onClick)
    {
        var mi = new MenuItem { Header = header, Icon = icon, IsCheckable = true, IsChecked = isChecked, StaysOpenOnClick = false };
        mi.Click += (_, _) => onClick();
        return mi;
    }

    public static Separator Sep() => new();

    public static MenuItem Sub(string header, Geometry? icon, params object?[] items)
    {
        var mi = new MenuItem { Header = header, Icon = icon == null ? null : new Icon(icon, 13) };
        foreach (var it in items)
            if (it != null) mi.Items.Add(it);
        return mi;
    }

    public static ContextMenu Build(params object?[] items)
    {
        var menu = new ContextMenu();
        foreach (var it in items)
            if (it != null) menu.Items.Add(it);
        return menu;
    }
}

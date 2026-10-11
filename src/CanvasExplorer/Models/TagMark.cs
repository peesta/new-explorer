using System.Windows;
using System.Windows.Media;
using CanvasExplorer.Services;
using CanvasExplorer.UI;

namespace CanvasExplorer.Models;

/// <summary>How a tag is drawn next to a file name: a colour dot or a small icon.</summary>
public sealed class TagMark
{
    /// <summary>Tag colours, as in the mockup.</summary>
    public static readonly (string Label, string Hex)[] Palette =
    {
        ("Красный", "#E5484D"), ("Оранжевый", "#F08C00"), ("Жёлтый", "#D9A400"), ("Зелёный", "#2F9E6E"),
        ("Бирюзовый", "#0099BC"), ("Синий", "#0078D4"), ("Фиолетовый", "#8764B8"), ("Розовый", "#D6409F"), ("Серый", "#7A7A7A"),
    };

    static readonly Dictionary<string, Brush> Brushes = new(StringComparer.OrdinalIgnoreCase);

    public static Brush BrushOf(string? hex)
    {
        hex ??= Palette[8].Hex;
        lock (Brushes)
        {
            if (Brushes.TryGetValue(hex, out var b)) return b;
            try { b = new SolidColorBrush((Color)ColorConverter.ConvertFromString(hex)); }
            catch { b = new SolidColorBrush(Colors.Gray); }
            b.Freeze();
            return Brushes[hex] = b;
        }
    }

    TagMark(TagDef tag)
    {
        Id = tag.Id;
        Name = tag.Name;
        Icon = Icons.TagIcon(tag.Icon);
        Dot = Icon == null ? BrushOf(tag.Color) : null;
    }

    public static TagMark From(TagDef tag) => new(tag);

    public static IReadOnlyList<TagMark> For(string path) =>
        TagStore.Current.TagsOf(path).Select(From).ToList();

    public string Id { get; }
    public string Name { get; }
    public Brush? Dot { get; }
    public Geometry? Icon { get; }
    public Visibility DotVisibility => Dot != null ? Visibility.Visible : Visibility.Collapsed;
    public Visibility IconVisibility => Icon != null ? Visibility.Visible : Visibility.Collapsed;

    /// <summary>A standalone mark (sidebar, menus, dialogs): dot of <paramref name="dot"/> px or icon of <paramref name="icon"/> px.</summary>
    public static FrameworkElement Visual(TagDef tag, double dot = 10, double icon = 14)
    {
        if (Icons.TagIcon(tag.Icon) is { } geometry)
        {
            var ic = new UI.Icon(geometry, icon, 1.9) { VerticalAlignment = VerticalAlignment.Center, HorizontalAlignment = HorizontalAlignment.Center };
            ic.SetResourceReference(UI.Icon.ForegroundProperty, "TagIconBrush");
            return ic;
        }
        return new System.Windows.Shapes.Ellipse
        {
            Width = dot,
            Height = dot,
            Fill = BrushOf(tag.Color),
            VerticalAlignment = VerticalAlignment.Center,
            HorizontalAlignment = HorizontalAlignment.Center,
        };
    }
}

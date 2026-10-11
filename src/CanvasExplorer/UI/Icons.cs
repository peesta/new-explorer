using System.Windows.Media;

namespace CanvasExplorer.UI;

/// <summary>Icon path data in a 24×24 box (same shapes as in the design mockups).</summary>
public static class Icons
{
    public static readonly Geometry Back = G("M15 18l-6-6 6-6");
    public static readonly Geometry Forward = G("M9 6l6 6-6 6");
    public static readonly Geometry Up = G("M12 19V5M6 11l6-6 6 6");
    public static readonly Geometry Refresh = G("M20 11a8 8 0 1 0-2.3 5.7M20 4v7h-7");
    public static readonly Geometry Chevron = G("M9 6l6 6-6 6");
    public static readonly Geometry Search = G("M11 4a7 7 0 1 0 0 14a7 7 0 1 0 0-14zM20 20l-4-4");
    public static readonly Geometry Minus = G("M5 12h14");
    public static readonly Geometry Plus = G("M12 5v14M5 12h14");
    public static readonly Geometry Close = G("M6 6l12 12M18 6L6 18");
    public static readonly Geometry Maximize = G("M6 6h12v12H6z");
    public static readonly Geometry Restore = G("M8 8h11v11H8zM5 16V5h11");
    public static readonly Geometry Pin = G("M12 17v5M8 3h8l-1 6 3 3v2H6v-2l3-3z");
    public static readonly Geometry Unpin = G("M12 17v5M8 3h8l-1 6 3 3v2H6v-2l3-3zM4 4l16 16");
    public static readonly Geometry Folder = G("M3 6h6l2 2h10v11H3z");
    public static readonly Geometry FolderPlus = G("M3 6h6l2 2h10v11H3zM12 11v5M9.5 13.5h5");
    public static readonly Geometry Clock = G("M12 3a9 9 0 1 0 0 18a9 9 0 1 0 0-18zM12 7v5l3 2");
    public static readonly Geometry Desktop = G("M3 4h18v12H3zM8 20h8M12 16v4");
    public static readonly Geometry Download = G("M12 3v12M7 10l5 5 5-5M4 20h16");
    public static readonly Geometry Doc = G("M6 3h8l4 4v14H6zM14 3v4h4");
    public static readonly Geometry Image = G("M3 5h18v14H3zM3 16l5-5 4 4 3-3 6 6M15 8h.01");
    public static readonly Geometry Music = G("M9 18V5l11-2v13M6 15a3 3 0 1 0 0 6a3 3 0 1 0 0-6zM17 13a3 3 0 1 0 0 6a3 3 0 1 0 0-6z");
    public static readonly Geometry Video = G("M3 6h13v12H3zM16 10l5-3v10l-5-3");
    public static readonly Geometry Home = G("M3 11l9-7 9 7v9H3zM9 20v-6h6v6");
    public static readonly Geometry Drive = G("M3 13h18v6H3zM5 13l2-8h10l2 8M7 16h.01");
    public static readonly Geometry Open = G("M14 4h6v6M20 4l-9 9M18 14v6H4V6h6");
    public static readonly Geometry OpenWith = G("M4 6h16v12H4zM4 10h16");
    public static readonly Geometry Cut = G("M6 9a3 3 0 1 0 0-6a3 3 0 1 0 0 6zM6 21a3 3 0 1 0 0-6a3 3 0 1 0 0 6zM8.1 7.9L20 20M8.1 16.1L20 4");
    public static readonly Geometry Copy = G("M9 9h11v11H9zM5 15H4V4h11v1");
    public static readonly Geometry Paste = G("M8 4h8v4H8zM6 6H4v15h16V6h-2");
    public static readonly Geometry Rename = G("M4 20h4L19 9l-4-4L4 16zM14 6l4 4");
    public static readonly Geometry Trash = G("M4 7h16M10 11v6M14 11v6M6 7l1 13h10l1-13M9 7V4h6v3");
    public static readonly Geometry Info = G("M12 3a9 9 0 1 0 0 18a9 9 0 1 0 0-18zM12 11v6M12 7.5h.01");
    public static readonly Geometry Path = G("M4 7h16M4 12h10M4 17h7");
    public static readonly Geometry Canvas = G("M3 5h18v14H3zM6 8h6v5H6zM14 11h4v5h-4z");
    public static readonly Geometry List = G("M8 6h12M8 12h12M8 18h12M4 6h.01M4 12h.01M4 18h.01");
    public static readonly Geometry Grid = G("M4 4h7v7H4zM13 4h7v7h-7zM4 13h7v7H4zM13 13h7v7h-7z");
    public static readonly Geometry DensityLarge = G("M4 5h16v6H4zM4 13h16v6H4z");
    public static readonly Geometry DensityMedium = G("M4 4h16v4H4zM4 10h16v4H4zM4 16h16v4H4z");
    public static readonly Geometry DensitySmall = G("M4 5h16M4 9.5h16M4 14h16M4 18.5h16");
    public static readonly Geometry ZoomIn = G("M11 4a7 7 0 1 0 0 14a7 7 0 1 0 0-14zM20 20l-4-4M11 8v6M8 11h6");
    public static readonly Geometry ZoomOut = G("M11 4a7 7 0 1 0 0 14a7 7 0 1 0 0-14zM20 20l-4-4M8 11h6");
    public static readonly Geometry Fit = G("M4 9V4h5M20 9V4h-5M4 15v5h5M20 15v5h-5");

    public static readonly Geometry Play = G("M8 4.5v15l12-7.5z");
    public static readonly Geometry Pause = G("M7 5h4v14H7zM13 5h4v14h-4z");
    public static readonly Geometry SkipBack = G("M6 5h2v14H6zM20 5v14L9 12z");
    public static readonly Geometry Loop = G("M17 2l4 4-4 4M3 11V9a3 3 0 0 1 3-3h15M7 22l-4-4 4-4M21 13v2a3 3 0 0 1-3 3H3");
    public static readonly Geometry Volume = G("M4 9h4l5-4v14l-5-4H4zM17 9a4 4 0 0 1 0 6");

    static Geometry G(string data)
    {
        var g = Geometry.Parse(data);
        g.Freeze();
        return g;
    }
}

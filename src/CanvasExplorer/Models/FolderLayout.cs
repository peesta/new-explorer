namespace CanvasExplorer.Models;

public sealed class FolderLayout
{
    public int Version { get; set; } = 1;
    /// <summary>The list was stretched back over the whole area: canvas hidden, pins kept for later.</summary>
    public bool Docked { get; set; }
    public ViewState View { get; set; } = new();
    public RectState List { get; set; } = new();
    public List<PinState> Items { get; set; } = new();
}

public sealed class ViewState
{
    public double X { get; set; }
    public double Y { get; set; }
    public double Zoom { get; set; } = 1;
}

public sealed class RectState
{
    public double X { get; set; }
    public double Y { get; set; }
    public double W { get; set; }
    public double H { get; set; }
}

public sealed class PinState
{
    public string Name { get; set; } = "";
    public double X { get; set; }
    public double Y { get; set; }
    public double W { get; set; }
    public double H { get; set; }
    public double Zoom { get; set; } = 1;
}

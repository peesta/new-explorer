using System.Windows;
using System.Windows.Controls;
using CanvasExplorer.Models;

namespace CanvasExplorer.Previews;

/// <summary>Body of a pinned preview window. One subclass per file type.</summary>
public abstract class PreviewContent : Border
{
    protected PreviewContent(FileItem item)
    {
        Item = item;
        ClipToBounds = true;
    }

    public FileItem Item { get; private set; }

    public virtual bool SupportsZoom => false;
    public virtual double Zoom => 1;
    public virtual void SetZoom(double zoom) { }
    public void ZoomBy(double factor) => SetZoom(Zoom * factor);

    public virtual bool IsDirty => false;

    /// <summary>Extra controls for the window header (e.g. preview/code switch).</summary>
    public virtual FrameworkElement? HeaderExtra => null;

    /// <summary>Default window size for a freshly pinned file.</summary>
    public virtual Size PreferredSize => new(320, 240);

    /// <summary>Raised when the content learns its natural size (e.g. image dimensions).</summary>
    public event EventHandler<Size>? NaturalSizeKnown;
    /// <summary>Zoom, dirty state or header content changed.</summary>
    public event EventHandler? StateChanged;

    protected void RaiseNaturalSize(Size s) => NaturalSizeKnown?.Invoke(this, s);
    protected void RaiseState() => StateChanged?.Invoke(this, EventArgs.Empty);

    /// <summary>Called when the window is removed or the folder changes: flush and release.</summary>
    public virtual void Unload() { }

    public virtual void OnRenamed(FileItem item) => Item = item;
}

using CanvasExplorer.Models;

namespace CanvasExplorer.UI;

/// <summary>What the list and the canvas need from the main window.</summary>
public interface IExplorerHost
{
    string? CurrentFolder { get; }
    IntPtr Hwnd { get; }
    bool HasCanvas { get; }
    void NavigateTo(string path);
    void OpenInNewTab(string path);
    void GoUp();
    void OpenItem(FileItem item);
    void PinItems(IEnumerable<FileItem> items);
    void CreateCanvas();
    void RemoveCanvas();
    void RefreshFolder();
    void RenameItem(FileItem item);
    void CreateFolder();
}

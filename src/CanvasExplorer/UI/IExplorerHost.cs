using CanvasExplorer.Models;

namespace CanvasExplorer.UI;

/// <summary>What the list and the canvas need from the main window.</summary>
public interface IExplorerHost
{
    /// <summary>The real folder shown, or null for a virtual view (files with a tag).</summary>
    string? CurrentFolder { get; }
    /// <summary>A virtual view: files gathered from many folders (no canvas, no paste or create).</summary>
    bool IsVirtualView { get; }
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
    void CreateFile(string baseName, string extension);
    /// <summary>The user switched list/grid: remember it for the shown folder only.</summary>
    void SetFolderGrid(bool grid);
    bool IsFavorite(string path);
    /// <summary>Opens the folder that holds <paramref name="path"/> and selects it there.</summary>
    void ShowInFolder(string path);
    void AddFavorite(string path);
    /// <summary>Opens the "new tag" popup; the new tag goes onto <paramref name="assignTo"/>.</summary>
    void NewTag(IReadOnlyList<string> assignTo);
}

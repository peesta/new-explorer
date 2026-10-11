using System.ComponentModel;
using System.IO;
using System.Runtime.CompilerServices;
using System.Windows;
using System.Windows.Media;
using CanvasExplorer.Services;

namespace CanvasExplorer.Models;

public sealed class FileItem : INotifyPropertyChanged
{
    public FileItem(FileSystemInfo info)
    {
        Name = info.Name;
        FullPath = info.FullName;
        IsFolder = info is DirectoryInfo;
        Modified = info.LastWriteTime;
        Size = info is FileInfo f ? f.Length : 0;

        var ext = IsFolder ? "" : Path.GetExtension(Name).TrimStart('.');
        Kind = IsFolder ? FileKind.Folder : FileTypes.KindOf(ext);
        Ext = IsFolder ? "" : ext.Length == 0 ? "—" : ext.Length > 4 ? ext[..4].ToUpperInvariant() : ext.ToUpperInvariant();
        (ChipBg, ChipFg) = FileTypes.ChipBrushes(Kind);
        KindText = FileTypes.KindName(Kind, ext.ToUpperInvariant());
        DateText = Format.ShortDate(Modified);
        SizeText = IsFolder ? "" : Format.Size(Size);
        Tip = Name + "\n" + KindText + (IsFolder ? "" : " · " + SizeText) + "\n" + Format.LongDate(Modified);
    }

    public string Name { get; }
    public string FullPath { get; }
    public bool IsFolder { get; }
    public DateTime Modified { get; }
    public long Size { get; }
    public string Ext { get; }
    public FileKind Kind { get; }
    public Brush ChipBg { get; }
    public Brush ChipFg { get; }
    public string KindText { get; }
    public string DateText { get; }
    public string SizeText { get; }
    public string Tip { get; }

    public Visibility ExtVisibility => IsFolder ? Visibility.Collapsed : Visibility.Visible;
    public Visibility FolderVisibility => IsFolder ? Visibility.Visible : Visibility.Collapsed;

    bool _pinned;
    public bool IsPinned
    {
        get => _pinned;
        set { if (_pinned != value) { _pinned = value; Changed(); } }
    }

    /// <summary>Shown by screen readers and UI automation for the list row.</summary>
    public override string ToString() => Name;

    public event PropertyChangedEventHandler? PropertyChanged;
    void Changed([CallerMemberName] string? name = null) => PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(name));
}

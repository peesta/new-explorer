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

    /// <summary>Images and videos show a picture instead of the type chip in the tile view.</summary>
    public bool HasThumbnail => Kind is FileKind.Image or FileKind.Video;
    public bool IsVideo => Kind == FileKind.Video;

    ImageSource? _thumb;
    public ImageSource? Thumb
    {
        get => _thumb;
        set
        {
            if (_thumb == value) return;
            _thumb = value;
            Changed();
            Changed(nameof(ThumbVisibility));
            Changed(nameof(TileChipVisibility));
        }
    }
    public Visibility ThumbVisibility => _thumb != null ? Visibility.Visible : Visibility.Collapsed;
    public Visibility TileChipVisibility => _thumb != null ? Visibility.Collapsed : Visibility.Visible;
    public Visibility VideoBadgeVisibility => _thumb != null && IsVideo ? Visibility.Visible : Visibility.Collapsed;

    IReadOnlyList<TagMark> _tags = Array.Empty<TagMark>();
    /// <summary>Tag dots and icons shown after the name.</summary>
    public IReadOnlyList<TagMark> Tags
    {
        get => _tags;
        set
        {
            if (SameTags(_tags, value)) return;
            _tags = value;
            Changed();
        }
    }

    static bool SameTags(IReadOnlyList<TagMark> a, IReadOnlyList<TagMark> b) =>
        a.Count == b.Count && a.Zip(b).All(p => p.First.Id == p.Second.Id && p.First.Name == p.Second.Name &&
                                                p.First.Dot == p.Second.Dot && p.First.Icon == p.Second.Icon);

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

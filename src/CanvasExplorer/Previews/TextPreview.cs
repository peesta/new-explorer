using System.IO;
using System.Text;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Threading;
using CanvasExplorer.Models;
using CanvasExplorer.Services;

namespace CanvasExplorer.Previews;

/// <summary>
/// Editable text file. Click and type; changes are saved automatically in the file's own encoding.
/// Edits made by other programs are picked up; a conflict with unsaved edits is asked about, never overwritten silently.
/// </summary>
public sealed class TextPreview : PreviewContent
{
    const double BaseFontSize = 12.5;

    readonly TextBox _box;
    readonly DispatcherTimer _saveTimer;
    readonly DispatcherTimer _checkTimer;
    Encoding _encoding = new UTF8Encoding(false);
    FileStamp _stamp;
    bool _loading = true;
    bool _resolving;
    bool _dirty;
    double _zoom = 1;

    public TextPreview(FileItem item) : base(item)
    {
        _box = new TextBox
        {
            AcceptsReturn = true,
            AcceptsTab = true,
            TextWrapping = TextWrapping.Wrap,
            VerticalScrollBarVisibility = ScrollBarVisibility.Auto,
            HorizontalScrollBarVisibility = ScrollBarVisibility.Disabled,
            FontSize = BaseFontSize,
            IsReadOnly = true,
        };
        _box.Style = (Style)Application.Current.FindResource("EditorBox");
        _box.TextChanged += (_, _) =>
        {
            if (_loading) return;
            SetDirty(true);
            _saveTimer!.Stop();
            _saveTimer.Start();
        };
        _box.PreviewMouseWheel += (_, e) =>
        {
            if (Keyboard.Modifiers != ModifierKeys.Control) return;
            ZoomBy(e.Delta > 0 ? 1.1 : 1 / 1.1);
            e.Handled = true;
        };
        _box.PreviewKeyDown += (_, e) =>
        {
            if (e.Key == Key.S && Keyboard.Modifiers == ModifierKeys.Control) { Save(); e.Handled = true; }
        };
        _box.LostKeyboardFocus += (_, _) => { if (_dirty) Save(); };
        _saveTimer = new DispatcherTimer { Interval = TimeSpan.FromMilliseconds(700) };
        _saveTimer.Tick += (_, _) => { _saveTimer.Stop(); Save(); };
        // File change notifications come in bursts; look once they settle.
        _checkTimer = new DispatcherTimer { Interval = TimeSpan.FromMilliseconds(300) };
        _checkTimer.Tick += (_, _) => { _checkTimer.Stop(); CheckDisk(); };
        Child = _box;
        _ = LoadAsync(reload: false);
    }

    public override bool SupportsZoom => true;
    public override double Zoom => _zoom;
    public override bool IsDirty => _dirty;
    public override Size PreferredSize => new(320, 280);

    public override void SetZoom(double zoom)
    {
        _zoom = Math.Clamp(zoom, 0.6, 3);
        _box.FontSize = BaseFontSize * _zoom;
        RaiseState();
    }

    async Task LoadAsync(bool reload)
    {
        _loading = true;
        try
        {
            var path = Item.FullPath;
            var (text, encoding, truncated, stamp) = await Task.Run(() =>
            {
                var s = FileStamp.Of(path);
                var (t, enc, cut) = TextFile.Read(path);
                return (t, enc, cut, s);
            });
            _encoding = encoding;
            _stamp = stamp;
            // A reload keeps the caret and the scroll position where the reader left them.
            var caret = _box.CaretIndex;
            var offset = _box.VerticalOffset;
            _box.Text = text;
            if (reload)
            {
                _box.CaretIndex = Math.Min(caret, text.Length);
                _box.ScrollToVerticalOffset(offset);
            }
            _box.IsReadOnly = truncated;
            _box.ToolTip = truncated ? "Файл слишком большой: показано начало, редактирование отключено" : null;
        }
        catch (Exception ex)
        {
            if (!reload)
            {
                _box.Text = "Не удалось открыть файл: " + ex.Message;
                _box.IsReadOnly = true;
            }
        }
        finally
        {
            _loading = false;
        }
    }

    public override void OnFileChanged()
    {
        _checkTimer.Stop();
        _checkTimer.Start();
    }

    /// <summary>Re-reads the file if another program changed it; with unsaved edits the save asks first.</summary>
    void CheckDisk()
    {
        if (_loading || _resolving) return;
        var now = FileStamp.Of(Item.FullPath);
        if (!now.Exists || now == _stamp) return;
        if (_dirty) Save();
        else _ = LoadAsync(reload: true);
    }

    void SetDirty(bool value)
    {
        if (_dirty == value) return;
        _dirty = value;
        RaiseState();
    }

    void Save()
    {
        if (!_dirty || _box.IsReadOnly || _loading || _resolving) return;
        _saveTimer.Stop();
        var path = Item.FullPath;
        var now = FileStamp.Of(path);
        // Deleted meanwhile: writing would bring the file back.
        if (!now.Exists) return;
        if (now != _stamp)
        {
            _resolving = true;
            var keep = FileStamp.AskKeepEdits(Item.Name);
            _resolving = false;
            if (!keep)
            {
                SetDirty(false);
                _ = LoadAsync(reload: true);
                return;
            }
        }
        try
        {
            TextFile.Write(path, _box.Text, _encoding);
            _stamp = FileStamp.Of(path);
            _box.ToolTip = null;
            SetDirty(false);
        }
        catch (Exception ex)
        {
            _box.ToolTip = "Не удалось сохранить: " + ex.Message;
        }
    }

    public override void Unload()
    {
        _checkTimer.Stop();
        _saveTimer.Stop();
        Save();
    }

    public void BeginEditing()
    {
        _box.Focus();
    }
}

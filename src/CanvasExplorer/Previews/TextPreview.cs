using System.IO;
using System.Text;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Threading;
using CanvasExplorer.Models;
using CanvasExplorer.Services;

namespace CanvasExplorer.Previews;

/// <summary>Editable text file. Click and type; changes are saved automatically in the file's own encoding.</summary>
public sealed class TextPreview : PreviewContent
{
    const double BaseFontSize = 12.5;

    readonly TextBox _box;
    readonly DispatcherTimer _saveTimer;
    Encoding _encoding = new UTF8Encoding(false);
    bool _loading = true;
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
        Child = _box;
        _ = LoadAsync();
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

    async Task LoadAsync()
    {
        try
        {
            var path = Item.FullPath;
            var (text, encoding, truncated) = await Task.Run(() => TextFile.Read(path));
            _encoding = encoding;
            _box.Text = text;
            _box.IsReadOnly = truncated;
            if (truncated) _box.ToolTip = "Файл слишком большой: показано начало, редактирование отключено";
        }
        catch (Exception ex)
        {
            _box.Text = "Не удалось открыть файл: " + ex.Message;
        }
        finally
        {
            _loading = false;
        }
    }

    void SetDirty(bool value)
    {
        if (_dirty == value) return;
        _dirty = value;
        RaiseState();
    }

    void Save()
    {
        if (!_dirty || _box.IsReadOnly) return;
        try
        {
            TextFile.Write(Item.FullPath, _box.Text, _encoding);
            SetDirty(false);
        }
        catch (Exception ex)
        {
            _box.ToolTip = "Не удалось сохранить: " + ex.Message;
        }
    }

    public override void Unload()
    {
        _saveTimer.Stop();
        Save();
    }

    public void BeginEditing()
    {
        _box.Focus();
    }
}

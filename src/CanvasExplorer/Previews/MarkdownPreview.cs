using System.Text;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Documents;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Threading;
using CanvasExplorer.Models;
using CanvasExplorer.Services;

namespace CanvasExplorer.Previews;

/// <summary>
/// Markdown with two modes: rendered preview and source code. Both are editable;
/// changes are saved automatically.
/// </summary>
public sealed class MarkdownPreview : PreviewContent
{
    readonly Grid _host = new();
    readonly RichTextBox _rich;
    readonly TextBox _code;
    readonly RadioButton _previewBtn;
    readonly RadioButton _codeBtn;
    readonly FrameworkElement _modeBar;
    readonly DispatcherTimer _saveTimer;
    readonly DispatcherTimer _checkTimer;
    readonly ScaleTransform _zoomTransform = new(1, 1);

    string _markdown = "";
    string _newline = "\n";
    Encoding _encoding = new UTF8Encoding(false);
    FileStamp _stamp;
    bool _loading = true;
    bool _resolving;
    bool _building;
    bool _previewMode = true;
    bool _richChanged;
    bool _dirty;
    bool _readOnly;
    double _zoom = 1;

    public MarkdownPreview(FileItem item) : base(item)
    {
        _rich = new RichTextBox
        {
            IsDocumentEnabled = true,
            AcceptsTab = true,
            Style = (Style)Application.Current.FindResource("RichEditorBox"),
        };
        _rich.TextChanged += (_, _) =>
        {
            if (_building || _loading) return;
            _richChanged = true;
            Changed();
        };
        _rich.PreviewKeyDown += OnRichKey;

        _code = new TextBox
        {
            AcceptsReturn = true,
            AcceptsTab = true,
            TextWrapping = TextWrapping.Wrap,
            VerticalScrollBarVisibility = ScrollBarVisibility.Auto,
            FontSize = 12.5,
            Visibility = Visibility.Collapsed,
            Style = (Style)Application.Current.FindResource("EditorBox"),
        };
        _code.TextChanged += (_, _) =>
        {
            if (_building || _loading) return;
            _markdown = _code.Text;
            Changed();
        };

        foreach (var box in new Control[] { _rich, _code })
        {
            box.PreviewMouseWheel += (_, e) =>
            {
                if (Keyboard.Modifiers != ModifierKeys.Control) return;
                ZoomBy(e.Delta > 0 ? 1.1 : 1 / 1.1);
                e.Handled = true;
            };
            box.PreviewKeyDown += (_, e) =>
            {
                if (e.Key == Key.S && Keyboard.Modifiers == ModifierKeys.Control) { Save(); e.Handled = true; }
            };
            box.LostKeyboardFocus += (_, _) => { if (_dirty) Save(); };
        }

        _host.LayoutTransform = _zoomTransform;
        _host.Children.Add(_rich);
        _host.Children.Add(_code);
        Child = _host;

        var group = "md-" + Guid.NewGuid().ToString("N");
        _previewBtn = new RadioButton { Content = "Превью", GroupName = group, IsChecked = true, Style = (Style)Application.Current.FindResource("SegTextButton") };
        _codeBtn = new RadioButton { Content = "Код", GroupName = group, Style = (Style)Application.Current.FindResource("SegTextButton") };
        _previewBtn.Checked += (_, _) => SetMode(true);
        _codeBtn.Checked += (_, _) => SetMode(false);
        var bar = new StackPanel { Orientation = Orientation.Horizontal };
        bar.Children.Add(_previewBtn);
        bar.Children.Add(_codeBtn);
        var barBorder = new Border { CornerRadius = new CornerRadius(4), Padding = new Thickness(2), Child = bar };
        barBorder.SetResourceReference(Border.BackgroundProperty, "PressedBrush");
        _modeBar = barBorder;

        _saveTimer = new DispatcherTimer { Interval = TimeSpan.FromMilliseconds(800) };
        _saveTimer.Tick += (_, _) => { _saveTimer.Stop(); Save(); };
        _checkTimer = new DispatcherTimer { Interval = TimeSpan.FromMilliseconds(300) };
        _checkTimer.Tick += (_, _) => { _checkTimer.Stop(); CheckDisk(); };
        _ = LoadAsync(reload: false);
    }

    public override bool SupportsZoom => true;
    public override double Zoom => _zoom;
    public override bool IsDirty => _dirty;
    public override FrameworkElement? HeaderExtra => _modeBar;
    public override Size PreferredSize => new(340, 320);

    public override void SetZoom(double zoom)
    {
        _zoom = Math.Clamp(zoom, 0.6, 3);
        _zoomTransform.ScaleX = _zoomTransform.ScaleY = _zoom;
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
            _markdown = text;
            _newline = text.Contains("\r\n") ? "\r\n" : "\n";
            _readOnly = truncated;
            _rich.IsReadOnly = _code.IsReadOnly = truncated;
            if (_previewMode)
            {
                // A reload keeps the reader's place.
                var offset = _rich.VerticalOffset;
                BuildPreview();
                if (reload) _rich.ScrollToVerticalOffset(offset);
            }
            else
            {
                var caret = _code.CaretIndex;
                _building = true;
                _code.Text = text;
                _building = false;
                _code.CaretIndex = Math.Min(caret, text.Length);
            }
        }
        catch (Exception ex)
        {
            if (!reload)
            {
                _markdown = "Не удалось открыть файл: " + ex.Message;
                _readOnly = true;
                _rich.IsReadOnly = _code.IsReadOnly = true;
                BuildPreview();
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

    void BuildPreview()
    {
        _building = true;
        try { _rich.Document = MarkdownFlow.Render(_markdown, OnTaskToggled); }
        catch { _rich.Document = new FlowDocument(new Paragraph(new Run(_markdown))); }
        _building = false;
        _richChanged = false;
    }

    void OnTaskToggled()
    {
        if (_readOnly) return;
        _richChanged = true;
        Changed();
    }

    string CurrentMarkdown()
    {
        if (_previewMode && _richChanged)
        {
            _markdown = MarkdownFlow.Serialize(_rich.Document, _newline);
            _richChanged = false;
        }
        return _markdown;
    }

    void SetMode(bool preview)
    {
        if (preview == _previewMode) return;
        if (preview)
        {
            _markdown = _code.Text;
            _previewMode = true;
            BuildPreview();
        }
        else
        {
            var md = CurrentMarkdown();
            _previewMode = false;
            _building = true;
            _code.Text = md;
            _building = false;
        }
        _rich.Visibility = preview ? Visibility.Visible : Visibility.Collapsed;
        _code.Visibility = preview ? Visibility.Collapsed : Visibility.Visible;
        RaiseState();
    }

    void Changed()
    {
        if (_readOnly) return;
        if (!_dirty)
        {
            _dirty = true;
            RaiseState();
        }
        _saveTimer.Stop();
        _saveTimer.Start();
    }

    void Save()
    {
        if (!_dirty || _readOnly || _loading || _resolving) return;
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
                _dirty = false;
                RaiseState();
                _ = LoadAsync(reload: true);
                return;
            }
        }
        try
        {
            TextFile.Write(path, CurrentMarkdown(), _encoding);
            _stamp = FileStamp.Of(path);
            ToolTip = null;
            _dirty = false;
            RaiseState();
        }
        catch (Exception ex)
        {
            ToolTip = "Не удалось сохранить: " + ex.Message;
        }
    }

    // Keeps the rendered structure sane while typing in the preview.
    void OnRichKey(object sender, KeyEventArgs e)
    {
        if (e.Key != Key.Enter || Keyboard.Modifiers != ModifierKeys.None) return;
        var caret = _rich.CaretPosition;
        var para = caret.Paragraph;
        var tag = para?.Tag as string ?? "";

        if (tag.StartsWith("code:") || tag == "raw")
        {
            // New line inside a code block, not a new block.
            var pos = caret.InsertLineBreak();
            _rich.CaretPosition = pos;
            e.Handled = true;
            return;
        }

        if (para != null && tag.Length == 2 && tag[0] == 'h' && caret.GetOffsetToPosition(para.ContentEnd) <= 1)
        {
            // Enter at the end of a heading starts a normal paragraph.
            var next = new Paragraph { Margin = new Thickness(0, 0, 0, 8) };
            if (para.Parent is FlowDocument doc) doc.Blocks.InsertAfter(para, next);
            else if (para.Parent is Section sec) sec.Blocks.InsertAfter(para, next);
            else return;
            _rich.CaretPosition = next.ContentStart;
            e.Handled = true;
            return;
        }

        if (para?.Parent is ListItem { Parent: List { Tag: "task" } })
            Dispatcher.InvokeAsync(EnsureTaskBoxes, DispatcherPriority.Background);
    }

    void EnsureTaskBoxes()
    {
        _building = true;
        foreach (var list in AllBlocks(_rich.Document.Blocks).OfType<List>().Where(l => l.Tag as string == "task"))
        {
            foreach (var li in list.ListItems)
            {
                if (li.Blocks.FirstBlock is not Paragraph p) continue;
                if (p.Inlines.FirstInline is InlineUIContainer { Child: CheckBox }) continue;
                var box = MarkdownFlow.MakeTaskBox(false, OnTaskToggled);
                if (p.Inlines.FirstInline is { } first) p.Inlines.InsertBefore(first, box);
                else p.Inlines.Add(box);
                if (li.IsKeyboardFocusWithin || _rich.CaretPosition.Paragraph == p)
                    _rich.CaretPosition = p.ContentEnd;
            }
        }
        _building = false;
        _richChanged = true;
        Changed();
    }

    static IEnumerable<System.Windows.Documents.Block> AllBlocks(BlockCollection blocks)
    {
        foreach (var b in blocks)
        {
            yield return b;
            IEnumerable<System.Windows.Documents.Block> inner = b switch
            {
                Section s => AllBlocks(s.Blocks),
                List l => l.ListItems.SelectMany(li => AllBlocks(li.Blocks)),
                _ => Enumerable.Empty<System.Windows.Documents.Block>(),
            };
            foreach (var i in inner) yield return i;
        }
    }

    public override void Unload()
    {
        _checkTimer.Stop();
        _saveTimer.Stop();
        Save();
    }
}

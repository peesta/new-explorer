using System.IO;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using CanvasExplorer.Models;
using WinPdf = global::Windows.Data.Pdf;
using WinStorage = global::Windows.Storage;
using WinStreams = global::Windows.Storage.Streams;

namespace CanvasExplorer.Previews;

/// <summary>PDF pages rendered by Windows (Windows.Data.Pdf), lazily as they scroll into view.</summary>
public sealed class PdfPreview : PreviewContent
{
    const double PageGap = 8;

    readonly ScrollViewer _scroll;
    readonly StackPanel _pages = new() { Margin = new Thickness(6) };
    readonly TextBlock _status;
    readonly TextBlock _pageLabel;
    readonly List<PageSlot> _slots = new();
    WinPdf.PdfDocument? _doc;
    double _zoom = 1;
    bool _rendering;
    bool _renderAgain;
    bool _unloaded;

    sealed class PageSlot
    {
        public required Border Frame;
        public required Image Image;
        public required double Aspect; // height / width
        public double RenderedWidth;
    }

    public PdfPreview(FileItem item) : base(item)
    {
        _status = new TextBlock { Text = "Загрузка…", HorizontalAlignment = HorizontalAlignment.Center, VerticalAlignment = VerticalAlignment.Center };
        _status.SetResourceReference(TextBlock.ForegroundProperty, "MutedBrush");
        _scroll = new ScrollViewer
        {
            Style = (Style)Application.Current.FindResource("OverlayScroll"),
            VerticalScrollBarVisibility = ScrollBarVisibility.Auto,
            HorizontalScrollBarVisibility = ScrollBarVisibility.Disabled,
            Content = _pages,
            Focusable = false,
        };
        _scroll.ScrollChanged += (_, _) => { UpdatePageLabel(); _ = RenderVisibleAsync(); };
        _scroll.SizeChanged += (_, _) => Relayout();
        _scroll.PreviewMouseWheel += (_, e) =>
        {
            if (Keyboard.Modifiers != ModifierKeys.Control) return;
            ZoomBy(e.Delta > 0 ? 1.15 : 1 / 1.15);
            e.Handled = true;
        };

        var grid = new Grid();
        grid.SetResourceReference(BackgroundProperty, "PressedBrush");
        grid.Children.Add(_scroll);
        grid.Children.Add(_status);
        Child = grid;
        CornerRadius = new CornerRadius(4);

        _pageLabel = new TextBlock { FontSize = 10.5, VerticalAlignment = VerticalAlignment.Center };
        _pageLabel.SetResourceReference(TextBlock.ForegroundProperty, "SubBrush");

        _ = LoadAsync();
    }

    public override bool SupportsZoom => true;
    public override double Zoom => _zoom;
    public override FrameworkElement? HeaderExtra => _pageLabel;
    public override Size PreferredSize => new(300, 390);

    public override void SetZoom(double zoom)
    {
        _zoom = Math.Clamp(zoom, 0.5, 4);
        _scroll.HorizontalScrollBarVisibility = _zoom > 1.01 ? ScrollBarVisibility.Auto : ScrollBarVisibility.Disabled;
        Relayout();
        RaiseState();
    }

    async Task LoadAsync()
    {
        try
        {
            var file = await WinStorage.StorageFile.GetFileFromPathAsync(Path.GetFullPath(Item.FullPath));
            _doc = await WinPdf.PdfDocument.LoadFromFileAsync(file);
            if (_unloaded) return;
            for (uint i = 0; i < _doc.PageCount; i++)
            {
                double aspect;
                using (var page = _doc.GetPage(i))
                    aspect = page.Size.Width > 0 ? page.Size.Height / page.Size.Width : 1.414;
                var img = new Image { Stretch = Stretch.Fill };
                RenderOptions.SetBitmapScalingMode(img, BitmapScalingMode.HighQuality);
                var frame = new Border
                {
                    Background = Brushes.White,
                    Margin = new Thickness(0, 0, 0, PageGap),
                    HorizontalAlignment = HorizontalAlignment.Center,
                    Child = img,
                };
                _pages.Children.Add(frame);
                _slots.Add(new PageSlot { Frame = frame, Image = img, Aspect = aspect });
            }
            _status.Visibility = Visibility.Collapsed;
            Relayout();
            UpdatePageLabel();
        }
        catch (Exception ex)
        {
            _status.Text = "Не удалось открыть PDF" + (ex is UnauthorizedAccessException ? " (защищён паролем?)" : "");
        }
    }

    double PageWidth => Math.Max(60, (_scroll.ViewportWidth > 0 ? _scroll.ViewportWidth : _scroll.ActualWidth) - 12) * _zoom;

    void Relayout()
    {
        if (_slots.Count == 0) return;
        var w = PageWidth;
        foreach (var s in _slots)
        {
            s.Frame.Width = w;
            s.Frame.Height = w * s.Aspect;
        }
        _ = RenderVisibleAsync();
    }

    void UpdatePageLabel()
    {
        if (_slots.Count == 0) return;
        var offset = _scroll.VerticalOffset + 6;
        var idx = 0;
        double y = 0;
        for (var i = 0; i < _slots.Count; i++)
        {
            var h = _slots[i].Frame.Height + PageGap;
            if (offset < y + h * 0.6) { idx = i; break; }
            y += h;
            idx = i;
        }
        _pageLabel.Text = (idx + 1) + " / " + _slots.Count;
    }

    async Task RenderVisibleAsync()
    {
        if (_doc == null || _unloaded) return;
        if (_rendering)
        {
            // Layout changed mid-render: run once more afterwards.
            _renderAgain = true;
            return;
        }
        _rendering = true;
        _renderAgain = false;
        try
        {
            var dpi = VisualTreeHelper.GetDpi(this).DpiScaleX;
            var top = _scroll.VerticalOffset - 200;
            var bottom = _scroll.VerticalOffset + _scroll.ViewportHeight + 400;
            double y = 0;
            for (var i = 0; i < _slots.Count; i++)
            {
                var s = _slots[i];
                var h = s.Frame.Height + PageGap;
                var visible = y + h > top && y < bottom;
                y += h;
                if (!visible) continue;
                var target = Math.Min(2400, Math.Round(s.Frame.Width * dpi));
                if (s.RenderedWidth > 0 && Math.Abs(s.RenderedWidth - target) / target < 0.2) continue;
                var bmp = await RenderPageAsync((uint)i, (uint)target);
                if (_unloaded) return;
                if (bmp != null)
                {
                    s.Image.Source = bmp;
                    s.RenderedWidth = target;
                }
            }
        }
        finally
        {
            _rendering = false;
        }
        if (_renderAgain) await RenderVisibleAsync();
    }

    async Task<BitmapSource?> RenderPageAsync(uint index, uint width)
    {
        if (_doc == null) return null;
        try
        {
            using var page = _doc.GetPage(index);
            using var stream = new WinStreams.InMemoryRandomAccessStream();
            await page.RenderToStreamAsync(stream, new WinPdf.PdfPageRenderOptions { DestinationWidth = width });
            stream.Seek(0);
            var bmp = new BitmapImage();
            bmp.BeginInit();
            bmp.CacheOption = BitmapCacheOption.OnLoad;
            bmp.StreamSource = stream.AsStream();
            bmp.EndInit();
            bmp.Freeze();
            return bmp;
        }
        catch
        {
            return null;
        }
    }

    public override void Unload()
    {
        _unloaded = true;
        foreach (var s in _slots) s.Image.Source = null;
        _doc = null;
    }
}

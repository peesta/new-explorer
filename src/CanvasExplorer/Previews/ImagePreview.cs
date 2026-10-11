using System.IO;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using CanvasExplorer.Models;

namespace CanvasExplorer.Previews;

/// <summary>Image with wheel zoom around the cursor and drag panning. 100% = fit to window.</summary>
public sealed class ImagePreview : PreviewContent
{
    const double MinZoom = 0.5, MaxZoom = 12;

    readonly Grid _surface;
    readonly Image _image = new() { Stretch = Stretch.Uniform };
    readonly ScaleTransform _scale = new();
    readonly TranslateTransform _move = new();
    readonly TextBlock _status;
    double _zoom = 1;
    Point? _panFrom;
    Point _panOrigin;

    public ImagePreview(FileItem item) : base(item)
    {
        RenderOptions.SetBitmapScalingMode(_image, BitmapScalingMode.HighQuality);
        _image.RenderTransform = new TransformGroup { Children = { _scale, _move } };
        _status = new TextBlock { Text = "Загрузка…", HorizontalAlignment = HorizontalAlignment.Center, VerticalAlignment = VerticalAlignment.Center };
        _status.SetResourceReference(TextBlock.ForegroundProperty, "MutedBrush");
        _surface = new Grid { Background = Brushes.Transparent, ClipToBounds = true };
        _surface.Children.Add(_image);
        _surface.Children.Add(_status);
        Child = _surface;
        CornerRadius = new CornerRadius(4);

        _surface.MouseWheel += OnWheel;
        _surface.MouseLeftButtonDown += OnDown;
        _surface.MouseMove += OnMove;
        _surface.MouseLeftButtonUp += (_, _) => { _panFrom = null; _surface.ReleaseMouseCapture(); };
        _ = LoadAsync();
    }

    public override bool SupportsZoom => true;
    public override double Zoom => _zoom;
    public override Size PreferredSize => new(360, 280);

    async Task LoadAsync()
    {
        try
        {
            var path = Item.FullPath;
            var bmp = await Task.Run(() => Decode(path));
            _image.Source = bmp;
            _status.Visibility = Visibility.Collapsed;
            RaiseNaturalSize(new Size(bmp.PixelWidth, bmp.PixelHeight));
        }
        catch
        {
            _status.Text = "Не удалось открыть изображение";
        }
    }

    static BitmapSource Decode(string path)
    {
        using var fs = new FileStream(path, FileMode.Open, FileAccess.Read, FileShare.ReadWrite | FileShare.Delete);
        var decoder = BitmapDecoder.Create(fs, BitmapCreateOptions.DelayCreation, BitmapCacheOption.None);
        var width = decoder.Frames[0].PixelWidth;
        fs.Position = 0;
        var bi = new BitmapImage();
        bi.BeginInit();
        bi.StreamSource = fs;
        bi.CacheOption = BitmapCacheOption.OnLoad;
        if (width > 2400) bi.DecodePixelWidth = 2400;
        bi.EndInit();
        bi.Freeze();
        return bi;
    }

    public override void SetZoom(double zoom) => ZoomAt(new Point(_surface.ActualWidth / 2, _surface.ActualHeight / 2), zoom);

    void ZoomAt(Point p, double target)
    {
        var nz = Math.Clamp(target, MinZoom, MaxZoom);
        if (Math.Abs(nz - 1) < 0.03) nz = 1;
        _move.X = p.X - (p.X - _move.X) * nz / _zoom;
        _move.Y = p.Y - (p.Y - _move.Y) * nz / _zoom;
        _zoom = nz;
        _scale.ScaleX = _scale.ScaleY = nz;
        if (nz == 1) { _move.X = 0; _move.Y = 0; }
        RaiseState();
    }

    void OnWheel(object sender, MouseWheelEventArgs e)
    {
        ZoomAt(e.GetPosition(_surface), _zoom * (e.Delta > 0 ? 1.15 : 1 / 1.15));
        e.Handled = true;
    }

    void OnDown(object sender, MouseButtonEventArgs e)
    {
        if (e.ClickCount == 2)
        {
            ZoomAt(default, 1);
            e.Handled = true;
            return;
        }
        if (_zoom <= 1) return;
        _panFrom = e.GetPosition(_surface);
        _panOrigin = new Point(_move.X, _move.Y);
        _surface.CaptureMouse();
        e.Handled = true;
    }

    void OnMove(object sender, MouseEventArgs e)
    {
        if (_panFrom is not { } from) return;
        var p = e.GetPosition(_surface);
        _move.X = _panOrigin.X + p.X - from.X;
        _move.Y = _panOrigin.Y + p.Y - from.Y;
    }

    public override void Unload() => _image.Source = null;
}

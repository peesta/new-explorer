using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Shapes;
using CanvasExplorer.Models;
using CanvasExplorer.Previews;
using CanvasExplorer.Services;

namespace CanvasExplorer.UI;

/// <summary>A pinned file on the canvas: header (drag handle) + type-specific body.</summary>
public sealed class PreviewWindow : FloatingFrame
{
    const double HeaderHeight = 28;

    readonly TextBlock _title;
    readonly TextBlock _zoomText;
    readonly StackPanel _zoomPanel;
    readonly Ellipse _dirtyDot;
    bool _fitToNaturalSize;

    public event EventHandler? CloseRequested;
    public event EventHandler? RevealRequested;

    public PreviewWindow(FileItem item, PreviewContent body, bool fitToNaturalSize)
    {
        Item = item;
        Body = body;
        _fitToNaturalSize = fitToNaturalSize;
        MinW = 160;
        MinH = 90;
        Focusable = true;
        FocusVisualStyle = null;

        var card = new Border { CornerRadius = new CornerRadius(6), BorderThickness = new Thickness(1) };
        card.SetResourceReference(Border.BackgroundProperty, "WidgetBrush");
        card.SetResourceReference(Border.BorderBrushProperty, "WidgetLineBrush");

        var grid = new Grid();
        grid.RowDefinitions.Add(new RowDefinition { Height = new GridLength(HeaderHeight) });
        grid.RowDefinitions.Add(new RowDefinition());

        var header = new DockPanel { Background = Brushes.Transparent, Margin = new Thickness(8, 0, 4, 0), Cursor = Cursors.SizeAll };
        SetIsDragHandle(header, true);

        var chip = new Border
        {
            CornerRadius = new CornerRadius(3),
            Padding = new Thickness(4, 1, 4, 1),
            Background = item.ChipBg,
            VerticalAlignment = VerticalAlignment.Center,
            Margin = new Thickness(0, 0, 6, 0),
            Child = new TextBlock { Text = item.Ext, FontSize = 8, FontWeight = FontWeights.Bold, Foreground = item.ChipFg },
        };
        DockPanel.SetDock(chip, Dock.Left);
        header.Children.Add(chip);

        var close = HeaderButton(Icons.Close, "Открепить с холста", () => CloseRequested?.Invoke(this, EventArgs.Empty));
        DockPanel.SetDock(close, Dock.Right);
        header.Children.Add(close);

        _zoomText = new TextBlock { FontSize = 10.5, Width = 34, TextAlignment = TextAlignment.Center, VerticalAlignment = VerticalAlignment.Center };
        _zoomText.SetResourceReference(TextBlock.ForegroundProperty, "SubBrush");
        _zoomPanel = new StackPanel { Orientation = Orientation.Horizontal, Visibility = Visibility.Collapsed };
        _zoomPanel.Children.Add(HeaderButton(Icons.Minus, "Уменьшить", () => Body.ZoomBy(1 / 1.2)));
        _zoomPanel.Children.Add(_zoomText);
        _zoomPanel.Children.Add(HeaderButton(Icons.Plus, "Увеличить", () => Body.ZoomBy(1.2)));
        DockPanel.SetDock(_zoomPanel, Dock.Right);
        header.Children.Add(_zoomPanel);

        if (body.HeaderExtra is { } extra)
        {
            extra.Margin = new Thickness(4, 0, 4, 0);
            extra.VerticalAlignment = VerticalAlignment.Center;
            DockPanel.SetDock(extra, Dock.Right);
            header.Children.Add(extra);
        }

        _dirtyDot = new Ellipse { Width = 6, Height = 6, Margin = new Thickness(6, 0, 6, 0), VerticalAlignment = VerticalAlignment.Center, Visibility = Visibility.Collapsed, ToolTip = "Сохраняется…" };
        _dirtyDot.SetResourceReference(Shape.FillProperty, "AccentBrush");
        DockPanel.SetDock(_dirtyDot, Dock.Right);
        header.Children.Add(_dirtyDot);

        _title = new TextBlock { Text = item.Name, FontSize = 12, FontWeight = FontWeights.SemiBold, VerticalAlignment = VerticalAlignment.Center, TextTrimming = TextTrimming.CharacterEllipsis, ToolTip = item.FullPath };
        _title.SetResourceReference(TextBlock.ForegroundProperty, "TextBrush");
        header.Children.Add(_title);

        header.ContextMenu = new ContextMenu();
        header.ContextMenuOpening += (_, _) => header.ContextMenu = BuildMenu();

        body.Margin = new Thickness(4, 0, 4, 4);
        Grid.SetRow(body, 1);
        grid.Children.Add(header);
        grid.Children.Add(body);
        card.Child = grid;
        Child = card;

        body.StateChanged += (_, _) => UpdateHeader();
        body.NaturalSizeKnown += OnNaturalSize;
        UpdateHeader();
    }

    public FileItem Item { get; private set; }
    public PreviewContent Body { get; }

    public void Rename(FileItem item)
    {
        Item = item;
        _title.Text = item.Name;
        _title.ToolTip = item.FullPath;
        Body.OnRenamed(item);
    }

    void OnNaturalSize(object? sender, Size px)
    {
        if (!_fitToNaturalSize || px.Width <= 0 || px.Height <= 0) return;
        _fitToNaturalSize = false;
        var b = Bounds;
        var w = b.Width;
        var h = Math.Clamp(w * px.Height / px.Width + HeaderHeight + 4, 140, 640);
        Bounds = new Rect(b.X, b.Y, w, h);
        BoundsChangedByContent?.Invoke(this, EventArgs.Empty);
    }

    public event EventHandler? BoundsChangedByContent;

    protected override void OnSelectedChanged() => UpdateHeader();

    void UpdateHeader()
    {
        _zoomPanel.Visibility = IsSelected && Body.SupportsZoom ? Visibility.Visible : Visibility.Collapsed;
        _zoomText.Text = Math.Round(Body.Zoom * 100) + "%";
        _dirtyDot.Visibility = Body.IsDirty ? Visibility.Visible : Visibility.Collapsed;
    }

    static Button HeaderButton(Geometry icon, string tip, Action click)
    {
        var b = new Button
        {
            Width = 20,
            Height = 20,
            ToolTip = tip,
            Content = new Icon(icon, 11, 2.6),
            Style = (Style)Application.Current.FindResource("IconButton"),
            VerticalAlignment = VerticalAlignment.Center,
        };
        b.SetResourceReference(Control.ForegroundProperty, "SubBrush");
        b.Click += (_, _) => click();
        return b;
    }

    ContextMenu BuildMenu()
    {
        var path = Item.FullPath;
        return Menus.Build(
            Menus.Item("Открыть", Icons.Open, () => Shell.Open(path), "Enter"),
            Menus.Item("Открыть с помощью…", Icons.OpenWith, () => Shell.OpenWith(path)),
            Menus.Item("Показать в списке", Icons.List, () => RevealRequested?.Invoke(this, EventArgs.Empty)),
            Menus.Sep(),
            Body.SupportsZoom ? Menus.Item("Увеличить", Icons.ZoomIn, () => Body.ZoomBy(1.2), "Ctrl+колесо") : null,
            Body.SupportsZoom ? Menus.Item("Уменьшить", Icons.ZoomOut, () => Body.ZoomBy(1 / 1.2)) : null,
            Body.SupportsZoom ? Menus.Item("Сбросить масштаб", Icons.Fit, () => Body.SetZoom(1)) : null,
            Body.SupportsZoom ? Menus.Sep() : null,
            Menus.Item("Копировать путь", Icons.Path, () => Clipboard.SetText(path)),
            Menus.Item("Свойства", Icons.Info, () => Shell.ShowProperties(path), "Alt+Enter"),
            Menus.Sep(),
            Menus.Item("Открепить с холста", Icons.Unpin, () => CloseRequested?.Invoke(this, EventArgs.Empty), "Del", danger: true));
    }
}

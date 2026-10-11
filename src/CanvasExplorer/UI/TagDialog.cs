using System.Windows;
using System.Windows.Automation;
using System.Windows.Controls;
using System.Windows.Controls.Primitives;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Shapes;
using CanvasExplorer.Models;
using CanvasExplorer.Services;
using UiIcon = CanvasExplorer.UI.Icon;

namespace CanvasExplorer.UI;

/// <summary>
/// "Новый тег" popup next to the sidebar (mockup «Тёмная · вариант A»): a name, then either a colour
/// (9 circles) or an icon (16 in a grid). Also used to edit an existing tag.
/// </summary>
public sealed class TagDialog : Window
{
    readonly TextBox _name;
    readonly Border _preview = new() { Width = 15, Height = 15 };
    readonly TextBlock _error;
    readonly Panel _colors;
    readonly Panel _icons;
    readonly RadioButton _colorMode;
    readonly RadioButton _iconMode;
    string _color = TagMark.Palette[6].Hex;
    string _icon = Icons.TagIcons[1].Key;
    readonly string? _editingId;
    readonly string _group = "tag" + Guid.NewGuid().ToString("N");

    TagDialog(TagDef? editing)
    {
        _editingId = editing?.Id;
        if (editing != null)
        {
            if (editing.Icon != null) _icon = editing.Icon;
            if (editing.Color != null) _color = editing.Color;
        }

        WindowStyle = WindowStyle.None;
        AllowsTransparency = true;
        Background = Brushes.Transparent;
        ResizeMode = ResizeMode.NoResize;
        SizeToContent = SizeToContent.WidthAndHeight;
        ShowInTaskbar = false;
        Title = editing == null ? "Новый тег" : "Изменить тег";
        SetResourceReference(FontFamilyProperty, "UiFont");
        FontSize = 12;

        _name = new TextBox { Text = editing?.Name ?? "", Height = 26, Padding = new Thickness(8, 0, 8, 0), VerticalContentAlignment = VerticalAlignment.Center };
        _name.Style = (Style)FindResource("FieldBox");
        AutomationProperties.SetAutomationId(_name, "TagName");
        AutomationProperties.SetName(_name, "Название тега");
        var hint = new TextBlock { Text = "Название тега", IsHitTestVisible = false, Margin = new Thickness(9, 0, 0, 0), VerticalAlignment = VerticalAlignment.Center };
        hint.SetResourceReference(TextBlock.ForegroundProperty, "FaintBrush");
        _name.TextChanged += (_, _) =>
        {
            hint.Visibility = _name.Text.Length == 0 ? Visibility.Visible : Visibility.Collapsed;
            _error!.Visibility = Visibility.Collapsed;
        };
        hint.Visibility = _name.Text.Length == 0 ? Visibility.Visible : Visibility.Collapsed;
        var field = new Border { CornerRadius = new CornerRadius(4), BorderThickness = new Thickness(1, 1, 1, 2), Child = new Grid { Children = { _name, hint } } };
        field.SetResourceReference(Border.BackgroundProperty, "FieldBrush");
        field.SetResourceReference(Border.BorderBrushProperty, "AccentBrush");
        var top = new DockPanel();
        _preview.Margin = new Thickness(0, 0, 8, 0);
        DockPanel.SetDock(_preview, Dock.Left);
        top.Children.Add(_preview);
        top.Children.Add(field);

        var group = "tagmode" + Guid.NewGuid().ToString("N");
        _colorMode = new RadioButton { Content = "Цвет", GroupName = group, Style = (Style)FindResource("SegTextButton"), HorizontalAlignment = HorizontalAlignment.Stretch };
        _iconMode = new RadioButton { Content = "Иконка", GroupName = group, Style = (Style)FindResource("SegTextButton"), HorizontalAlignment = HorizontalAlignment.Stretch };
        var modes = new UniformGrid { Columns = 2, Children = { _colorMode, _iconMode } };
        var modeBar = new Border { CornerRadius = new CornerRadius(4), Padding = new Thickness(2), Margin = new Thickness(0, 9, 0, 0), Child = modes };
        modeBar.SetResourceReference(Border.BackgroundProperty, "PressedBrush");

        _colors = new UniformGrid { Columns = TagMark.Palette.Length, Margin = new Thickness(0, 10, 0, 2) };
        foreach (var (label, hex) in TagMark.Palette) _colors.Children.Add(ColorButton(label, hex));
        _icons = new UniformGrid { Columns = 8, Margin = new Thickness(0, 9, 0, 0) };
        foreach (var (key, label, geometry) in Icons.TagIcons) _icons.Children.Add(IconButton(key, label, geometry));

        _error = new TextBlock { FontSize = 11.5, Margin = new Thickness(0, 8, 0, 0), TextWrapping = TextWrapping.Wrap, Visibility = Visibility.Collapsed };
        _error.SetResourceReference(TextBlock.ForegroundProperty, "DangerBrush");
        AutomationProperties.SetAutomationId(_error, "TagError");

        var cancel = new Button { Content = "Отмена", Height = 24, IsCancel = true, Style = (Style)FindResource("TextButton"), Padding = new Thickness(10, 0, 10, 0), Margin = new Thickness(0, 0, 4, 0) };
        cancel.SetResourceReference(ForegroundProperty, "TextBrush");
        cancel.SetResourceReference(BackgroundProperty, "HoverBrush");
        var ok = new Button { Content = editing == null ? "Создать" : "Сохранить", Height = 24, IsDefault = true, Style = (Style)FindResource("AccentButton"), Padding = new Thickness(10, 0, 10, 0) };
        cancel.Click += (_, _) => Finish(false);
        ok.Click += (_, _) => Accept();
        var buttons = new StackPanel { Orientation = Orientation.Horizontal, HorizontalAlignment = HorizontalAlignment.Right, Margin = new Thickness(0, 10, 0, 0), Children = { cancel, ok } };

        var panel = new StackPanel { Width = 236 - 22, Children = { top, modeBar, _colors, _icons, _error, buttons } };
        var card = new Border
        {
            CornerRadius = new CornerRadius(7),
            BorderThickness = new Thickness(1),
            Padding = new Thickness(10),
            Margin = new Thickness(16),
            Child = panel,
            Effect = new System.Windows.Media.Effects.DropShadowEffect { BlurRadius = 30, ShadowDepth = 8, Direction = 270, Opacity = 0.55 },
        };
        card.SetResourceReference(Border.BackgroundProperty, "MenuBrush");
        card.SetResourceReference(Border.BorderBrushProperty, "MenuLineBrush");
        Content = card;

        _colorMode.Checked += (_, _) => SetMode(icon: false);
        _iconMode.Checked += (_, _) => SetMode(icon: true);
        (editing?.Icon != null ? _iconMode : _colorMode).IsChecked = true;
        SetMode(editing?.Icon != null);

        Loaded += (_, _) => { _name.Focus(); _name.SelectAll(); };
        // Clicking elsewhere closes the popup like a menu.
        Deactivated += (_, _) => { if (_pending) Finish(false); };
        card.MouseLeftButtonDown += (_, e) => { if (e.ButtonState == MouseButtonState.Pressed) DragMove(); };
    }

    bool _pending = true;
    bool _iconModeOn;

    void Finish(bool ok)
    {
        if (!_pending) return;
        _pending = false;
        try { DialogResult = ok; }
        catch (InvalidOperationException) { Close(); }
    }

    public string TagName => _name.Text.Trim();
    public string? TagColor => _iconModeOn ? null : _color;
    public string? TagIcon => _iconModeOn ? _icon : null;

    void SetMode(bool icon)
    {
        _iconModeOn = icon;
        _colors.Visibility = icon ? Visibility.Collapsed : Visibility.Visible;
        _icons.Visibility = icon ? Visibility.Visible : Visibility.Collapsed;
        UpdatePreview();
    }

    void UpdatePreview()
    {
        _preview.Child = TagMark.Visual(new TagDef { Color = TagColor, Icon = TagIcon }, 10, 14);
        foreach (var b in _colors.Children.OfType<ToggleButton>()) b.IsChecked = (string)b.Tag == _color;
        foreach (var b in _icons.Children.OfType<ToggleButton>()) b.IsChecked = (string)b.Tag == _icon;
    }

    RadioButton ColorButton(string label, string hex)
    {
        var dot = new Ellipse { Width = 16, Height = 16, Fill = TagMark.BrushOf(hex) };
        var ring = new Ellipse { Width = 22, Height = 22, StrokeThickness = 1.5, Stroke = TagMark.BrushOf(hex), Visibility = Visibility.Hidden };
        var b = new RadioButton { Tag = hex, GroupName = _group + "c", ToolTip = label, Width = 22, Height = 22, Cursor = Cursors.Hand, Focusable = false };
        b.Template = SwatchTemplate();
        b.Content = new Grid { Children = { ring, dot } };
        b.Checked += (_, _) => ring.Visibility = Visibility.Visible;
        b.Unchecked += (_, _) => ring.Visibility = Visibility.Hidden;
        // Checked, not Click: picking with the keyboard or a screen reader works too.
        b.Checked += (_, _) => { if (_color != hex) { _color = hex; UpdatePreview(); } };
        AutomationProperties.SetName(b, label);
        return b;
    }

    RadioButton IconButton(string key, string label, Geometry geometry)
    {
        var ic = new UiIcon(geometry, 13, 1.9);
        ic.SetResourceReference(UiIcon.ForegroundProperty, "TagIconBrush");
        var bg = new Border { Height = 24, CornerRadius = new CornerRadius(4), Background = Brushes.Transparent, Child = ic, Margin = new Thickness(1) };
        var b = new RadioButton { Tag = key, GroupName = _group + "i", ToolTip = label, Cursor = Cursors.Hand, Focusable = false, Template = SwatchTemplate(), Content = bg };
        b.Checked += (_, _) => { bg.SetResourceReference(Border.BackgroundProperty, "AccentSoftBrush"); ic.SetResourceReference(UiIcon.ForegroundProperty, "AccentBrush"); };
        b.Unchecked += (_, _) => { bg.Background = Brushes.Transparent; ic.SetResourceReference(UiIcon.ForegroundProperty, "TagIconBrush"); };
        b.Checked += (_, _) => { if (_icon != key) { _icon = key; UpdatePreview(); } };
        AutomationProperties.SetName(b, label);
        return b;
    }

    static ControlTemplate SwatchTemplate()
    {
        var root = new FrameworkElementFactory(typeof(Border));
        root.SetValue(Border.BackgroundProperty, Brushes.Transparent);
        root.AppendChild(new FrameworkElementFactory(typeof(ContentPresenter)));
        return new ControlTemplate(typeof(RadioButton)) { VisualTree = root };
    }

    void Accept()
    {
        var name = TagName;
        string? problem = null;
        if (name.Length == 0) problem = "Введите название тега.";
        else if (TagStore.Current.Tags.Any(t => t.Id != _editingId && string.Equals(t.Name, name, StringComparison.CurrentCultureIgnoreCase)))
            problem = "Тег с таким названием уже есть.";
        if (problem != null)
        {
            _error.Text = problem;
            _error.Visibility = Visibility.Visible;
            _name.Focus();
            return;
        }
        Finish(true);
    }

    /// <summary>
    /// Shows the popup next to <paramref name="anchor"/> (or centred over the owner). Returns the
    /// created/edited tag, or null when cancelled.
    /// </summary>
    public static TagDef? Show(Window owner, FrameworkElement? anchor, TagDef? editing = null)
    {
        var dlg = new TagDialog(editing) { Owner = owner };
        if (anchor != null && PresentationSource.FromVisual(anchor) != null)
        {
            // Right of the anchor, top aligned with it (16 px is the shadow margin around the card).
            var p = anchor.PointToScreen(new Point(anchor.ActualWidth, 0));
            var dpi = VisualTreeHelper.GetDpi(anchor);
            dlg.WindowStartupLocation = WindowStartupLocation.Manual;
            dlg.Left = p.X / dpi.DpiScaleX - 6;
            dlg.Top = p.Y / dpi.DpiScaleY - 16 - 40;
            var area = SystemParameters.WorkArea;
            dlg.Top = Math.Min(dlg.Top, area.Bottom - 330);
        }
        else dlg.WindowStartupLocation = WindowStartupLocation.CenterOwner;

        if (dlg.ShowDialog() != true) return null;
        var store = TagStore.Current;
        if (editing == null) return store.Create(dlg.TagName, dlg.TagColor, dlg.TagIcon);
        store.Update(editing.Id, dlg.TagName, dlg.TagColor, dlg.TagIcon);
        return store.Find(editing.Id);
    }
}

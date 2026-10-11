using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media;

namespace CanvasExplorer.UI;

/// <summary>Small dark prompt (rename, new folder name).</summary>
public sealed class InputDialog : Window
{
    readonly TextBox _box;

    InputDialog(string title, string value, int selectLength)
    {
        WindowStyle = WindowStyle.None;
        AllowsTransparency = true;
        Background = Brushes.Transparent;
        ResizeMode = ResizeMode.NoResize;
        SizeToContent = SizeToContent.WidthAndHeight;
        ShowInTaskbar = false;
        WindowStartupLocation = WindowStartupLocation.CenterOwner;
        SetResourceReference(FontFamilyProperty, "UiFont");
        FontSize = 12.5;

        _box = new TextBox { Text = value, Height = 28, Padding = new Thickness(8, 0, 8, 0) };
        _box.Style = (Style)FindResource("FieldBox");
        var field = new Border
        {
            CornerRadius = new CornerRadius(4),
            BorderThickness = new Thickness(1, 1, 1, 2),
            Child = _box,
        };
        field.SetResourceReference(Border.BackgroundProperty, "FieldBrush");
        field.SetResourceReference(Border.BorderBrushProperty, "AccentBrush");

        var ok = new Button { Content = "Готово", Height = 26, IsDefault = true, Style = (Style)FindResource("AccentButton"), Padding = new Thickness(14, 0, 14, 0) };
        var cancel = new Button { Content = "Отмена", Height = 26, IsCancel = true, Style = (Style)FindResource("TextButton"), Padding = new Thickness(12, 0, 12, 0), Margin = new Thickness(0, 0, 6, 0) };
        cancel.SetResourceReference(ForegroundProperty, "TextBrush");
        ok.Click += (_, _) => { DialogResult = true; };
        cancel.Click += (_, _) => { DialogResult = false; };

        var buttons = new StackPanel { Orientation = Orientation.Horizontal, HorizontalAlignment = HorizontalAlignment.Right, Margin = new Thickness(0, 12, 0, 0) };
        buttons.Children.Add(cancel);
        buttons.Children.Add(ok);

        var heading = new TextBlock { Text = title, FontWeight = FontWeights.SemiBold, Margin = new Thickness(0, 0, 0, 10) };
        heading.SetResourceReference(TextBlock.ForegroundProperty, "TextBrush");

        var panel = new StackPanel { Width = 320 };
        panel.Children.Add(heading);
        panel.Children.Add(field);
        panel.Children.Add(buttons);

        var card = new Border
        {
            CornerRadius = new CornerRadius(8),
            BorderThickness = new Thickness(1),
            Padding = new Thickness(16, 14, 16, 14),
            Margin = new Thickness(16),
            Child = panel,
            Effect = new System.Windows.Media.Effects.DropShadowEffect { BlurRadius = 24, ShadowDepth = 6, Direction = 270, Opacity = 0.6 },
        };
        card.SetResourceReference(Border.BackgroundProperty, "MenuBrush");
        card.SetResourceReference(Border.BorderBrushProperty, "MenuLineBrush");
        Content = card;

        Loaded += (_, _) =>
        {
            _box.Focus();
            _box.Select(0, selectLength < 0 ? _box.Text.Length : selectLength);
        };
        card.MouseLeftButtonDown += (_, e) => { if (e.ButtonState == MouseButtonState.Pressed) DragMove(); };
    }

    public static string? Ask(Window owner, string title, string value, int selectLength = -1)
    {
        var dlg = new InputDialog(title, value, selectLength) { Owner = owner };
        return dlg.ShowDialog() == true ? dlg._box.Text.Trim() : null;
    }
}

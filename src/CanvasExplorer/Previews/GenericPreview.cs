using System.Windows;
using System.Windows.Controls;
using CanvasExplorer.Models;
using CanvasExplorer.Services;

namespace CanvasExplorer.Previews;

/// <summary>Fallback card for types without a dedicated viewer yet.</summary>
public sealed class GenericPreview : PreviewContent
{
    public GenericPreview(FileItem item) : base(item)
    {
        var chip = new Border
        {
            Width = 48,
            Height = 48,
            CornerRadius = new CornerRadius(8),
            Background = item.ChipBg,
            Child = new TextBlock
            {
                Text = item.Ext,
                FontSize = 12,
                FontWeight = FontWeights.Bold,
                Foreground = item.ChipFg,
                HorizontalAlignment = HorizontalAlignment.Center,
                VerticalAlignment = VerticalAlignment.Center,
            },
        };
        var info = new TextBlock { Text = item.KindText + "\n" + item.SizeText + " · " + Format.LongDate(item.Modified), FontSize = 11.5, Margin = new Thickness(0, 8, 0, 10), TextAlignment = TextAlignment.Center };
        info.SetResourceReference(TextBlock.ForegroundProperty, "MutedBrush");
        var open = new Button { Content = "Открыть", Height = 26, Style = (Style)Application.Current.FindResource("AccentButton"), HorizontalAlignment = HorizontalAlignment.Center, Padding = new Thickness(14, 0, 14, 0) };
        open.Click += (_, _) => Shell.Open(Item.FullPath);

        var stack = new StackPanel { HorizontalAlignment = HorizontalAlignment.Center, VerticalAlignment = VerticalAlignment.Center };
        stack.Children.Add(chip);
        stack.Children.Add(info);
        stack.Children.Add(open);
        Child = stack;
    }

    public override Size PreferredSize => new(260, 190);
}

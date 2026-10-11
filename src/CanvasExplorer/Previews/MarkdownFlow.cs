using System.Text;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Documents;
using System.Windows.Media;
using Markdig;
using Markdig.Extensions.TaskLists;
using Markdig.Syntax;
using Markdig.Syntax.Inlines;
using CanvasExplorer.Services;
using MdBlock = Markdig.Syntax.Block;
using MdInline = Markdig.Syntax.Inlines.Inline;
using WpfBlock = System.Windows.Documents.Block;
using WpfInline = System.Windows.Documents.Inline;

namespace CanvasExplorer.Previews;

/// <summary>
/// Markdown ⇄ FlowDocument for the editable preview. Supported constructs round-trip
/// (headings, paragraphs, emphasis, code, links, lists, task lists, quotes, rules);
/// anything else is kept verbatim as a "raw" block.
/// </summary>
public static class MarkdownFlow
{
    static readonly MarkdownPipeline Pipeline = new MarkdownPipelineBuilder()
        .UseTaskLists()
        .UsePipeTables()
        .UseEmphasisExtras()
        .Build();

    static readonly FontFamily Mono = new("Cascadia Mono, Consolas");
    static readonly double[] HeadingSizes = { 0, 20, 17, 15, 13.5, 13, 13 };

    static Brush B(string hex)
    {
        var b = new SolidColorBrush((Color)ColorConverter.ConvertFromString(hex));
        b.Freeze();
        return b;
    }

    static readonly Brush CodeBg = B("#161616");
    static readonly Brush InlineCodeBg = B("#2C2C2C");
    static readonly Brush QuoteLine = B("#4A4A4A");
    static readonly Brush QuoteText = B("#BDBDBD");
    static readonly Brush RawText = B("#9A9A9A");
    static readonly Brush RuleBrush = B("#3A3A3A");

    // ---------------- Markdown → FlowDocument ----------------

    public static FlowDocument Render(string markdown, Action onTaskToggled)
    {
        var doc = new FlowDocument { PagePadding = new Thickness(12, 8, 12, 12), FontSize = 13, LineHeight = double.NaN };
        doc.SetResourceReference(FlowDocument.FontFamilyProperty, "UiFont");
        doc.SetResourceReference(FlowDocument.ForegroundProperty, "TextBrush");
        var ast = Markdown.Parse(markdown, Pipeline);
        foreach (var b in ast)
            if (RenderBlock(b, markdown, onTaskToggled) is { } block) doc.Blocks.Add(block);
        if (doc.Blocks.Count == 0) doc.Blocks.Add(new Paragraph { Margin = new Thickness(0, 0, 0, 8) });
        return doc;
    }

    static WpfBlock? RenderBlock(MdBlock b, string src, Action onTask)
    {
        switch (b)
        {
            case HeadingBlock h:
            {
                var level = Math.Clamp(h.Level, 1, 6);
                var p = new Paragraph
                {
                    FontSize = HeadingSizes[level],
                    FontWeight = FontWeights.Bold,
                    Margin = new Thickness(0, level == 1 ? 2 : 8, 0, 4),
                    Tag = "h" + level,
                };
                AddInlines(p.Inlines, h.Inline, src, onTask);
                return p;
            }
            case ParagraphBlock pb:
            {
                var p = new Paragraph { Margin = new Thickness(0, 0, 0, 8) };
                AddInlines(p.Inlines, pb.Inline, src, onTask);
                return p;
            }
            case ListBlock lb:
            {
                var isTask = lb.OfType<ListItemBlock>().Any(li => li.FirstOrDefault() is ParagraphBlock { Inline.FirstChild: TaskList });
                var list = new List
                {
                    MarkerStyle = isTask ? TextMarkerStyle.None : lb.IsOrdered ? TextMarkerStyle.Decimal : TextMarkerStyle.Disc,
                    Margin = new Thickness(0, 0, 0, 8),
                    Padding = new Thickness(isTask ? 2 : 20, 0, 0, 0),
                    Tag = isTask ? "task" : lb.IsOrdered ? "ol" : "ul",
                };
                if (lb.IsOrdered && int.TryParse(lb.OrderedStart, out var start)) list.StartIndex = Math.Max(1, start);
                foreach (var li in lb.OfType<ListItemBlock>())
                {
                    var item = new ListItem();
                    foreach (var child in li)
                    {
                        if (RenderBlock(child, src, onTask) is not { } cb) continue;
                        if (cb is Paragraph cp) cp.Margin = new Thickness(0, 0, 0, 2);
                        item.Blocks.Add(cb);
                    }
                    if (item.Blocks.Count == 0) item.Blocks.Add(new Paragraph { Margin = new Thickness(0, 0, 0, 2) });
                    list.ListItems.Add(item);
                }
                return list;
            }
            case QuoteBlock qb:
            {
                var sec = new Section
                {
                    BorderBrush = QuoteLine,
                    BorderThickness = new Thickness(3, 0, 0, 0),
                    Padding = new Thickness(10, 0, 0, 0),
                    Margin = new Thickness(0, 0, 0, 8),
                    Foreground = QuoteText,
                    Tag = "quote",
                };
                foreach (var child in qb)
                    if (RenderBlock(child, src, onTask) is { } cb) sec.Blocks.Add(cb);
                return sec;
            }
            case CodeBlock cb:
            {
                var lang = (cb as FencedCodeBlock)?.Info ?? "";
                var p = new Paragraph
                {
                    FontFamily = Mono,
                    FontSize = 12,
                    Background = CodeBg,
                    Padding = new Thickness(8, 6, 8, 6),
                    Margin = new Thickness(0, 0, 0, 8),
                    Tag = "code:" + lang,
                };
                var lines = cb.Lines;
                for (var i = 0; i < lines.Count; i++)
                {
                    if (i > 0) p.Inlines.Add(new LineBreak());
                    p.Inlines.Add(new Run(lines.Lines[i].Slice.ToString()));
                }
                return p;
            }
            case ThematicBreakBlock:
                return new BlockUIContainer(new Border { Height = 1, Background = RuleBrush, Margin = new Thickness(0, 6, 0, 6) }) { Tag = "hr" };
            default:
            {
                var raw = SafeSlice(src, b.Span.Start, b.Span.Length).TrimEnd('\r', '\n');
                var p = new Paragraph { FontFamily = Mono, FontSize = 12, Foreground = RawText, Margin = new Thickness(0, 0, 0, 8), Tag = "raw" };
                var parts = raw.Replace("\r\n", "\n").Split('\n');
                for (var i = 0; i < parts.Length; i++)
                {
                    if (i > 0) p.Inlines.Add(new LineBreak());
                    p.Inlines.Add(new Run(parts[i]));
                }
                return p;
            }
        }
    }

    static string SafeSlice(string s, int start, int length)
    {
        if (start < 0 || start >= s.Length) return "";
        return s.Substring(start, Math.Min(length, s.Length - start));
    }

    static void AddInlines(InlineCollection target, ContainerInline? container, string src, Action onTask)
    {
        if (container == null) return;
        for (var inline = container.FirstChild; inline != null; inline = inline.NextSibling)
            AddInline(target, inline, src, onTask);
    }

    static void AddInline(InlineCollection target, MdInline inline, string src, Action onTask)
    {
        switch (inline)
        {
            case LiteralInline lit:
                target.Add(new Run(lit.Content.ToString()));
                break;
            case EmphasisInline em:
            {
                Span span = em.DelimiterChar == '~'
                    ? new Span { TextDecorations = TextDecorations.Strikethrough }
                    : em.DelimiterCount >= 2 ? new Bold() : new Italic();
                AddInlines(span.Inlines, em, src, onTask);
                target.Add(span);
                break;
            }
            case CodeInline code:
                target.Add(new Run(code.Content) { FontFamily = Mono, FontSize = 12, Background = InlineCodeBg });
                break;
            case LinkInline { IsImage: true } img:
                target.Add(new Run(SafeSlice(src, img.Span.Start, img.Span.Length)) { Tag = "raw", Foreground = RawText });
                break;
            case LinkInline link:
            {
                var h = MakeLink(link.Url ?? "");
                AddInlines(h.Inlines, link, src, onTask);
                target.Add(h);
                break;
            }
            case AutolinkInline auto:
            {
                var h = MakeLink(auto.Url);
                h.Inlines.Add(new Run(auto.Url));
                target.Add(h);
                break;
            }
            case LineBreakInline lb:
                if (lb.IsHard) target.Add(new LineBreak());
                else target.Add(new Run(" "));
                break;
            case TaskList task:
                target.Add(MakeTaskBox(task.Checked, onTask));
                break;
            case HtmlInline html:
                target.Add(new Run(html.Tag) { Tag = "raw", Foreground = RawText });
                break;
            case HtmlEntityInline entity:
                target.Add(new Run(entity.Transcoded.ToString()));
                break;
            case ContainerInline c:
                AddInlines(target, c, src, onTask);
                break;
            default:
                target.Add(new Run(SafeSlice(src, inline.Span.Start, inline.Span.Length)) { Tag = "raw" });
                break;
        }
    }

    static Hyperlink MakeLink(string url)
    {
        var h = new Hyperlink { Tag = url, ToolTip = url + "\nCtrl+клик — открыть" };
        h.SetResourceReference(TextElement.ForegroundProperty, "AccentBrush");
        if (Uri.TryCreate(url, UriKind.Absolute, out var uri)) h.NavigateUri = uri;
        h.RequestNavigate += (_, e) => Shell.Open(e.Uri.ToString());
        return h;
    }

    public static InlineUIContainer MakeTaskBox(bool isChecked, Action onTask)
    {
        var cb = new CheckBox { IsChecked = isChecked, Style = Application.Current?.TryFindResource("TaskCheck") as Style };
        // Checked/Unchecked, not Click: toggling from the keyboard or a screen reader must be saved too.
        cb.Checked += (_, _) => onTask();
        cb.Unchecked += (_, _) => onTask();
        return new InlineUIContainer(cb) { BaselineAlignment = BaselineAlignment.Center };
    }

    // ---------------- FlowDocument → Markdown ----------------

    public static string Serialize(FlowDocument doc, string newline)
    {
        var parts = new List<string>();
        foreach (var b in doc.Blocks)
        {
            var text = BlockText(b);
            if (!string.IsNullOrWhiteSpace(text)) parts.Add(text);
        }
        var md = string.Join("\n\n", parts) + "\n";
        return newline == "\n" ? md : md.Replace("\n", newline);
    }

    static string? BlockText(WpfBlock b)
    {
        switch (b)
        {
            case Paragraph p:
            {
                var tag = p.Tag as string ?? "";
                if (tag.StartsWith("code:")) return "```" + tag[5..] + "\n" + PlainText(p) + "\n```";
                if (tag == "raw") return PlainText(p);
                if (tag.Length == 2 && tag[0] == 'h' && char.IsDigit(tag[1]))
                    return new string('#', tag[1] - '0') + " " + InlineText(p.Inlines, inHeading: true).Trim();
                return InlineText(p.Inlines, inHeading: false).Trim();
            }
            case List list:
            {
                var tag = list.Tag as string ?? (list.MarkerStyle == TextMarkerStyle.Decimal ? "ol" : "ul");
                var lines = new List<string>();
                var n = list.StartIndex;
                foreach (var li in list.ListItems)
                {
                    var marker = tag == "task" ? (IsChecked(li) ? "- [x] " : "- [ ] ")
                        : tag == "ol" ? n++ + ". "
                        : "- ";
                    var indent = new string(' ', marker.Length);
                    var first = true;
                    foreach (var child in li.Blocks)
                    {
                        var text = BlockText(child) ?? "";
                        var childLines = text.Split('\n');
                        for (var i = 0; i < childLines.Length; i++)
                        {
                            if (first && i == 0) lines.Add(marker + childLines[i]);
                            else lines.Add(childLines[i].Length == 0 ? "" : indent + childLines[i]);
                        }
                        first = false;
                    }
                    if (first) lines.Add(marker.TrimEnd());
                }
                return string.Join("\n", lines);
            }
            case Section sec:
            {
                var inner = string.Join("\n\n", sec.Blocks.Select(BlockText).Where(t => !string.IsNullOrWhiteSpace(t)));
                return string.Join("\n", inner.Split('\n').Select(l => l.Length == 0 ? ">" : "> " + l));
            }
            case BlockUIContainer:
                return "---";
            default:
                return PlainText(b);
        }
    }

    static bool IsChecked(ListItem li) =>
        li.Blocks.FirstBlock is Paragraph p &&
        p.Inlines.OfType<InlineUIContainer>().FirstOrDefault()?.Child is CheckBox { IsChecked: true };

    static string PlainText(TextElement e) =>
        new TextRange(e.ContentStart, e.ContentEnd).Text.Replace("\r\n", "\n").TrimEnd('\n');

    readonly record struct Seg(string Text, bool Bold, bool Italic, bool Strike, bool Code, bool Raw, string? Link, bool Break);

    static string InlineText(InlineCollection inlines, bool inHeading)
    {
        var segs = new List<Seg>();
        Collect(inlines, segs, inHeading, false, null);
        var sb = new StringBuilder();
        var i = 0;
        while (i < segs.Count)
        {
            var s = segs[i];
            if (s.Break) { sb.Append("  \n"); i++; continue; }
            if (s.Link != null)
            {
                var text = new StringBuilder();
                var url = s.Link;
                while (i < segs.Count && segs[i].Link == url && !segs[i].Break) text.Append(Wrap(segs[i++]));
                var t = text.ToString();
                sb.Append(t == url ? "<" + url + ">" : "[" + t + "](" + url + ")");
                continue;
            }
            // Merge neighbours with identical formatting so "**a** **b**" stays "**a b**".
            var merged = s.Text;
            i++;
            while (i < segs.Count && !segs[i].Break && segs[i].Link == null && SameFormat(segs[i], s))
                merged += segs[i++].Text;
            sb.Append(Wrap(s with { Text = merged }));
        }
        return sb.ToString();
    }

    static bool SameFormat(Seg a, Seg b) =>
        a.Bold == b.Bold && a.Italic == b.Italic && a.Strike == b.Strike && a.Code == b.Code && a.Raw == b.Raw;

    static string Wrap(Seg s)
    {
        if (s.Raw) return s.Text;
        if (s.Code) return "`" + s.Text + "`";
        var core = s.Text.Trim();
        if (core.Length == 0) return s.Text;
        var lead = s.Text[..s.Text.IndexOf(core[0])];
        var trail = s.Text[(lead.Length + core.Length)..];
        if (s.Strike) core = "~~" + core + "~~";
        if (s.Italic) core = "*" + core + "*";
        if (s.Bold) core = "**" + core + "**";
        return lead + core + trail;
    }

    static void Collect(InlineCollection inlines, List<Seg> segs, bool inHeading, bool strike, string? link)
    {
        foreach (var inline in inlines)
        {
            switch (inline)
            {
                case Run r:
                {
                    if (r.Text.Length == 0) break;
                    var isRaw = r.Tag as string == "raw";
                    var code = !isRaw && r.FontFamily.Source.Contains("Cascadia", StringComparison.OrdinalIgnoreCase);
                    var bold = !inHeading && r.FontWeight.ToOpenTypeWeight() >= 600;
                    var italic = r.FontStyle == FontStyles.Italic;
                    var st = strike || HasStrike(r);
                    segs.Add(new Seg(r.Text, bold, italic, st, code, isRaw, link, false));
                    break;
                }
                case LineBreak:
                    segs.Add(new Seg("", false, false, false, false, false, null, true));
                    break;
                case Hyperlink h:
                    Collect(h.Inlines, segs, inHeading, strike, h.Tag as string ?? h.NavigateUri?.ToString());
                    break;
                case Span span:
                    Collect(span.Inlines, segs, inHeading, strike || HasStrike(span), link);
                    break;
                case InlineUIContainer:
                    break; // task checkbox — written by the list
            }
        }
    }

    static bool HasStrike(WpfInline i) =>
        i.TextDecorations != null && i.TextDecorations.Any(d => d.Location == TextDecorationLocation.Strikethrough);
}

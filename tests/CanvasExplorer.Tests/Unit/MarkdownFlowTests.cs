using System.Windows.Controls;
using System.Windows.Documents;
using CanvasExplorer.Previews;
using CanvasExplorer.Tests.Infrastructure;

namespace CanvasExplorer.Tests.Unit;

/// <summary>
/// The Markdown window is edited as rendered text and written back as Markdown. Anything the
/// preview supports must survive Render → Serialize unchanged, or a click in the preview rewrites the file.
/// </summary>
public class MarkdownFlowTests
{
    static string RoundTrip(string md) => Sta.Run(() => MarkdownFlow.Serialize(MarkdownFlow.Render(md, () => { }), "\n"));

    [Theory]
    [InlineData("# Заголовок\n")]
    [InlineData("## Второй уровень\n")]
    [InlineData("Простой абзац.\n")]
    [InlineData("Текст с **жирным** и *курсивом*.\n")]
    [InlineData("Зачёркнутое ~~слово~~ тут.\n")]
    [InlineData("Код `var x = 1;` в строке.\n")]
    [InlineData("Ссылка [сайт](https://example.com) здесь.\n")]
    [InlineData("- один\n- два\n- три\n")]
    [InlineData("1. первый\n2. второй\n")]
    [InlineData("3. с тройки\n4. дальше\n")]
    [InlineData("- [ ] купить хлеб\n- [x] молоко\n")]
    [InlineData("> цитата\n")]
    [InlineData("```cs\nvar a = 1;\nvar b = 2;\n```\n")]
    [InlineData("---\n")]
    [InlineData("# Список\n\n- [x] готово\n- [ ] нет\n\nКонец.\n")]
    public void Supported_markdown_survives_a_round_trip(string md) => Assert.Equal(md, RoundTrip(md));

    [Fact]
    public void Windows_line_endings_are_kept()
    {
        var text = Sta.Run(() => MarkdownFlow.Serialize(MarkdownFlow.Render("a\r\n\r\nb\r\n", () => { }), "\r\n"));
        Assert.Equal("a\r\n\r\nb\r\n", text);
    }

    [Fact]
    public void Task_checkbox_toggle_is_written_back()
    {
        var text = Sta.Run(() =>
        {
            var toggled = 0;
            var doc = MarkdownFlow.Render("- [ ] задача\n", () => toggled++);
            var box = doc.Blocks.OfType<List>().Single().ListItems.First().Blocks.OfType<Paragraph>().First()
                .Inlines.OfType<InlineUIContainer>().Select(c => c.Child).OfType<CheckBox>().Single();
            // Toggle the way a click does (raises Click, unlike setting IsChecked).
            var peer = new System.Windows.Automation.Peers.CheckBoxAutomationPeer(box);
            ((System.Windows.Automation.Provider.IToggleProvider)peer.GetPattern(System.Windows.Automation.Peers.PatternInterface.Toggle)).Toggle();
            Assert.Equal(1, toggled);
            return MarkdownFlow.Serialize(doc, "\n");
        });
        Assert.Equal("- [x] задача\n", text);
    }

    [Fact]
    public void Unsupported_blocks_are_kept_verbatim()
    {
        const string table = "| a | b |\n|---|---|\n| 1 | 2 |\n";
        Assert.Equal(table, RoundTrip(table));
    }
}

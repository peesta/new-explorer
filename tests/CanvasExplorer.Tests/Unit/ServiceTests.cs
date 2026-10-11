using System.IO;
using System.Text;
using System.Windows;
using CanvasExplorer.Models;
using CanvasExplorer.Services;
using CanvasExplorer.Tests.Infrastructure;

namespace CanvasExplorer.Tests.Unit;

public class FormatTests
{
    [Theory]
    [InlineData(0, "0 объектов")]
    [InlineData(1, "1 объект")]
    [InlineData(2, "2 объекта")]
    [InlineData(4, "4 объекта")]
    [InlineData(5, "5 объектов")]
    [InlineData(11, "11 объектов")]
    [InlineData(12, "12 объектов")]
    [InlineData(14, "14 объектов")]
    [InlineData(21, "21 объект")]
    [InlineData(22, "22 объекта")]
    [InlineData(111, "111 объектов")]
    [InlineData(1001, "1001 объект")]
    public void Plural_follows_russian_rules(int n, string expected) =>
        Assert.Equal(expected, Format.Plural(n, "объект", "объекта", "объектов"));

    [Theory]
    [InlineData(0, "0 Б")]
    [InlineData(1023, "1023 Б")]
    [InlineData(1024, "1 КБ")]
    [InlineData(1536, "1,5 КБ")]
    [InlineData(10 * 1024, "10 КБ")]
    [InlineData(1024 * 1024 - 1, "1 МБ")]          // used to print "1024 КБ"
    [InlineData(1024L * 1024 * 1024 - 1, "1 ГБ")]
    [InlineData(10189, "10 КБ")]                   // 9.95 KB rounds to 10, no stray decimal
    public void Size_is_short_and_never_shows_1024_of_a_unit(long bytes, string expected) =>
        Assert.Equal(expected, Format.Size(bytes));

    [Fact]
    public void ShortDate_uses_time_today_and_word_for_yesterday()
    {
        var now = DateTime.Today.AddHours(9).AddMinutes(5);
        Assert.Equal("09:05", Format.ShortDate(now));
        Assert.Equal("Вчера", Format.ShortDate(DateTime.Today.AddDays(-1).AddHours(3)));
    }
}

public class TextFileTests : IDisposable
{
    readonly string _dir = TestSetup.NewFolder("textfile");

    public void Dispose() => TestSetup.DeleteFolder(_dir);

    string PathOf(string name) => Path.Combine(_dir, name);

    [Fact]
    public void Utf8_without_bom_round_trips()
    {
        var p = PathOf("a.txt");
        File.WriteAllBytes(p, new UTF8Encoding(false).GetBytes("Привет, мир"));
        var (text, enc, truncated) = TextFile.Read(p);
        Assert.Equal("Привет, мир", text);
        Assert.False(truncated);
        TextFile.Write(p, text + "!", enc);
        Assert.Equal(new UTF8Encoding(false).GetBytes("Привет, мир!"), File.ReadAllBytes(p));
    }

    [Fact]
    public void Utf8_bom_is_kept_on_save()
    {
        var p = PathOf("bom.txt");
        File.WriteAllText(p, "текст", new UTF8Encoding(true));
        var (text, enc, _) = TextFile.Read(p);
        Assert.Equal("текст", text);
        TextFile.Write(p, "новый", enc);
        var bytes = File.ReadAllBytes(p);
        Assert.Equal(new byte[] { 0xEF, 0xBB, 0xBF }, bytes[..3]);
        Assert.Equal("новый", Encoding.UTF8.GetString(bytes, 3, bytes.Length - 3));
    }

    [Fact]
    public void Cp1251_is_detected_and_kept_on_save()
    {
        var cp = Encoding.GetEncoding(1251);
        var p = PathOf("win.txt");
        File.WriteAllBytes(p, cp.GetBytes("Старый файл"));
        var (text, enc, _) = TextFile.Read(p);
        Assert.Equal("Старый файл", text);
        TextFile.Write(p, "Новый файл", enc);
        Assert.Equal(cp.GetBytes("Новый файл"), File.ReadAllBytes(p));
    }

    [Fact]
    public void Utf16_le_with_bom_is_read()
    {
        var p = PathOf("u16.txt");
        File.WriteAllText(p, "юникод", new UnicodeEncoding(false, true));
        Assert.Equal("юникод", TextFile.Read(p).text);
    }

    [Fact]
    public void Truncated_big_utf8_file_is_not_mistaken_for_cp1251()
    {
        // Cyrillic is 2 bytes in UTF-8; an odd prefix makes the 4 MB cut land in the middle of a character.
        var p = PathOf("big.txt");
        var sb = new StringBuilder("x");
        while (sb.Length < TextFile.MaxEditableBytes / 2 + 1000) sb.Append("Ж");
        File.WriteAllText(p, sb.ToString(), new UTF8Encoding(false));
        var (text, enc, truncated) = TextFile.Read(p);
        Assert.True(truncated);
        Assert.IsType<UTF8Encoding>(enc);
        Assert.StartsWith("xЖЖЖ", text);
        Assert.DoesNotContain('\uFFFD', text);
    }

    [Fact]
    public void Write_leaves_no_temp_file_behind()
    {
        var p = PathOf("t.txt");
        File.WriteAllText(p, "1");
        TextFile.Write(p, "2", new UTF8Encoding(false));
        Assert.Equal("2", File.ReadAllText(p));
        Assert.False(File.Exists(p + ".~tmp"));
    }

    [Fact]
    public void FileStamp_changes_when_another_program_writes()
    {
        var p = PathOf("s.txt");
        File.WriteAllText(p, "one");
        var a = FileStamp.Of(p);
        File.WriteAllText(p, "three");
        var b = FileStamp.Of(p);
        Assert.True(a.Exists);
        Assert.NotEqual(a, b);
        Assert.False(FileStamp.Of(PathOf("missing.txt")).Exists);
    }
}

public class ShellRulesTests
{
    static readonly DragDropEffects All = DragDropEffects.Copy | DragDropEffects.Move | DragDropEffects.Link;

    [Fact]
    public void Same_drive_moves_other_drive_copies()
    {
        Assert.Equal(DragDropEffects.Move, Shell.DropEffect(new[] { @"C:\a\x.txt" }, @"C:\b", 0, All));
        Assert.Equal(DragDropEffects.Copy, Shell.DropEffect(new[] { @"D:\a\x.txt" }, @"C:\b", 0, All));
    }

    [Fact]
    public void Ctrl_copies_and_shift_moves()
    {
        Assert.Equal(DragDropEffects.Copy, Shell.DropEffect(new[] { @"C:\a\x.txt" }, @"C:\b", DragDropKeyStates.ControlKey, All));
        Assert.Equal(DragDropEffects.Move, Shell.DropEffect(new[] { @"D:\a\x.txt" }, @"C:\b", DragDropKeyStates.ShiftKey, All));
    }

    [Fact]
    public void Dropping_into_own_folder_or_onto_itself_does_nothing()
    {
        Assert.Equal(DragDropEffects.None, Shell.DropEffect(new[] { @"C:\a\x.txt" }, @"C:\a", 0, All));
        Assert.Equal(DragDropEffects.None, Shell.DropEffect(new[] { @"C:\a\sub" }, @"C:\a\sub", 0, All));
        Assert.Equal(DragDropEffects.None, Shell.DropEffect(new[] { @"C:\a\x.txt" }, @"C:\A\", 0, All));
    }

    [Fact]
    public void Falls_back_to_copy_when_move_is_not_allowed()
    {
        Assert.Equal(DragDropEffects.Copy, Shell.DropEffect(new[] { @"C:\a\x.txt" }, @"C:\b", 0, DragDropEffects.Copy));
        Assert.Equal(DragDropEffects.None, Shell.DropEffect(new[] { @"C:\a\x.txt" }, @"C:\b", 0, DragDropEffects.Link));
    }

    [Theory]
    [InlineData(@"C:\a", @"C:\A\", true)]
    [InlineData(@"C:\a", @"C:\ab", false)]
    [InlineData(null, @"C:\a", false)]
    public void SameFolder_ignores_case_and_trailing_slash(string? a, string b, bool expected) =>
        Assert.Equal(expected, Shell.SameFolder(a, b));
}

public class NamingTests
{
    [Theory]
    [InlineData(@"C:\a", @"C:\a\b\c", "b")]
    [InlineData(@"C:\a\", @"C:\a\b", "b")]
    [InlineData(@"C:\", @"C:\Users\me", "Users")]
    [InlineData(@"C:\a", @"C:\ab\c", null)]
    [InlineData(@"C:\a", @"C:\a", null)]
    [InlineData(@"C:\a\b", @"C:\a", null)]
    public void ChildOnTheWay_finds_the_folder_we_came_from(string outer, string inner, string? expected) =>
        Assert.Equal(expected, MainWindow.ChildOnTheWay(outer, inner));

    [Fact]
    public void NameProblem_explains_bad_names()
    {
        var dir = TestSetup.NewFolder("names");
        try
        {
            File.WriteAllText(Path.Combine(dir, "есть.txt"), "");
            Assert.Null(MainWindow.NameProblem("новый.txt", dir, null));
            Assert.Null(MainWindow.NameProblem("ЕСТЬ.txt", dir, "есть.txt"));        // only the case changes
            Assert.NotNull(MainWindow.NameProblem("есть.txt", dir, "другой.txt"));    // taken
            Assert.NotNull(MainWindow.NameProblem("a:b", dir, null));
            Assert.NotNull(MainWindow.NameProblem("a?b", dir, null));
            Assert.NotNull(MainWindow.NameProblem("   ", dir, null));
            Assert.NotNull(MainWindow.NameProblem("...", dir, null));
            Assert.NotNull(MainWindow.NameProblem("con.txt", dir, null));
            Assert.NotNull(MainWindow.NameProblem("есть.txt.", dir, null));          // Windows drops the trailing dot
        }
        finally { TestSetup.DeleteFolder(dir); }
    }
}

public class LayoutStoreTests : IDisposable
{
    readonly string _dir = TestSetup.NewFolder("layout");

    public void Dispose() => TestSetup.DeleteFolder(_dir);

    [Fact]
    public void Layout_is_saved_hidden_and_saved_again_over_the_hidden_file()
    {
        var layout = new FolderLayout { List = new RectState { X = 1, Y = 2, W = 300, H = 400 } };
        layout.Items.Add(new PinState { Name = "Озеро.png", X = 10, Y = 20, W = 200, H = 100, Zoom = 1.5 });
        LayoutStore.Save(_dir, layout);
        var path = Path.Combine(_dir, LayoutStore.FileName);
        Assert.True(File.GetAttributes(path).HasFlag(FileAttributes.Hidden));
        Assert.Contains("Озеро.png", File.ReadAllText(path)); // readable, not \u-escaped

        layout.Items[0].X = 99;
        LayoutStore.Save(_dir, layout);
        var back = LayoutStore.Load(_dir)!;
        Assert.Equal(99, back.Items[0].X);
        Assert.Equal(1.5, back.Items[0].Zoom);
        Assert.Equal(300, back.List.W);
    }

    [Fact]
    public void Broken_file_reads_as_no_layout()
    {
        File.WriteAllText(Path.Combine(_dir, LayoutStore.FileName), "{ not json");
        Assert.Null(LayoutStore.Load(_dir));
    }

    [Fact]
    public void Saving_for_a_deleted_folder_writes_nothing_anywhere()
    {
        var gone = Path.Combine(_dir, "gone");
        LayoutStore.Save(gone, new FolderLayout());
        Assert.False(Directory.Exists(gone));
        Assert.Null(LayoutStore.Load(gone));
    }

    [Fact]
    public void Delete_removes_hidden_file()
    {
        LayoutStore.Save(_dir, new FolderLayout());
        LayoutStore.Delete(_dir);
        Assert.False(File.Exists(Path.Combine(_dir, LayoutStore.FileName)));
    }
}

public class FileItemTests : IDisposable
{
    readonly string _dir = TestSetup.NewFolder("items");

    public void Dispose() => TestSetup.DeleteFolder(_dir);

    [Fact]
    public void Describes_files_and_folders()
    {
        var f = Path.Combine(_dir, "Фото.JPEG");
        File.WriteAllBytes(f, new byte[2048]);
        var item = new FileItem(new FileInfo(f));
        Assert.Equal(FileKind.Image, item.Kind);
        Assert.Equal("JPEG", item.Ext);
        Assert.Equal("2 КБ", item.SizeText);
        Assert.Equal("Фото.JPEG", item.ToString());

        var d = Directory.CreateDirectory(Path.Combine(_dir, "Папка"));
        var folder = new FileItem(d);
        Assert.True(folder.IsFolder);
        Assert.Equal("", folder.SizeText);
        Assert.Equal(Visibility.Visible, folder.FolderVisibility);
    }

    [Fact]
    public void Long_or_missing_extension_gets_a_short_chip()
    {
        var a = Path.Combine(_dir, "README");
        var b = Path.Combine(_dir, "data.backup");
        File.WriteAllText(a, "");
        File.WriteAllText(b, "");
        Assert.Equal("—", new FileItem(new FileInfo(a)).Ext);
        Assert.Equal("BACK", new FileItem(new FileInfo(b)).Ext);
    }
}

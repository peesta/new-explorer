using System.IO;
using CanvasExplorer.Services;
using CanvasExplorer.Tests.Infrastructure;

namespace CanvasExplorer.Tests.Unit;

public class TagStoreTests : IDisposable
{
    readonly string _dir = TestSetup.NewFolder("tags");
    string StoreFile => Path.Combine(_dir, "tags.json");

    public void Dispose() => TestSetup.DeleteFolder(_dir);

    string File(string name, string text = "")
    {
        var p = Path.Combine(_dir, name);
        Directory.CreateDirectory(Path.GetDirectoryName(p)!);
        System.IO.File.WriteAllText(p, text);
        return p;
    }

    [Fact]
    public void Tag_is_either_a_colour_or_an_icon_and_survives_a_reload()
    {
        var store = new TagStore(StoreFile);
        var urgent = store.Create("Срочно", "#E5484D", null);
        var idea = store.Create("Идеи", "#E5484D", "bulb");
        Assert.Null(idea.Color); // an icon tag has no colour

        var back = new TagStore(StoreFile);
        Assert.Equal(new[] { "Срочно", "Идеи" }, back.Tags.Select(t => t.Name));
        Assert.Equal("#E5484D", back.Find(urgent.Id)!.Color);
        Assert.Equal("bulb", back.Find(idea.Id)!.Icon);
    }

    [Fact]
    public void Assign_count_and_remove()
    {
        var store = new TagStore(StoreFile);
        var tag = store.Create("Заказать", "#F08C00", null);
        var a = File("a.txt");
        var b = File("b.txt");
        store.Set(new[] { a, b }, tag.Id, true);
        Assert.Equal(2, store.Count(tag.Id));
        Assert.True(store.Has(a.ToUpperInvariant(), tag.Id)); // paths are case-insensitive
        store.Set(new[] { a }, tag.Id, false);
        Assert.Equal(new[] { b }, store.PathsOf(tag.Id));
    }

    [Fact]
    public void Deleted_files_are_not_counted_but_keep_their_tag_for_a_restore()
    {
        var store = new TagStore(StoreFile);
        var tag = store.Create("Т", null, "star");
        var a = File("a.txt");
        store.Set(new[] { a }, tag.Id, true);
        System.IO.File.Delete(a);
        Assert.Equal(0, store.Count(tag.Id));
        File("a.txt");
        Assert.Equal(1, store.Count(tag.Id));
    }

    [Fact]
    public void Tags_follow_a_renamed_file_and_everything_inside_a_moved_folder()
    {
        var store = new TagStore(StoreFile);
        var tag = store.Create("Т", "#0078D4", null);
        var file = File("старое.txt");
        var inner = File(@"Папка\внутри.txt");
        store.Set(new[] { file, inner }, tag.Id, true);

        store.Move(file, Path.Combine(_dir, "новое.txt"));
        store.Move(Path.Combine(_dir, "Папка"), Path.Combine(_dir, "Другая"));

        Assert.False(store.Has(file, tag.Id));
        Assert.True(store.Has(Path.Combine(_dir, "новое.txt"), tag.Id));
        Assert.True(store.Has(Path.Combine(_dir, "Другая", "внутри.txt"), tag.Id));
        Assert.False(store.Has(inner, tag.Id));
    }

    [Fact]
    public void Copy_keeps_the_source_tags()
    {
        var store = new TagStore(StoreFile);
        var tag = store.Create("Т", "#0078D4", null);
        var a = File("a.txt");
        store.Set(new[] { a }, tag.Id, true);
        store.Copy(a, Path.Combine(_dir, "sub", "a.txt"));
        Assert.True(store.Has(a, tag.Id));
        Assert.True(store.Has(Path.Combine(_dir, "sub", "a.txt"), tag.Id));
    }

    [Fact]
    public void Deleting_a_tag_takes_it_off_every_file()
    {
        var store = new TagStore(StoreFile);
        var keep = store.Create("Оставить", "#2F9E6E", null);
        var drop = store.Create("Удалить", "#7A7A7A", null);
        var a = File("a.txt");
        store.Set(new[] { a }, keep.Id, true);
        store.Set(new[] { a }, drop.Id, true);
        store.Delete(drop.Id);
        Assert.Equal(new[] { "Оставить" }, store.TagsOf(a).Select(t => t.Name));
        Assert.DoesNotContain(new TagStore(StoreFile).Tags, t => t.Id == drop.Id);
    }

    [Fact]
    public void Update_switches_between_colour_and_icon()
    {
        var store = new TagStore(StoreFile);
        var tag = store.Create("Т", "#0078D4", null);
        store.Update(tag.Id, "Новое имя", null, "heart");
        Assert.Equal("Новое имя", store.Find(tag.Id)!.Name);
        Assert.Equal("heart", store.Find(tag.Id)!.Icon);
        store.Update(tag.Id, "Новое имя", "#D6409F", null);
        Assert.Null(store.Find(tag.Id)!.Icon);
        Assert.Equal("#D6409F", store.Find(tag.Id)!.Color);
    }

    [Fact]
    public void Tags_are_written_into_the_file_itself_without_changing_its_date()
    {
        var store = new TagStore(StoreFile);
        var tag = store.Create("Срочно", "#E5484D", null);
        var file = File("отчёт.txt", "данные");
        var stamp = new DateTime(2020, 5, 1, 12, 0, 0, DateTimeKind.Utc);
        System.IO.File.SetLastWriteTimeUtc(file, stamp);

        store.Set(new[] { file }, tag.Id, true);
        Assert.True(System.IO.File.Exists(file + TagStore.StreamName), "поток с тегами создан");
        Assert.Contains("Срочно", System.IO.File.ReadAllText(file + TagStore.StreamName));
        Assert.Equal(stamp, System.IO.File.GetLastWriteTimeUtc(file));
        Assert.Equal("данные", System.IO.File.ReadAllText(file));

        store.Set(new[] { file }, tag.Id, false);
        Assert.False(System.IO.File.Exists(file + TagStore.StreamName), "без тегов поток удалён");
        Assert.Equal(stamp, System.IO.File.GetLastWriteTimeUtc(file));
    }

    [Fact]
    public void A_file_moved_by_another_program_keeps_its_tags()
    {
        var store = new TagStore(StoreFile);
        var tag = store.Create("Мебель", "#8764B8", null);
        var file = File(@"Откуда\стол.jpg");
        store.Set(new[] { file }, tag.Id, true);

        // Moved outside the app: the index still points at the old place.
        Directory.CreateDirectory(Path.Combine(_dir, "Куда"));
        var moved = Path.Combine(_dir, "Куда", "стол.jpg");
        System.IO.File.Move(file, moved);

        store.Reconcile(Path.Combine(_dir, "Куда"), new[] { moved });
        store.Reconcile(Path.Combine(_dir, "Откуда"), Array.Empty<string>());
        Assert.True(store.Has(moved, tag.Id));
        Assert.Equal(new[] { moved }, store.PathsOf(tag.Id));
    }

    [Fact]
    public void Another_computer_recreates_the_tag_from_the_file()
    {
        var mine = new TagStore(StoreFile);
        var tag = mine.Create("Идеи", null, "bulb");
        var file = File("заметка.md");
        mine.Set(new[] { file }, tag.Id, true);

        var other = new TagStore(Path.Combine(_dir, "other-tags.json"));
        Assert.Empty(other.Tags);
        other.Reconcile(_dir, new[] { file });
        var seen = Assert.Single(other.TagsOf(file));
        Assert.Equal("Идеи", seen.Name);
        Assert.Equal("bulb", seen.Icon);
    }

    [Fact]
    public void A_lost_stream_is_written_again_from_the_index()
    {
        var store = new TagStore(StoreFile);
        var tag = store.Create("Т", "#0078D4", null);
        var file = File("doc.txt", "1");
        store.Set(new[] { file }, tag.Id, true);
        // An editor that saves through a temporary file drops the stream.
        System.IO.File.Delete(file);
        System.IO.File.WriteAllText(file, "2");
        Assert.False(System.IO.File.Exists(file + TagStore.StreamName));

        store.Reconcile(_dir, new[] { file });
        Assert.True(store.Has(file, tag.Id));
        Assert.True(System.IO.File.Exists(file + TagStore.StreamName), "поток восстановлен");
    }

    [Fact]
    public void Read_only_files_can_be_tagged()
    {
        var store = new TagStore(StoreFile);
        var tag = store.Create("Т", "#0078D4", null);
        var file = File("ro.txt");
        System.IO.File.SetAttributes(file, FileAttributes.ReadOnly);
        store.Set(new[] { file }, tag.Id, true);
        Assert.True(System.IO.File.Exists(file + TagStore.StreamName));
        Assert.True(System.IO.File.GetAttributes(file).HasFlag(FileAttributes.ReadOnly), "атрибут вернулся");
        System.IO.File.SetAttributes(file, FileAttributes.Normal);
    }

    [Fact]
    public void Broken_file_starts_empty()
    {
        System.IO.File.WriteAllText(StoreFile, "{ broken");
        Assert.Empty(new TagStore(StoreFile).Tags);
    }

    [Fact]
    public void Changed_is_raised_on_every_change()
    {
        var store = new TagStore(StoreFile);
        var n = 0;
        store.Changed += (_, _) => n++;
        var tag = store.Create("Т", "#0078D4", null);
        store.Set(new[] { File("x.txt") }, tag.Id, true);
        store.Delete(tag.Id);
        Assert.Equal(3, n);
    }
}

public class AppSettingsTests
{
    [Fact]
    public void Grid_view_is_remembered_per_folder()
    {
        var s = new AppSettings();
        Sta.Run(() =>
        {
            s.SetGrid(@"C:\Фото", true);
            s.SetGrid(@"C:\Документы", false);
        });
        Assert.True(s.IsGrid(@"c:\фото"));
        Assert.False(s.IsGrid(@"C:\Документы"));
        Assert.False(s.IsGrid(@"C:\Другая"));
        Sta.Run(() => s.SetGrid(@"C:\Фото", false));
        Assert.False(s.IsGrid(@"C:\Фото"));
    }
}

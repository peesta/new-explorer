using System.IO;
using System.Text;
using System.Text.Json;

namespace CanvasExplorer.Services;

/// <summary>A tag: a name and either a colour dot or an icon.</summary>
public sealed class TagDef
{
    public string Id { get; set; } = "";
    public string Name { get; set; } = "";
    /// <summary>"#RRGGBB" for a colour tag; null for an icon tag.</summary>
    public string? Color { get; set; }
    /// <summary>Icon key (see <c>Icons.TagIcons</c>) for an icon tag; null for a colour tag.</summary>
    public string? Icon { get; set; }
}

/// <summary>
/// Tags are stored WITH the file: in a hidden NTFS stream <c>file:canvas.tags</c> (one line per tag: id, name,
/// colour, icon), which travels with the file when any program renames or moves it within NTFS, and lets another
/// computer recreate the tag. %AppData%\CanvasExplorer\tags.json keeps the tag list and an index
/// (path → tag ids) for counts and the tag view; the index is corrected from the streams whenever a folder is
/// read (<see cref="Reconcile"/>). Where streams are not supported (FAT, some network drives) the index alone is used.
/// Writing a stream never changes the file's "modified" date. Thread-safe: folders are read on a background thread.
/// </summary>
public sealed class TagStore
{
    public const string StreamName = ":canvas.tags";

    sealed class Data
    {
        public List<TagDef> Tags { get; set; } = new();
        public Dictionary<string, List<string>> Files { get; set; } = new(StringComparer.OrdinalIgnoreCase);
    }

    static readonly JsonSerializerOptions Json = new()
    {
        WriteIndented = true,
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
        Encoder = System.Text.Encodings.Web.JavaScriptEncoder.UnsafeRelaxedJsonEscaping,
    };

    public static TagStore Current { get; } = new(Path.Combine(AppSettings.DataDir, "tags.json"));

    readonly object _lock = new();
    readonly string _file;
    readonly List<TagDef> _tags;
    readonly Dictionary<string, List<string>> _files;

    public TagStore(string file)
    {
        _file = file;
        Data data;
        try
        {
            data = File.Exists(file) ? JsonSerializer.Deserialize<Data>(File.ReadAllText(file), Json) ?? new Data() : new Data();
        }
        catch
        {
            data = new Data();
        }
        _tags = data.Tags;
        _files = new Dictionary<string, List<string>>(data.Files, StringComparer.OrdinalIgnoreCase);
    }

    /// <summary>Tags or assignments changed (raised on the thread that made the change).</summary>
    public event EventHandler? Changed;

    public IReadOnlyList<TagDef> Tags
    {
        get { lock (_lock) return _tags.ToList(); }
    }

    public TagDef? Find(string id)
    {
        lock (_lock) return _tags.FirstOrDefault(t => t.Id == id);
    }

    public TagDef Create(string name, string? color, string? icon)
    {
        var tag = new TagDef { Id = Guid.NewGuid().ToString("N")[..10], Name = name.Trim(), Color = icon == null ? color : null, Icon = icon };
        lock (_lock) _tags.Add(tag);
        Commit();
        return tag;
    }

    public void Update(string id, string name, string? color, string? icon)
    {
        lock (_lock)
        {
            if (_tags.FirstOrDefault(t => t.Id == id) is not { } tag) return;
            tag.Name = name.Trim();
            tag.Icon = icon;
            tag.Color = icon == null ? color : null;
        }
        // The streams carry the tag's look too (for other computers): rewrite them.
        foreach (var p in IndexedWith(id)) WriteStream(p);
        Commit();
    }

    public void Delete(string id)
    {
        List<string> touched;
        lock (_lock)
        {
            _tags.RemoveAll(t => t.Id == id);
            touched = _files.Where(kv => kv.Value.Contains(id)).Select(kv => kv.Key).ToList();
            foreach (var key in touched)
            {
                _files[key].Remove(id);
                if (_files[key].Count == 0) _files.Remove(key);
            }
        }
        foreach (var p in touched) WriteStream(p);
        Commit();
    }

    List<string> IndexedWith(string id)
    {
        lock (_lock) return _files.Where(kv => kv.Value.Contains(id)).Select(kv => kv.Key).ToList();
    }

    /// <summary>Tags of a file or folder, in tag order.</summary>
    public IReadOnlyList<TagDef> TagsOf(string path)
    {
        lock (_lock)
        {
            if (!_files.TryGetValue(Normalize(path), out var ids) || ids.Count == 0) return Array.Empty<TagDef>();
            return _tags.Where(t => ids.Contains(t.Id)).ToList();
        }
    }

    public bool Has(string path, string id)
    {
        lock (_lock) return _files.TryGetValue(Normalize(path), out var ids) && ids.Contains(id);
    }

    /// <summary>Puts the tag on the files (or takes it off): into each file's stream and the index.</summary>
    public void Set(IEnumerable<string> paths, string id, bool on)
    {
        var changed = new List<string>();
        lock (_lock)
        {
            if (_tags.All(t => t.Id != id)) return;
            foreach (var raw in paths)
            {
                var p = Normalize(raw);
                if (on)
                {
                    if (!_files.TryGetValue(p, out var ids)) _files[p] = ids = new List<string>();
                    if (ids.Contains(id)) continue;
                    ids.Add(id);
                }
                else
                {
                    if (!_files.TryGetValue(p, out var ids) || !ids.Remove(id)) continue;
                    if (ids.Count == 0) _files.Remove(p);
                }
                changed.Add(p);
            }
        }
        foreach (var p in changed) WriteStream(p);
        Commit();
    }

    /// <summary>Everything that carries the tag and still exists.</summary>
    public IReadOnlyList<string> PathsOf(string id)
    {
        var all = IndexedWith(id);
        // Deleted files keep their entry (they may come back from the recycle bin) but are not listed.
        return all.Where(p => File.Exists(p) || Directory.Exists(p)).ToList();
    }

    public int Count(string id) => PathsOf(id).Count;

    /// <summary>A file or folder was renamed or moved by this app: the index follows (the stream travels by itself).</summary>
    public void Move(string from, string to) => Relocate(from, to, keepSource: false);

    /// <summary>A file or folder was copied: the copy gets the same tags.</summary>
    public void Copy(string from, string to) => Relocate(from, to, keepSource: true);

    void Relocate(string from, string to, bool keepSource)
    {
        from = Normalize(from);
        to = Normalize(to);
        if (string.Equals(from, to, StringComparison.Ordinal)) return;
        var changed = new List<string>();
        lock (_lock)
        {
            var prefix = from + Path.DirectorySeparatorChar;
            foreach (var key in _files.Keys.ToList())
            {
                string target;
                if (string.Equals(key, from, StringComparison.OrdinalIgnoreCase)) target = to;
                else if (key.StartsWith(prefix, StringComparison.OrdinalIgnoreCase)) target = to + key[from.Length..];
                else continue;
                var ids = _files[key];
                if (!keepSource) _files.Remove(key);
                _files[target] = _files.TryGetValue(target, out var existing) ? existing.Union(ids).ToList() : ids.ToList();
                changed.Add(target);
            }
        }
        if (changed.Count == 0) return;
        // A copy to FAT or a move done without the stream (e.g. a temp-file save) gets it back.
        foreach (var p in changed) if (!HasStream(p)) WriteStream(p);
        Commit();
    }

    /// <summary>
    /// A folder was read: take the tags of its items from their streams (files moved here by other programs bring
    /// their tags), restore streams that were lost (an editor that saves through a temporary file), and drop index
    /// entries of items that left the folder (their stream goes with them and is found where they land).
    /// Call from any thread; raises <see cref="Changed"/> only when something differed.
    /// </summary>
    public void Reconcile(string folder, IEnumerable<string> items)
    {
        folder = Normalize(folder);
        var present = new HashSet<string>(items.Select(Normalize), StringComparer.OrdinalIgnoreCase);
        var heal = new List<string>();
        var changed = false;
        foreach (var path in present)
        {
            var fromStream = ReadStream(path);
            lock (_lock)
            {
                _files.TryGetValue(path, out var indexed);
                if (fromStream != null)
                {
                    var ids = new List<string>();
                    foreach (var t in fromStream)
                    {
                        // A tag made on another computer (or deleted here meanwhile) is recreated from the stream.
                        var known = _tags.FirstOrDefault(x => x.Id == t.Id) ?? _tags.FirstOrDefault(x => string.Equals(x.Name, t.Name, StringComparison.CurrentCultureIgnoreCase));
                        if (known == null)
                        {
                            _tags.Add(known = t);
                            changed = true;
                        }
                        if (!ids.Contains(known.Id)) ids.Add(known.Id);
                    }
                    if (indexed == null && ids.Count > 0)
                    {
                        // New here with tags: most likely moved from where the index still has it.
                        var name = System.IO.Path.GetFileName(path);
                        foreach (var key in _files.Keys.ToList())
                            if (string.Equals(System.IO.Path.GetFileName(key), name, StringComparison.OrdinalIgnoreCase) &&
                                !File.Exists(key) && !Directory.Exists(key))
                                _files.Remove(key);
                    }
                    if (indexed == null || !indexed.SequenceEqual(ids))
                    {
                        if (ids.Count == 0) _files.Remove(path);
                        else _files[path] = ids;
                        changed = true;
                    }
                }
                else if (indexed is { Count: > 0 }) heal.Add(path);
            }
        }
        lock (_lock)
        {
            // Items of this folder that are gone: moved (the stream went along) or deleted.
            foreach (var key in _files.Keys.ToList())
                if (string.Equals(Path.GetDirectoryName(key), folder, StringComparison.OrdinalIgnoreCase) && !present.Contains(key) &&
                    !File.Exists(key) && !Directory.Exists(key))
                {
                    _files.Remove(key);
                    changed = true;
                }
        }
        foreach (var p in heal) WriteStream(p);
        if (changed) Commit();
    }

    // ---------- the stream ----------

    /// <summary>Tags in the file's stream; null when the file has no stream (or streams are not supported).</summary>
    static List<TagDef>? ReadStream(string path)
    {
        try
        {
            var stream = path + StreamName;
            if (!File.Exists(stream)) return null;
            var list = new List<TagDef>();
            foreach (var line in File.ReadAllLines(stream, Encoding.UTF8))
            {
                var f = line.Split('\t');
                if (f.Length < 2 || f[0].Length == 0) continue;
                string? Field(int i) => f.Length > i && f[i].Length > 0 ? f[i] : null;
                list.Add(new TagDef { Id = f[0], Name = f[1], Color = Field(2), Icon = Field(3) });
            }
            return list;
        }
        catch
        {
            return null;
        }
    }

    static bool HasStream(string path)
    {
        try { return File.Exists(path + StreamName); }
        catch { return false; }
    }

    /// <summary>Writes the indexed tags of <paramref name="path"/> into its stream, keeping its dates.</summary>
    void WriteStream(string path)
    {
        List<TagDef> tags;
        lock (_lock)
        {
            if (!_files.TryGetValue(path, out var ids)) ids = new List<string>();
            tags = _tags.Where(t => ids.Contains(t.Id)).ToList();
        }
        try
        {
            var isDir = Directory.Exists(path);
            if (!isDir && !File.Exists(path)) return;
            var stream = path + StreamName;
            if (tags.Count == 0 && !File.Exists(stream)) return;
            var info = isDir ? (FileSystemInfo)new DirectoryInfo(path) : new FileInfo(path);
            var written = info.LastWriteTimeUtc;
            var attrs = info.Attributes;
            // A read-only file refuses new streams too: lift the flag for the moment.
            if (attrs.HasFlag(FileAttributes.ReadOnly)) File.SetAttributes(path, attrs & ~FileAttributes.ReadOnly);
            try
            {
                if (tags.Count == 0) File.Delete(stream);
                else
                {
                    var text = string.Join("\n", tags.Select(t => string.Join('\t', t.Id, Clean(t.Name), t.Color ?? "", t.Icon ?? "")));
                    File.WriteAllText(stream, text, new UTF8Encoding(false));
                }
            }
            finally
            {
                // Tagging is not an edit: the "modified" date stays as it was.
                if (isDir) Directory.SetLastWriteTimeUtc(path, written);
                else File.SetLastWriteTimeUtc(path, written);
                if (attrs.HasFlag(FileAttributes.ReadOnly)) File.SetAttributes(path, attrs);
            }
        }
        catch
        {
            // No streams here (FAT, some network shares) or no access: the index alone keeps the tags.
        }
    }

    static string Clean(string s) => s.Replace('\t', ' ').Replace('\n', ' ').Replace('\r', ' ');

    static string Normalize(string path)
    {
        try { return Path.TrimEndingDirectorySeparator(Path.GetFullPath(path)); }
        catch { return path; }
    }

    void Commit()
    {
        string text;
        lock (_lock) text = JsonSerializer.Serialize(new Data { Tags = _tags, Files = _files }, Json);
        try
        {
            Directory.CreateDirectory(Path.GetDirectoryName(_file)!);
            var tmp = _file + ".tmp";
            File.WriteAllText(tmp, text);
            File.Move(tmp, _file, overwrite: true);
        }
        catch { }
        Changed?.Invoke(this, EventArgs.Empty);
    }
}

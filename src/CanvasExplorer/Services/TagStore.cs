using System.IO;
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
/// Tags and which files carry them, in %AppData%\CanvasExplorer\tags.json. Files are keyed by full path;
/// renames and moves made by this app (and renames seen in the open folder) carry the tags along.
/// Thread-safe: the folder is read on a background thread.
/// </summary>
public sealed class TagStore
{
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
        Commit();
    }

    public void Delete(string id)
    {
        lock (_lock)
        {
            _tags.RemoveAll(t => t.Id == id);
            foreach (var key in _files.Keys.ToList())
            {
                _files[key].Remove(id);
                if (_files[key].Count == 0) _files.Remove(key);
            }
        }
        Commit();
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

    /// <summary>Puts the tag on the files (or takes it off).</summary>
    public void Set(IEnumerable<string> paths, string id, bool on)
    {
        lock (_lock)
        {
            if (_tags.All(t => t.Id != id)) return;
            foreach (var raw in paths)
            {
                var p = Normalize(raw);
                if (on)
                {
                    if (!_files.TryGetValue(p, out var ids)) _files[p] = ids = new List<string>();
                    if (!ids.Contains(id)) ids.Add(id);
                }
                else if (_files.TryGetValue(p, out var ids))
                {
                    ids.Remove(id);
                    if (ids.Count == 0) _files.Remove(p);
                }
            }
        }
        Commit();
    }

    /// <summary>Everything that carries the tag and still exists.</summary>
    public IReadOnlyList<string> PathsOf(string id)
    {
        List<string> all;
        lock (_lock) all = _files.Where(kv => kv.Value.Contains(id)).Select(kv => kv.Key).ToList();
        // Deleted files keep their entry (they may come back from the recycle bin) but are not listed.
        return all.Where(p => File.Exists(p) || Directory.Exists(p)).ToList();
    }

    public int Count(string id) => PathsOf(id).Count;

    /// <summary>A file or folder was renamed or moved: its tags (and those of everything inside it) follow.</summary>
    public void Move(string from, string to) => Relocate(from, to, keepSource: false);

    /// <summary>A file or folder was copied: the copy gets the same tags.</summary>
    public void Copy(string from, string to) => Relocate(from, to, keepSource: true);

    void Relocate(string from, string to, bool keepSource)
    {
        from = Normalize(from);
        to = Normalize(to);
        if (string.Equals(from, to, StringComparison.Ordinal)) return;
        var changed = false;
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
                changed = true;
            }
        }
        if (changed) Commit();
    }

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

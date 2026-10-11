using System.IO;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using CanvasExplorer.Models;

namespace CanvasExplorer.Services;

/// <summary>
/// Canvas layout lives in a hidden file inside the folder itself. If the folder is not writable
/// the layout falls back to %LocalAppData%. No file means "no canvas" (plain list).
/// </summary>
public static class LayoutStore
{
    public const string FileName = ".canvas.json";

    static readonly JsonSerializerOptions Json = new()
    {
        WriteIndented = true,
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
        // Keep file names readable in the layout file.
        Encoder = System.Text.Encodings.Web.JavaScriptEncoder.UnsafeRelaxedJsonEscaping,
    };

    static readonly string FallbackDir = Path.Combine(AppSettings.LocalDir, "layouts");

    public static FolderLayout? Load(string folder)
    {
        foreach (var path in new[] { Path.Combine(folder, FileName), FallbackPath(folder) })
        {
            try
            {
                if (File.Exists(path))
                    return JsonSerializer.Deserialize<FolderLayout>(File.ReadAllText(path), Json);
            }
            catch
            {
                // A broken file is treated like a missing one: default layout.
            }
        }
        return null;
    }

    public static void Save(string folder, FolderLayout layout)
    {
        // The folder itself was deleted or renamed: there is nothing to keep a layout for.
        if (!Directory.Exists(folder)) return;
        var text = JsonSerializer.Serialize(layout, Json);
        try
        {
            WriteHidden(Path.Combine(folder, FileName), text);
            TryDelete(FallbackPath(folder));
        }
        catch (Exception ex) when (ex is UnauthorizedAccessException or IOException)
        {
            try
            {
                Directory.CreateDirectory(FallbackDir);
                File.WriteAllText(FallbackPath(folder), text);
            }
            catch { }
        }
    }

    public static void Delete(string folder)
    {
        TryDelete(Path.Combine(folder, FileName));
        TryDelete(FallbackPath(folder));
    }

    static void WriteHidden(string path, string text)
    {
        // FileMode.Create fails on hidden files, so truncate an existing one instead.
        using (var fs = new FileStream(path, FileMode.OpenOrCreate, FileAccess.Write, FileShare.Read))
        {
            fs.SetLength(0);
            var bytes = new UTF8Encoding(false).GetBytes(text);
            fs.Write(bytes, 0, bytes.Length);
        }
        var attrs = File.GetAttributes(path);
        if ((attrs & FileAttributes.Hidden) == 0)
            File.SetAttributes(path, attrs | FileAttributes.Hidden);
    }

    static void TryDelete(string path)
    {
        try
        {
            if (!File.Exists(path)) return;
            File.SetAttributes(path, FileAttributes.Normal);
            File.Delete(path);
        }
        catch { }
    }

    static string FallbackPath(string folder)
    {
        var hash = SHA1.HashData(Encoding.UTF8.GetBytes(folder.ToLowerInvariant()));
        return Path.Combine(FallbackDir, Convert.ToHexString(hash) + ".json");
    }
}

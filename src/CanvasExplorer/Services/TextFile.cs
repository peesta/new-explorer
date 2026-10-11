using System.IO;
using System.Text;

namespace CanvasExplorer.Services;

/// <summary>Reads text files with encoding detection and writes them back in the same encoding.</summary>
public static class TextFile
{
    public const int MaxEditableBytes = 4 * 1024 * 1024;

    public static (string text, Encoding encoding, bool truncated) Read(string path)
    {
        using var fs = new FileStream(path, FileMode.Open, FileAccess.Read, FileShare.ReadWrite | FileShare.Delete);
        var truncated = fs.Length > MaxEditableBytes;
        var bytes = new byte[Math.Min(fs.Length, MaxEditableBytes)];
        fs.ReadExactly(bytes);
        var encoding = Detect(bytes, out var bom);
        return (encoding.GetString(bytes, bom, bytes.Length - bom), encoding, truncated);
    }

    static Encoding Detect(byte[] b, out int bomLength)
    {
        bomLength = 0;
        if (b.Length >= 3 && b[0] == 0xEF && b[1] == 0xBB && b[2] == 0xBF) { bomLength = 3; return new UTF8Encoding(true); }
        if (b.Length >= 2 && b[0] == 0xFF && b[1] == 0xFE) { bomLength = 2; return new UnicodeEncoding(false, true); }
        if (b.Length >= 2 && b[0] == 0xFE && b[1] == 0xFF) { bomLength = 2; return new UnicodeEncoding(true, true); }
        try
        {
            new UTF8Encoding(false, true).GetString(b);
            return new UTF8Encoding(false);
        }
        catch (DecoderFallbackException)
        {
            return Encoding.GetEncoding(1251);
        }
    }

    /// <summary>Writes via a temp file + replace so a crash never leaves a half-written file.</summary>
    public static void Write(string path, string text, Encoding encoding)
    {
        var tmp = path + ".~tmp";
        try
        {
            File.WriteAllText(tmp, text, encoding);
            File.Replace(tmp, path, null);
        }
        catch
        {
            try { if (File.Exists(tmp)) File.Delete(tmp); } catch { }
            File.WriteAllText(path, text, encoding);
        }
    }
}

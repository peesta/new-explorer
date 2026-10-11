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
        // A cut-off file may end in the middle of a multi-byte character: detect on whole characters only.
        var length = truncated ? Utf8Boundary(bytes) : bytes.Length;
        var encoding = Detect(bytes.AsSpan(0, length), out var bom);
        return (encoding.GetString(bytes, bom, length - bom), encoding, truncated);
    }

    /// <summary>Length of the buffer without a trailing incomplete UTF-8 sequence.</summary>
    static int Utf8Boundary(byte[] b)
    {
        var n = b.Length;
        // Step back over continuation bytes (10xxxxxx) to the lead byte of the last character.
        var i = n - 1;
        while (i >= 0 && n - i <= 4 && (b[i] & 0xC0) == 0x80) i--;
        if (i < 0 || n - i > 4) return n;
        var lead = b[i];
        var need = lead < 0x80 ? 1 : (lead & 0xE0) == 0xC0 ? 2 : (lead & 0xF0) == 0xE0 ? 3 : (lead & 0xF8) == 0xF0 ? 4 : 1;
        return n - i < need ? i : n;
    }

    internal static Encoding Detect(ReadOnlySpan<byte> b, out int bomLength)
    {
        bomLength = 0;
        if (b.Length >= 3 && b[0] == 0xEF && b[1] == 0xBB && b[2] == 0xBF) { bomLength = 3; return new UTF8Encoding(true); }
        if (b.Length >= 2 && b[0] == 0xFF && b[1] == 0xFE) { bomLength = 2; return new UnicodeEncoding(false, true); }
        if (b.Length >= 2 && b[0] == 0xFE && b[1] == 0xFF) { bomLength = 2; return new UnicodeEncoding(true, true); }
        try
        {
            new UTF8Encoding(false, true).GetCharCount(b);
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

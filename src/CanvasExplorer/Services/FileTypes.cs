using System.Windows.Media;

namespace CanvasExplorer.Services;

public enum FileKind
{
    Folder, Image, Text, Markdown, Code, Audio, Video, Pdf,
    Archive, Document, Spreadsheet, Presentation, Executable, Other
}

public static class FileTypes
{
    static readonly Dictionary<string, FileKind> Map = new(StringComparer.OrdinalIgnoreCase);

    static FileTypes()
    {
        void Add(FileKind k, params string[] exts) { foreach (var e in exts) Map[e] = k; }
        Add(FileKind.Image, "jpg", "jpeg", "jfif", "png", "gif", "bmp", "tif", "tiff", "ico", "webp", "heic", "heif", "avif");
        Add(FileKind.Text, "txt", "log", "ini", "cfg", "conf", "csv", "tsv", "nfo", "srt");
        Add(FileKind.Markdown, "md", "markdown", "mdown");
        Add(FileKind.Code, "json", "xml", "yaml", "yml", "toml", "cs", "js", "ts", "tsx", "jsx", "py", "html", "htm", "css",
            "scss", "c", "h", "cpp", "hpp", "java", "kt", "go", "rs", "sh", "ps1", "bat", "cmd", "sql", "lua", "rb", "php", "xaml", "csproj", "sln");
        Add(FileKind.Audio, "mp3", "wav", "flac", "ogg", "m4a", "aac", "wma", "opus", "aiff");
        Add(FileKind.Video, "mp4", "mkv", "avi", "mov", "wmv", "webm", "m4v", "mpg", "mpeg");
        Add(FileKind.Pdf, "pdf");
        Add(FileKind.Archive, "zip", "7z", "rar", "tar", "gz", "bz2", "xz", "cab", "iso");
        Add(FileKind.Document, "doc", "docx", "rtf", "odt");
        Add(FileKind.Spreadsheet, "xls", "xlsx", "ods");
        Add(FileKind.Presentation, "ppt", "pptx", "odp");
        Add(FileKind.Executable, "exe", "msi", "lnk", "url", "appx", "msix");
    }

    public static FileKind KindOf(string ext) =>
        ext.Length > 0 && Map.TryGetValue(ext, out var k) ? k : FileKind.Other;

    static readonly Dictionary<FileKind, (Brush bg, Brush fg)> Chips = new()
    {
        [FileKind.Folder] = Pair("#1F2A40", "#A9C1FF"),
        [FileKind.Image] = Pair("#2A2440", "#C9BBFF"),
        [FileKind.Text] = Pair("#2C2C2C", "#D4D4D4"),
        [FileKind.Markdown] = Pair("#363016", "#FBE38E"),
        [FileKind.Code] = Pair("#1E3330", "#8FE3D0"),
        [FileKind.Audio] = Pair("#3B1F33", "#FFB0D8"),
        [FileKind.Video] = Pair("#3A2A16", "#FFC98A"),
        [FileKind.Pdf] = Pair("#3D1F1E", "#FFB1AB"),
        [FileKind.Archive] = Pair("#2E2E2E", "#C8C8C8"),
        [FileKind.Document] = Pair("#1C2A45", "#A6CBFF"),
        [FileKind.Spreadsheet] = Pair("#173324", "#94EDB4"),
        [FileKind.Presentation] = Pair("#3D2616", "#FFB98A"),
        [FileKind.Executable] = Pair("#2B2B35", "#C5C5E0"),
        [FileKind.Other] = Pair("#2A2A2A", "#BDBDBD"),
    };

    public static (Brush bg, Brush fg) ChipBrushes(FileKind kind) => Chips[kind];

    static (Brush, Brush) Pair(string bg, string fg)
    {
        var b = new SolidColorBrush((Color)ColorConverter.ConvertFromString(bg));
        var f = new SolidColorBrush((Color)ColorConverter.ConvertFromString(fg));
        b.Freeze(); f.Freeze();
        return (b, f);
    }

    public static string KindName(FileKind kind, string ext) => kind switch
    {
        FileKind.Folder => "Папка",
        FileKind.Image => "Изображение " + ext,
        FileKind.Text => "Текст",
        FileKind.Markdown => "Markdown",
        FileKind.Code => "Код " + ext,
        FileKind.Audio => "Аудио " + ext,
        FileKind.Video => "Видео " + ext,
        FileKind.Pdf => "Документ PDF",
        FileKind.Archive => "Архив " + ext,
        FileKind.Document => "Документ " + ext,
        FileKind.Spreadsheet => "Таблица " + ext,
        FileKind.Presentation => "Презентация " + ext,
        FileKind.Executable => "Приложение",
        _ => ext.Length > 0 ? "Файл " + ext : "Файл",
    };
}

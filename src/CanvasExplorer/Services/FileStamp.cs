using System.IO;
using System.Windows;

namespace CanvasExplorer.Services;

/// <summary>A version of a file on disk (write time + size): tells whether another program changed it.</summary>
public readonly record struct FileStamp(DateTime WriteUtc, long Length)
{
    public bool Exists => WriteUtc != default;

    public static FileStamp Of(string path)
    {
        try
        {
            var fi = new FileInfo(path);
            return fi.Exists ? new FileStamp(fi.LastWriteTimeUtc, fi.Length) : default;
        }
        catch
        {
            return default;
        }
    }

    /// <summary>
    /// The file changed on disk while the window holds unsaved edits. True = keep the edits and overwrite
    /// the file, false = drop them and load the file.
    /// </summary>
    public static bool AskKeepEdits(string fileName)
    {
        var text = $"Файл «{fileName}» изменён другой программой, а в окне на холсте есть несохранённые правки.\n\n" +
                   "Да — сохранить ваши правки (изменения другой программы пропадут).\n" +
                   "Нет — загрузить файл с диска (ваши правки пропадут).";
        var owner = Application.Current?.MainWindow;
        var result = owner != null
            ? MessageBox.Show(owner, text, "Файл изменён", MessageBoxButton.YesNo, MessageBoxImage.Warning, MessageBoxResult.No)
            : MessageBox.Show(text, "Файл изменён", MessageBoxButton.YesNo, MessageBoxImage.Warning, MessageBoxResult.No);
        return result == MessageBoxResult.Yes;
    }
}

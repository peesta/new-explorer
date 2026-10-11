using System.Collections.Specialized;
using System.Diagnostics;
using System.IO;
using System.Windows;

namespace CanvasExplorer.Services;

/// <summary>File operations that go through the Windows shell (undo, recycle bin, progress UI).</summary>
public static class Shell
{
    const uint FO_MOVE = 1, FO_COPY = 2, FO_DELETE = 3;
    const ushort FOF_RENAMEONCOLLISION = 0x8, FOF_ALLOWUNDO = 0x40, FOF_NOCONFIRMMKDIR = 0x200;

    public static void Open(string path)
    {
        try { Process.Start(new ProcessStartInfo(path) { UseShellExecute = true }); }
        catch (Exception ex) { MessageBox.Show(ex.Message, "Не удалось открыть", MessageBoxButton.OK, MessageBoxImage.Warning); }
    }

    public static void OpenWith(string path)
    {
        try { Process.Start("rundll32.exe", "shell32.dll,OpenAs_RunDLL " + path); }
        catch (Exception ex) { MessageBox.Show(ex.Message); }
    }

    public static void ShowProperties(string path) => Native.SHObjectProperties(IntPtr.Zero, 0x2, path, null);

    public static bool Delete(IEnumerable<string> paths, IntPtr owner) =>
        Run(FO_DELETE, paths, null, FOF_ALLOWUNDO, owner);

    public static bool Copy(IEnumerable<string> paths, string destFolder, IntPtr owner)
    {
        var list = paths.ToList();
        var flags = (ushort)(FOF_ALLOWUNDO | FOF_NOCONFIRMMKDIR);
        if (list.Any(p => SameFolder(Path.GetDirectoryName(p), destFolder)))
            flags |= FOF_RENAMEONCOLLISION;
        return Run(FO_COPY, list, destFolder, flags, owner);
    }

    public static bool Move(IEnumerable<string> paths, string destFolder, IntPtr owner) =>
        Run(FO_MOVE, paths, destFolder, (ushort)(FOF_ALLOWUNDO | FOF_NOCONFIRMMKDIR), owner);

    static bool Run(uint func, IEnumerable<string> paths, string? to, ushort flags, IntPtr owner)
    {
        var from = string.Join("\0", paths);
        if (from.Length == 0) return false;
        var op = new Native.SHFILEOPSTRUCT
        {
            hwnd = owner,
            wFunc = func,
            pFrom = from + "\0\0",
            pTo = to == null ? null : to + "\0\0",
            fFlags = flags,
        };
        return Native.SHFileOperation(ref op) == 0 && !op.fAnyOperationsAborted;
    }

    /// <summary>Explorer rules: Ctrl copies, Shift moves, otherwise move on the same drive and copy across drives.</summary>
    public static DragDropEffects DropEffect(string[] files, string destFolder, DragDropKeyStates keys, DragDropEffects allowed)
    {
        var real = files.Where(f => !SameFolder(Path.GetDirectoryName(f), destFolder) &&
                                    !SameFolder(f, destFolder)).ToList();
        if (real.Count == 0) return DragDropEffects.None;
        DragDropEffects want;
        if ((keys & DragDropKeyStates.ControlKey) != 0) want = DragDropEffects.Copy;
        else if ((keys & DragDropKeyStates.ShiftKey) != 0) want = DragDropEffects.Move;
        else
        {
            var sameDrive = real.All(f => string.Equals(Path.GetPathRoot(f), Path.GetPathRoot(destFolder), StringComparison.OrdinalIgnoreCase));
            want = sameDrive ? DragDropEffects.Move : DragDropEffects.Copy;
        }
        if ((allowed & want) != 0) return want;
        return (allowed & DragDropEffects.Copy) != 0 ? DragDropEffects.Copy : DragDropEffects.None;
    }

    /// <summary>Performs a file drop into <paramref name="destFolder"/>; returns the effect applied.</summary>
    public static DragDropEffects Drop(DragEventArgs e, string destFolder, IntPtr owner)
    {
        if (e.Data.GetData(DataFormats.FileDrop) is not string[] files) return DragDropEffects.None;
        var effect = DropEffect(files, destFolder, e.KeyStates, e.AllowedEffects);
        var real = files.Where(f => !SameFolder(Path.GetDirectoryName(f), destFolder) && !SameFolder(f, destFolder)).ToList();
        if (effect == DragDropEffects.Move) Move(real, destFolder, owner);
        else if (effect == DragDropEffects.Copy) Copy(real, destFolder, owner);
        return effect;
    }

    public static bool SameFolder(string? a, string? b) =>
        a != null && b != null &&
        string.Equals(Path.TrimEndingDirectorySeparator(a), Path.TrimEndingDirectorySeparator(b), StringComparison.OrdinalIgnoreCase);

    // Clipboard in the same format Explorer uses, so cut/copy/paste works across both apps.
    public static void SetClipboard(IEnumerable<string> paths, bool cut)
    {
        var files = new StringCollection();
        files.AddRange(paths.ToArray());
        if (files.Count == 0) return;
        var data = new DataObject();
        data.SetFileDropList(files);
        data.SetData("Preferred DropEffect", new MemoryStream(BitConverter.GetBytes(cut ? 2 : 5)));
        Clipboard.SetDataObject(data, true);
    }

    public static bool CanPaste()
    {
        try { return Clipboard.ContainsFileDropList(); } catch { return false; }
    }

    public static void Paste(string destFolder, IntPtr owner)
    {
        if (!CanPaste()) return;
        var files = Clipboard.GetFileDropList().Cast<string>().ToArray();
        var cut = false;
        if (Clipboard.GetData("Preferred DropEffect") is MemoryStream ms)
        {
            var b = new byte[4];
            if (ms.Read(b, 0, 4) == 4) cut = (BitConverter.ToInt32(b, 0) & 2) != 0;
        }
        if (cut)
        {
            if (Move(files, destFolder, owner)) Clipboard.Clear();
        }
        else Copy(files, destFolder, owner);
    }
}

using System.IO;
using System.Runtime.CompilerServices;
using System.Runtime.InteropServices;

namespace CanvasExplorer.Tests.Infrastructure;

static class TestSetup
{
    /// <summary>Scratch root for everything the tests write; never the user's folders or settings.</summary>
    public static readonly string Root = Path.Combine(Path.GetTempPath(), "CanvasExplorerTests");

    [ModuleInitializer]
    internal static void Init()
    {
        // Code under test that touches %AppData% / %LocalAppData% goes to a scratch folder instead.
        Environment.SetEnvironmentVariable("CANVAS_EXPLORER_DATA", Path.Combine(Root, "unit-data"));
        // Screen coordinates from UI Automation and for the mouse must be the same physical pixels.
        SetProcessDpiAwarenessContext(new IntPtr(-4));
        System.Text.Encoding.RegisterProvider(System.Text.CodePagesEncodingProvider.Instance);
    }

    [DllImport("user32.dll")]
    static extern bool SetProcessDpiAwarenessContext(IntPtr value);

    /// <summary>A fresh empty folder under the scratch root.</summary>
    public static string NewFolder(string prefix)
    {
        var path = Path.Combine(Root, prefix + "-" + Guid.NewGuid().ToString("N")[..8]);
        Directory.CreateDirectory(path);
        return path;
    }

    public static void DeleteFolder(string path)
    {
        for (var i = 0; i < 10; i++)
        {
            try
            {
                if (!Directory.Exists(path)) return;
                foreach (var f in Directory.EnumerateFiles(path, "*", SearchOption.AllDirectories))
                    File.SetAttributes(f, FileAttributes.Normal);
                Directory.Delete(path, true);
                return;
            }
            catch (IOException) { Thread.Sleep(200); }
            catch (UnauthorizedAccessException) { Thread.Sleep(200); }
        }
    }
}

/// <summary>Runs WPF code on a dedicated STA thread (FlowDocument and friends require it).</summary>
static class Sta
{
    public static T Run<T>(Func<T> func)
    {
        T result = default!;
        Exception? error = null;
        var thread = new Thread(() =>
        {
            try { result = func(); }
            catch (Exception ex) { error = ex; }
        });
        thread.SetApartmentState(ApartmentState.STA);
        thread.Start();
        thread.Join();
        if (error != null) ExceptionDispatchInfoThrow(error);
        return result;
    }

    public static void Run(Action action) => Run(() => { action(); return 0; });

    static void ExceptionDispatchInfoThrow(Exception ex) =>
        System.Runtime.ExceptionServices.ExceptionDispatchInfo.Capture(ex).Throw();
}

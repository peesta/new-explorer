using System.Collections.Concurrent;
using System.Runtime.InteropServices;
using System.Windows.Media;
using System.Windows.Media.Imaging;

namespace CanvasExplorer.Services;

/// <summary>
/// Pictures for the tile view, made by Windows itself (IShellItemImageFactory): works for photos and for videos
/// that have a thumbnail handler, and uses the system thumbnail cache. Decoding runs on two background STA threads;
/// results are cached by path + modification time.
/// </summary>
public static class ShellThumbnails
{
    const int MaxCached = 600;

    sealed record Job(string Path, DateTime Stamp, int Size, TaskCompletionSource<BitmapSource?> Done, CancellationToken Token);

    static readonly BlockingCollection<Job> Queue = new(new ConcurrentStack<Job>());
    static readonly Dictionary<string, BitmapSource?> Cache = new(StringComparer.OrdinalIgnoreCase);
    static readonly LinkedList<string> Order = new();

    static ShellThumbnails()
    {
        for (var i = 0; i < 2; i++)
        {
            var t = new Thread(Work) { IsBackground = true, Name = "Thumbnails" + i, Priority = ThreadPriority.BelowNormal };
            t.SetApartmentState(ApartmentState.STA);
            t.Start();
        }
    }

    static string Key(string path, DateTime stamp, int size) => path + "|" + stamp.Ticks + "|" + size;

    /// <summary>A thumbnail at most <paramref name="size"/> px on its long side, or null if Windows has none.</summary>
    public static Task<BitmapSource?> GetAsync(string path, DateTime modified, int size, CancellationToken token)
    {
        var key = Key(path, modified, size);
        lock (Cache)
        {
            if (Cache.TryGetValue(key, out var hit)) return Task.FromResult(hit);
        }
        var job = new Job(path, modified, size, new TaskCompletionSource<BitmapSource?>(TaskCreationOptions.RunContinuationsAsynchronously), token);
        // A stack: the most recently requested (the folder just opened) are made first.
        Queue.Add(job);
        return job.Done.Task;
    }

    static void Work()
    {
        foreach (var job in Queue.GetConsumingEnumerable())
        {
            if (job.Token.IsCancellationRequested)
            {
                job.Done.TrySetResult(null);
                continue;
            }
            var key = Key(job.Path, job.Stamp, job.Size);
            BitmapSource? result;
            lock (Cache) Cache.TryGetValue(key, out result);
            if (result == null)
            {
                result = Make(job.Path, job.Size);
                lock (Cache)
                {
                    if (!Cache.ContainsKey(key)) Order.AddLast(key);
                    Cache[key] = result;
                    while (Order.Count > MaxCached)
                    {
                        Cache.Remove(Order.First!.Value);
                        Order.RemoveFirst();
                    }
                }
            }
            job.Done.TrySetResult(result);
        }
    }

    static BitmapSource? Make(string path, int size)
    {
        IntPtr hbm = IntPtr.Zero;
        try
        {
            var iid = typeof(IShellItemImageFactory).GUID;
            SHCreateItemFromParsingName(path, IntPtr.Zero, ref iid, out var factory);
            try
            {
                // Thumbnail only: no generic file-type icon when there is no picture.
                if (factory.GetImage(new SIZE { cx = size, cy = size }, SIIGBF_THUMBNAILONLY | SIIGBF_BIGGERSIZEOK, out hbm) != 0 || hbm == IntPtr.Zero)
                    return null;
            }
            finally
            {
                Marshal.ReleaseComObject(factory);
            }
            return ToBitmap(hbm, size);
        }
        catch
        {
            return null;
        }
        finally
        {
            if (hbm != IntPtr.Zero) DeleteObject(hbm);
        }
    }

    static BitmapSource? ToBitmap(IntPtr hbm, int maxSize)
    {
        if (GetObject(hbm, Marshal.SizeOf<BITMAP>(), out var bm) == 0 || bm.bmWidth <= 0 || bm.bmHeight <= 0) return null;
        int w = bm.bmWidth, h = Math.Abs(bm.bmHeight);
        var info = new BITMAPINFOHEADER { biSize = Marshal.SizeOf<BITMAPINFOHEADER>(), biWidth = w, biHeight = -h, biPlanes = 1, biBitCount = 32 };
        var pixels = new byte[w * h * 4];
        var dc = GetDC(IntPtr.Zero);
        try
        {
            if (GetDIBits(dc, hbm, 0, (uint)h, pixels, ref info, 0) == 0) return null;
        }
        finally
        {
            ReleaseDC(IntPtr.Zero, dc);
        }
        // Photos come without alpha (all zero): treat as opaque; otherwise alpha is premultiplied.
        var hasAlpha = false;
        for (var i = 3; i < pixels.Length; i += 4)
            if (pixels[i] != 0) { hasAlpha = true; break; }
        BitmapSource bmp = BitmapSource.Create(w, h, 96, 96, hasAlpha ? PixelFormats.Pbgra32 : PixelFormats.Bgr32, null, pixels, w * 4);
        // "Bigger size OK" may return the cached large thumbnail: scale it down to what the tile needs.
        var scale = (double)maxSize / Math.Max(w, h);
        if (scale < 0.95) bmp = new TransformedBitmap(bmp, new ScaleTransform(scale, scale));
        bmp.Freeze();
        return bmp;
    }

    // ---------- Win32 ----------

    const int SIIGBF_BIGGERSIZEOK = 0x1, SIIGBF_THUMBNAILONLY = 0x8;

    [StructLayout(LayoutKind.Sequential)]
    struct SIZE { public int cx, cy; }

    [ComImport, Guid("bcc18b79-ba16-442f-80c4-8a59c30c463b"), InterfaceType(ComInterfaceType.InterfaceIsIUnknown)]
    interface IShellItemImageFactory
    {
        [PreserveSig] int GetImage(SIZE size, int flags, out IntPtr phbm);
    }

    [StructLayout(LayoutKind.Sequential)]
    struct BITMAP { public int bmType, bmWidth, bmHeight, bmWidthBytes; public ushort bmPlanes, bmBitsPixel; public IntPtr bmBits; }

    [StructLayout(LayoutKind.Sequential)]
    struct BITMAPINFOHEADER
    {
        public int biSize, biWidth, biHeight;
        public short biPlanes, biBitCount;
        public int biCompression, biSizeImage, biXPelsPerMeter, biYPelsPerMeter, biClrUsed, biClrImportant;
    }

    [DllImport("shell32.dll", CharSet = CharSet.Unicode, PreserveSig = false)]
    static extern void SHCreateItemFromParsingName(string path, IntPtr bindCtx, ref Guid riid, out IShellItemImageFactory factory);

    [DllImport("gdi32.dll")] static extern int GetObject(IntPtr h, int size, out BITMAP bm);
    [DllImport("gdi32.dll")] static extern int GetDIBits(IntPtr dc, IntPtr hbm, uint start, uint lines, byte[] bits, ref BITMAPINFOHEADER info, uint usage);
    [DllImport("gdi32.dll")] static extern bool DeleteObject(IntPtr h);
    [DllImport("user32.dll")] static extern IntPtr GetDC(IntPtr hwnd);
    [DllImport("user32.dll")] static extern int ReleaseDC(IntPtr hwnd, IntPtr dc);
}

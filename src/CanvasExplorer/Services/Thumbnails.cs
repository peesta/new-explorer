using System.IO;
using System.Windows.Media.Imaging;

namespace CanvasExplorer.Services;

/// <summary>Small decoded previews for hover tooltips, with a little cache.</summary>
public static class Thumbnails
{
    public sealed record Thumb(BitmapSource Image, int Width, int Height);

    const int MaxCached = 64;
    const int DecodeWidth = 520;
    static readonly Dictionary<string, (DateTime stamp, Thumb thumb)> Cache = new(StringComparer.OrdinalIgnoreCase);
    static readonly Queue<string> Order = new();

    public static Thumb? Get(string path, DateTime modified)
    {
        if (Cache.TryGetValue(path, out var hit) && hit.stamp == modified) return hit.thumb;
        try
        {
            using var fs = new FileStream(path, FileMode.Open, FileAccess.Read, FileShare.ReadWrite | FileShare.Delete);
            var decoder = BitmapDecoder.Create(fs, BitmapCreateOptions.DelayCreation, BitmapCacheOption.None);
            var frame = decoder.Frames[0];
            int w = frame.PixelWidth, h = frame.PixelHeight;
            fs.Position = 0;
            var bi = new BitmapImage();
            bi.BeginInit();
            bi.StreamSource = fs;
            bi.CacheOption = BitmapCacheOption.OnLoad;
            if (w > DecodeWidth) bi.DecodePixelWidth = DecodeWidth;
            bi.EndInit();
            bi.Freeze();
            var thumb = new Thumb(bi, w, h);
            Cache[path] = (modified, thumb);
            Order.Enqueue(path);
            while (Order.Count > MaxCached) Cache.Remove(Order.Dequeue());
            return thumb;
        }
        catch
        {
            return null;
        }
    }
}

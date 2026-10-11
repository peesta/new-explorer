using System.IO;

namespace CanvasExplorer.Tests.Infrastructure;

/// <summary>Small media files made on the fly (the repository keeps no video samples).</summary>
static class TestMedia
{
    /// <summary>A quiet tone, 8 kHz mono PCM.</summary>
    public static void Wav(string path, int seconds)
    {
        const int rate = 8000;
        var samples = rate * seconds;
        using var w = new BinaryWriter(File.Create(path));
        w.Write("RIFF"u8); w.Write(36 + samples * 2); w.Write("WAVE"u8);
        w.Write("fmt "u8); w.Write(16); w.Write((short)1); w.Write((short)1); w.Write(rate); w.Write(rate * 2); w.Write((short)2); w.Write((short)16);
        w.Write("data"u8); w.Write(samples * 2);
        for (var i = 0; i < samples; i++) w.Write((short)(Math.Sin(i * 2 * Math.PI * 330 / rate) * 300));
    }

    /// <summary>
    /// An uncompressed AVI (RGB24, <paramref name="fps"/> frames a second): a coloured square moving over a
    /// gradient, so frames differ. Every Windows can play it without codecs.
    /// </summary>
    public static void Avi(string path, int seconds, int width = 64, int height = 48, int fps = 10)
    {
        var frames = seconds * fps;
        var rowBytes = width * 3;
        var frameBytes = rowBytes * height;
        using var fs = File.Create(path);
        using var w = new BinaryWriter(fs);

        void Fourcc(string s) => w.Write(System.Text.Encoding.ASCII.GetBytes(s));

        Fourcc("RIFF");
        var riffSizePos = fs.Position; w.Write(0);
        Fourcc("AVI ");

        // hdrl
        Fourcc("LIST"); w.Write(4 + 8 + 56 + 8 + 4 + 8 + 56 + 8 + 40); Fourcc("hdrl");
        Fourcc("avih"); w.Write(56);
        w.Write(1_000_000 / fps); w.Write(frameBytes * fps); w.Write(0); w.Write(0x10); // has index
        w.Write(frames); w.Write(0); w.Write(1); w.Write(frameBytes); w.Write(width); w.Write(height);
        w.Write(0); w.Write(0); w.Write(0); w.Write(0);
        Fourcc("LIST"); w.Write(4 + 8 + 56 + 8 + 40); Fourcc("strl");
        Fourcc("strh"); w.Write(56);
        Fourcc("vids"); Fourcc("DIB "); w.Write(0); w.Write((short)0); w.Write((short)0); w.Write(0);
        w.Write(1); w.Write(fps); w.Write(0); w.Write(frames); w.Write(frameBytes); w.Write(-1); w.Write(0);
        w.Write((short)0); w.Write((short)0); w.Write((short)width); w.Write((short)height);
        Fourcc("strf"); w.Write(40);
        w.Write(40); w.Write(width); w.Write(height); w.Write((short)1); w.Write((short)24); w.Write(0); w.Write(frameBytes);
        w.Write(0); w.Write(0); w.Write(0); w.Write(0);

        // movi
        Fourcc("LIST");
        var moviSizePos = fs.Position; w.Write(0);
        var moviStart = fs.Position;
        Fourcc("movi");
        var offsets = new List<long>();
        var pixels = new byte[frameBytes];
        for (var f = 0; f < frames; f++)
        {
            var sq = f * 2 % (width - 12);
            for (var y = 0; y < height; y++)
                for (var x = 0; x < width; x++)
                {
                    var i = y * rowBytes + x * 3;
                    var inSquare = x >= sq && x < sq + 12 && y >= 18 && y < 30;
                    pixels[i] = (byte)(inSquare ? 40 : 160 - y);       // B
                    pixels[i + 1] = (byte)(inSquare ? 200 : 60 + x);   // G
                    pixels[i + 2] = (byte)(inSquare ? 250 : 40);       // R
                }
            offsets.Add(fs.Position - moviStart);
            Fourcc("00db"); w.Write(frameBytes); w.Write(pixels);
        }
        var moviEnd = fs.Position;
        fs.Position = moviSizePos; w.Write((int)(moviEnd - moviStart));
        fs.Position = moviEnd;

        Fourcc("idx1"); w.Write(frames * 16);
        foreach (var o in offsets) { Fourcc("00db"); w.Write(0x10); w.Write((int)o); w.Write(frameBytes); }
        var end = fs.Position;
        fs.Position = riffSizePos; w.Write((int)(end - 8));
    }
}

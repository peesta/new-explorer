using CanvasExplorer.Models;
using CanvasExplorer.Services;

namespace CanvasExplorer.Previews;

public static class PreviewFactory
{
    // Formats Media Foundation can decode out of the box.
    static readonly HashSet<string> PlayableAudio = new(StringComparer.OrdinalIgnoreCase)
        { "MP3", "WAV", "M4A", "AAC", "WMA", "FLAC", "AIFF" };

    public static PreviewContent Create(FileItem item) => item.Kind switch
    {
        FileKind.Image => new ImagePreview(item),
        FileKind.Markdown => new MarkdownPreview(item),
        FileKind.Text or FileKind.Code => new TextPreview(item),
        FileKind.Audio when PlayableAudio.Contains(item.Ext) => new AudioPreview(item),
        FileKind.Pdf => new PdfPreview(item),
        _ => new GenericPreview(item),
    };
}

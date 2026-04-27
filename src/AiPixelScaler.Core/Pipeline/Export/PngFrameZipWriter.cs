using System.IO.Compression;
using SixLabors.ImageSharp;
using SixLabors.ImageSharp.Formats.Png;
using SixLabors.ImageSharp.PixelFormats;

namespace AiPixelScaler.Core.Pipeline.Export;

public static class PngFrameZipWriter
{
    public static void Write(
        IReadOnlyList<(string entryName, Image<Rgba32> image)> frames,
        Stream destinationStream,
        PngEncoder? encoder = null)
    {
        encoder ??= new PngEncoder();
        using var zip = new ZipArchive(destinationStream, ZipArchiveMode.Create, leaveOpen: true);
        foreach (var (name, image) in frames)
        {
            if (string.IsNullOrEmpty(name) || name.IndexOfAny(Path.GetInvalidFileNameChars()) >= 0)
                continue;
            var fileName = name;
            if (!fileName.EndsWith(".png", StringComparison.OrdinalIgnoreCase))
                fileName += ".png";
            var e = zip.CreateEntry(fileName, CompressionLevel.Fastest);
            using (var s = e.Open())
                image.Save(s, encoder);
        }
    }
}

using SixLabors.ImageSharp;
using SixLabors.ImageSharp.PixelFormats;
using SixLabors.ImageSharp.Processing;

namespace AiPixelScaler.Core.Pipeline.Export;

public static class AtlasPacker
{
    public sealed class PackedLayout
    {
        public required Image<Rgba32> Atlas { get; init; }
        public required IReadOnlyList<(string id, int x, int y, int w, int h)> Placements { get; init; }
    }

    /// <summary>Pack orizzontale semplice (una riga).</summary>
    public static PackedLayout PackRow(IReadOnlyList<(string id, Image<Rgba32> img)> items)
    {
        if (items.Count == 0)
            // ImageSharp non supporta immagini 0x0: per un atlas "vuoto" usiamo un
            // canvas trasparente minimo 1x1 e nessun placement.
            return new PackedLayout { Atlas = new Image<Rgba32>(1, 1, new Rgba32(0, 0, 0, 0)), Placements = Array.Empty<(string, int, int, int, int)>() };

        var totalW = 0;
        var maxH = 0;
        foreach (var (_, img) in items)
        {
            totalW += img.Width;
            if (img.Height > maxH) maxH = img.Height;
        }

        var atlas = new Image<Rgba32>(totalW, maxH, new Rgba32(0, 0, 0, 0));
        var placements = new List<(string id, int x, int y, int w, int h)>(items.Count);
        var x0 = 0;
        foreach (var (id, img) in items)
        {
            var y0 = (maxH - img.Height) / 2;
            atlas.Mutate(c => c.DrawImage(img, new Point(x0, y0), 1f));
            placements.Add((id, x0, y0, img.Width, img.Height));
            x0 += img.Width;
        }

        return new PackedLayout { Atlas = atlas, Placements = placements };
    }
}

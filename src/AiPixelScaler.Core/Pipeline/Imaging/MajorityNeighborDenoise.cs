using SixLabors.ImageSharp;
using SixLabors.ImageSharp.PixelFormats;

namespace AiPixelScaler.Core.Pipeline.Imaging;

/// <summary>
/// Per ogni pixel interno, se ha meno di <paramref name="minSameNeighbors"/> vicini 8-connessi
/// con lo stesso colore (RGB+A), viene sostituito con il colore più frequente tra i vicini
/// (escluso il centro). Utile per spegnere pixel isolati rispetto al colore locale.
/// </summary>
public static class MajorityNeighborDenoise
{
    public static void ApplyInPlace(Image<Rgba32> image, int minSameNeighbors = 2)
    {
        if (image.Width < 3 || image.Height < 3 || minSameNeighbors < 1) return;

        using var copy = image.Clone();
        var w = image.Width;
        var h = image.Height;

        for (var y = 1; y < h - 1; y++)
        {
            for (var x = 1; x < w - 1; x++)
            {
                var current = copy[x, y];
                var same = 0;
                for (var ny = -1; ny <= 1; ny++)
                for (var nx = -1; nx <= 1; nx++)
                {
                    if (nx == 0 && ny == 0) continue;
                    if (copy[x + nx, y + ny].Equals(current)) same++;
                }

                if (same < minSameNeighbors)
                    image[x, y] = MostCommonNeighbor(copy, x, y);
            }
        }
    }

    private static Rgba32 MostCommonNeighbor(Image<Rgba32> img, int x, int y)
    {
        var counts = new Dictionary<Rgba32, int>();
        for (var ny = -1; ny <= 1; ny++)
        for (var nx = -1; nx <= 1; nx++)
        {
            if (nx == 0 && ny == 0) continue;
            var px = img[x + nx, y + ny];
            counts.TryGetValue(px, out var c);
            counts[px] = c + 1;
        }

        return counts.OrderByDescending(kv => kv.Value).First().Key;
    }
}

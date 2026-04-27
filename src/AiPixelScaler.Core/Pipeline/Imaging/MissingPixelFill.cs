using SixLabors.ImageSharp;
using SixLabors.ImageSharp.PixelFormats;

namespace AiPixelScaler.Core.Pipeline.Imaging;

/// <summary>
/// Riempie piccoli buchi trasparenti interni usando voto di maggioranza dei vicini opachi.
/// Non processa il bordo esterno dell'immagine.
/// </summary>
public static class MissingPixelFill
{
    public static int FillInternalTransparentInPlace(Image<Rgba32> image, int iterations = 1)
    {
        if (image.Width < 3 || image.Height < 3 || iterations < 1) return 0;

        var totalFilled = 0;
        for (var it = 0; it < iterations; it++)
        {
            using var src = image.Clone();
            var filledThisPass = 0;

            for (var y = 1; y < image.Height - 1; y++)
            {
                for (var x = 1; x < image.Width - 1; x++)
                {
                    if (src[x, y].A != 0) continue;
                    if (!TryVote3x3(src, x, y, out var voted) && !TryVote5x5(src, x, y, out voted))
                        continue;

                    image[x, y] = new Rgba32(voted.R, voted.G, voted.B, 255);
                    filledThisPass++;
                }
            }

            totalFilled += filledThisPass;
            if (filledThisPass == 0) break;
        }

        return totalFilled;
    }

    private static bool TryVote3x3(Image<Rgba32> img, int x, int y, out Rgba32 color)
    {
        var counts = new Dictionary<Rgba32, int>();
        for (var ny = -1; ny <= 1; ny++)
        {
            for (var nx = -1; nx <= 1; nx++)
            {
                if (nx == 0 && ny == 0) continue;
                var p = img[x + nx, y + ny];
                if (p.A == 0) continue;
                counts.TryGetValue(p, out var c);
                counts[p] = c + 1;
            }
        }

        if (counts.Count == 0)
        {
            color = default;
            return false;
        }

        color = counts.OrderByDescending(kv => kv.Value).First().Key;
        return true;
    }

    private static bool TryVote5x5(Image<Rgba32> img, int x, int y, out Rgba32 color)
    {
        var counts = new Dictionary<Rgba32, int>();
        for (var ny = -2; ny <= 2; ny++)
        {
            var yy = y + ny;
            if (yy <= 0 || yy >= img.Height - 1) continue;
            for (var nx = -2; nx <= 2; nx++)
            {
                var xx = x + nx;
                if ((nx == 0 && ny == 0) || xx <= 0 || xx >= img.Width - 1) continue;
                var p = img[xx, yy];
                if (p.A == 0) continue;
                counts.TryGetValue(p, out var c);
                counts[p] = c + 1;
            }
        }

        if (counts.Count == 0)
        {
            color = default;
            return false;
        }

        color = counts.OrderByDescending(kv => kv.Value).First().Key;
        return true;
    }
}

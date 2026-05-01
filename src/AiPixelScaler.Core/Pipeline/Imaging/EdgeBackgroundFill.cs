using SixLabors.ImageSharp;
using SixLabors.ImageSharp.PixelFormats;

namespace AiPixelScaler.Core.Pipeline.Imaging;

/// <summary>
/// Rimuove lo sfondo con flood fill non ricorsivo dai bordi immagine verso l'interno,
/// fermando la propagazione sui bordi rilevati via Sobel.
/// </summary>
public static class EdgeBackgroundFill
{
    public enum Metric { EuclideanRgb, OklabPerceptual }

    public static void ApplyInPlace(
        Image<Rgba32> image,
        Rgba32 key,
        double tolerance = 0,
        Metric metric = Metric.EuclideanRgb)
    {
        var w = image.Width;
        var h = image.Height;
        if (w < 1 || h < 1) return;

        var __ = metric;
        var colorTolerance = Math.Max(0, tolerance);
        var colorToleranceSq = colorTolerance * colorTolerance;
        var edgeThreshold = tolerance <= 0 ? 48.0 : Math.Clamp(tolerance, 8.0, 255.0);
        var edge = BuildSobelEdgeMap(image, edgeThreshold);
        var snap = ImageUtils.ToFlatArray(image);
        var removed = new bool[w * h];
        var q = new Queue<(int x, int y)>();

        int I(int x, int y) => y * w + x;
        bool MatchesSeedColor(int x, int y)
        {
            var p = snap[I(x, y)];
            var dr = p.R - key.R;
            var dg = p.G - key.G;
            var db = p.B - key.B;
            return dr * dr + dg * dg + db * db <= colorToleranceSq;
        }

        bool CanFlood(int x, int y) => snap[I(x, y)].A != 0 && !edge[I(x, y)] && MatchesSeedColor(x, y);

        void Seed(int x, int y)
        {
            var i = I(x, y);
            if (removed[i] || !CanFlood(x, y)) return;
            removed[i] = true;
            q.Enqueue((x, y));
        }

        for (var x = 0; x < w; x++)
        {
            Seed(x, 0);
            if (h > 1) Seed(x, h - 1);
        }
        for (var y = 0; y < h; y++)
        {
            Seed(0, y);
            if (w > 1) Seed(w - 1, y);
        }

        void TryAdd(int x, int y)
        {
            if (x < 0 || y < 0 || x >= w || y >= h) return;
            var i = I(x, y);
            if (removed[i] || !CanFlood(x, y)) return;
            removed[i] = true;
            q.Enqueue((x, y));
        }

        while (q.Count > 0)
        {
            var (x, y) = q.Dequeue();
            TryAdd(x - 1, y);
            TryAdd(x + 1, y);
            TryAdd(x, y - 1);
            TryAdd(x, y + 1);
        }

        image.ProcessPixelRows(accessor =>
        {
            for (var y = 0; y < accessor.Height; y++)
            {
                var row = accessor.GetRowSpan(y);
                for (var x = 0; x < row.Length; x++)
                {
                    if (!removed[I(x, y)]) continue;
                    var p = row[x];
                    row[x] = new Rgba32(p.R, p.G, p.B, 0);
                }
            }
        });
    }

    private static bool[] BuildSobelEdgeMap(Image<Rgba32> image, double threshold)
    {
        var w = image.Width;
        var h = image.Height;
        var pixels = ImageUtils.ToFlatArray(image);
        var luma = new double[w * h];
        var edge = new bool[w * h];

        for (var i = 0; i < pixels.Length; i++)
        {
            var p = pixels[i];
            luma[i] = p.A == 0 ? 0 : 0.299 * p.R + 0.587 * p.G + 0.114 * p.B;
        }

        int I(int x, int y) => y * w + x;
        for (var y = 1; y < h - 1; y++)
        {
            for (var x = 1; x < w - 1; x++)
            {
                var gx =
                    -luma[I(x - 1, y - 1)] + luma[I(x + 1, y - 1)] +
                    -2 * luma[I(x - 1, y)] + 2 * luma[I(x + 1, y)] +
                    -luma[I(x - 1, y + 1)] + luma[I(x + 1, y + 1)];
                var gy =
                    luma[I(x - 1, y - 1)] + 2 * luma[I(x, y - 1)] + luma[I(x + 1, y - 1)] -
                    luma[I(x - 1, y + 1)] - 2 * luma[I(x, y + 1)] - luma[I(x + 1, y + 1)];

                edge[I(x, y)] = Math.Sqrt(gx * gx + gy * gy) >= threshold;
            }
        }

        return edge;
    }
}

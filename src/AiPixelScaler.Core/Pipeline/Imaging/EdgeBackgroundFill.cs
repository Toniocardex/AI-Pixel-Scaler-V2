using AiPixelScaler.Core.Color;
using SixLabors.ImageSharp;
using SixLabors.ImageSharp.PixelFormats;

namespace AiPixelScaler.Core.Pipeline.Imaging;

/// <summary>
/// Rimuove lo sfondo "attaccato ai bordi": BFS 4-conn da ogni pixel di bordo che corrisponde
/// al colore <paramref name="key"/> entro <paramref name="tolerance"/>, e propaga la rimozione
/// verso l'interno.
///
/// Differenza chiave rispetto a <see cref="ChromaKey"/>: i pixel del colore-key all'INTERNO
/// del soggetto (non connessi al bordo) vengono PRESERVATI. Tipico fix per "buchi" nel sprite
/// quando il colore della pelle/vestito è simile allo sfondo.
///
/// Metriche: Euclidean RGB (veloce) o Oklab (preciso/percettivo).
/// </summary>
public static class EdgeBackgroundFill
{
    public enum Metric { EuclideanRgb, OklabPerceptual }

    public static void ApplyInPlace(Image<Rgba32> image, Rgba32 key, double tolerance = 0,
                                     Metric metric = Metric.EuclideanRgb)
    {
        if (tolerance < 0) tolerance = 0;
        var w = image.Width;
        var h = image.Height;
        if (w < 1 || h < 1) return;

        var removed = new bool[w * h];
        var q = new Queue<(int x, int y)>();
        int I(int x, int y) => y * w + x;

        var snap = ImageUtils.ToFlatArray(image);
        Oklab[]? snapOk = null;
        Oklab keyOk = default;
        if (metric == Metric.OklabPerceptual)
        {
            snapOk = new Oklab[w * h];
            for (var i = 0; i < snap.Length; i++)
                if (snap[i].A != 0) snapOk[i] = Oklab.FromSrgb(snap[i]);
            keyOk = Oklab.FromSrgb(key);
        }

        var tolSq = tolerance * tolerance;
        bool KeyMatch(int x, int y)
        {
            var p = snap[I(x, y)];
            if (p.A == 0) return true;
            if (metric == Metric.EuclideanRgb)
            {
                var dr = p.R - key.R;
                var dg = p.G - key.G;
                var db = p.B - key.B;
                return dr * dr + dg * dg + db * db <= tolSq;
            }
            return Oklab.DistanceSquared(keyOk, snapOk![I(x, y)]) <= (float)tolSq;
        }

        for (var x = 0; x < w; x++)
        {
            if (KeyMatch(x, 0))     { removed[I(x, 0)]     = true; q.Enqueue((x, 0)); }
            if (h > 1 && KeyMatch(x, h - 1)) { removed[I(x, h - 1)] = true; q.Enqueue((x, h - 1)); }
        }
        for (var y = 0; y < h; y++)
        {
            if (KeyMatch(0, y))     { removed[I(0, y)]     = true; q.Enqueue((0, y)); }
            if (w > 1 && KeyMatch(w - 1, y)) { removed[I(w - 1, y)] = true; q.Enqueue((w - 1, y)); }
        }

        void TryAdd(int nx, int ny)
        {
            if (nx < 0 || ny < 0 || nx >= w || ny >= h) return;
            var i = I(nx, ny);
            if (removed[i] || !KeyMatch(nx, ny)) return;
            removed[i] = true;
            q.Enqueue((nx, ny));
        }
        while (q.Count > 0)
        {
            var (x, y) = q.Dequeue();
            TryAdd(x - 1, y); TryAdd(x + 1, y); TryAdd(x, y - 1); TryAdd(x, y + 1);
        }

        image.ProcessPixelRows(accessor =>
        {
            for (var y = 0; y < accessor.Height; y++)
            {
                var row = accessor.GetRowSpan(y);
                for (var x = 0; x < row.Length; x++)
                {
                    if (!removed[y * w + x]) continue;
                    var p = row[x];
                    row[x] = new Rgba32(p.R, p.G, p.B, 0);
                }
            }
        });
    }
}

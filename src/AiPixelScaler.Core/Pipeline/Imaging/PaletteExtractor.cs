using AiPixelScaler.Core.Color;
using SixLabors.ImageSharp;
using SixLabors.ImageSharp.PixelFormats;

namespace AiPixelScaler.Core.Pipeline.Imaging;

/// <summary>
/// Estrazione palette ottima via K-Means in spazio percettivo Oklab.
///
/// Algoritmo:
///   1. Sample fino a N pixel opachi rappresentativi (riservoir + grid sampling).
///   2. Inizializzazione centri con K-Means++ (probabilità ∝ ΔE_OK² dal centro più vicino).
///   3. Iterazione di Lloyd (assegna pixel → centro più vicino, sposta centro = media cluster).
///   4. Convergenza quando max-shift &lt; ε o raggiunte <c>maxIterations</c>.
///
/// Output: lista di <c>Rgba32</c> opachi ordinati per luminosità decrescente.
///
/// Lavorare in Oklab — non in RGB — produce palette percettivamente bilanciate
/// (es. distingue meglio sfumature di pelle/cielo/vegetazione).
/// </summary>
public static class PaletteExtractor
{
    public record Options(int Colors = 16, int MaxSamples = 8000, int MaxIterations = 30);

    public static IReadOnlyList<Rgba32> Extract(Image<Rgba32> image, Options options)
    {
        var k = Math.Clamp(options.Colors, 2, 256);
        var samples = SamplePixelsOklab(image, options.MaxSamples);
        if (samples.Count == 0) return [];

        var centers = KMeansPlusPlusInit(samples, k);
        RunLloyd(samples, centers, options.MaxIterations, epsilon: 1e-4f);

        return centers
            .Select(c => c.ToSrgb(255))
            .OrderByDescending(c => 0.299 * c.R + 0.587 * c.G + 0.114 * c.B)  // luma BT.601
            .ToList();
    }

    // ─────────────────────────────────────────────────────────────────────────

    private static List<Oklab> SamplePixelsOklab(Image<Rgba32> image, int maxSamples)
    {
        var w = image.Width;
        var h = image.Height;
        var total = w * h;
        var step = Math.Max(1, total / maxSamples);
        var list = new List<Oklab>(Math.Min(maxSamples, total));
        var idx = 0;

        image.ProcessPixelRows(accessor =>
        {
            for (var y = 0; y < accessor.Height; y++)
            {
                var row = accessor.GetRowSpan(y);
                for (var x = 0; x < row.Length; x++)
                {
                    if (idx++ % step != 0) continue;
                    var p = row[x];
                    if (p.A < 200) continue;            // ignora trasparenti/edge
                    list.Add(Oklab.FromSrgb(p));
                    if (list.Count >= maxSamples) return;
                }
            }
        });

        return list;
    }

    private static List<Oklab> KMeansPlusPlusInit(IReadOnlyList<Oklab> samples, int k)
    {
        var rng = new Random(42);  // deterministico
        var centers = new List<Oklab>(k) { samples[rng.Next(samples.Count)] };

        var distSq = new float[samples.Count];
        for (var i = 0; i < samples.Count; i++)
            distSq[i] = Oklab.DistanceSquared(samples[i], centers[0]);

        while (centers.Count < k)
        {
            var sum = 0.0;
            for (var i = 0; i < distSq.Length; i++) sum += distSq[i];
            if (sum <= 0) break;

            var threshold = rng.NextDouble() * sum;
            var acc = 0.0;
            var pick = samples.Count - 1;
            for (var i = 0; i < distSq.Length; i++)
            {
                acc += distSq[i];
                if (acc >= threshold) { pick = i; break; }
            }
            var picked = samples[pick];
            centers.Add(picked);

            // aggiorna distanze (manteniamo la minima al cluster esistente più vicino)
            for (var i = 0; i < samples.Count; i++)
            {
                var d = Oklab.DistanceSquared(samples[i], picked);
                if (d < distSq[i]) distSq[i] = d;
            }
        }
        return centers;
    }

    private static void RunLloyd(
        IReadOnlyList<Oklab> samples, List<Oklab> centers,
        int maxIterations, float epsilon)
    {
        var assignments = new int[samples.Count];
        var sumL = new double[centers.Count];
        var sumA = new double[centers.Count];
        var sumB = new double[centers.Count];
        var counts = new int[centers.Count];

        for (var iter = 0; iter < maxIterations; iter++)
        {
            // 1) assegnazione
            for (var i = 0; i < samples.Count; i++)
            {
                var best = 0;
                var bestD = float.MaxValue;
                for (var c = 0; c < centers.Count; c++)
                {
                    var d = Oklab.DistanceSquared(samples[i], centers[c]);
                    if (d < bestD) { bestD = d; best = c; }
                }
                assignments[i] = best;
            }

            // 2) update
            Array.Clear(sumL); Array.Clear(sumA); Array.Clear(sumB); Array.Clear(counts);
            for (var i = 0; i < samples.Count; i++)
            {
                var c = assignments[i];
                sumL[c] += samples[i].L; sumA[c] += samples[i].A; sumB[c] += samples[i].B;
                counts[c]++;
            }

            var maxShift = 0f;
            for (var c = 0; c < centers.Count; c++)
            {
                if (counts[c] == 0) continue;
                var nu = new Oklab(
                    (float)(sumL[c] / counts[c]),
                    (float)(sumA[c] / counts[c]),
                    (float)(sumB[c] / counts[c]));
                var shift = Oklab.DistanceSquared(centers[c], nu);
                if (shift > maxShift) maxShift = shift;
                centers[c] = nu;
            }
            if (maxShift < epsilon * epsilon) break;
        }
    }
}

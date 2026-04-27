using AiPixelScaler.Core.Color;
using SixLabors.ImageSharp;
using SixLabors.ImageSharp.PixelFormats;

namespace AiPixelScaler.Core.Pipeline.Imaging;

/// <summary>
/// Chroma key — rimuove (alpha=0) i pixel con colore "vicino" al colore chiave.
///
/// Due metriche disponibili:
///
/// • <see cref="Metric.EuclideanRgb"/> — distanza Euclidea su RGB sRGB grezzo.
///   Veloce. Tolleranza tipica: 20–60.
///
/// • <see cref="Metric.OklabPerceptual"/> — ΔE_OK in spazio Oklab.
///   Percettivamente uniforme. Tolleranza tipica: 0.05–0.15.
///   Distingue sfumature simili percettivamente che la metrica RGB confonde
///   (es. ombre del key sul foreground o gradient sottili).
/// </summary>
public static class ChromaKey
{
    public enum Metric { EuclideanRgb, OklabPerceptual }

    public static void ApplyInPlace(Image<Rgba32> image, Rgba32 key, double tolerance = 0,
                                     Metric metric = Metric.EuclideanRgb)
    {
        if (tolerance < 0) tolerance = 0;

        if (metric == Metric.EuclideanRgb)
        {
            var tolSq = tolerance * tolerance;
            image.ProcessPixelRows(accessor =>
            {
                for (var y = 0; y < accessor.Height; y++)
                {
                    var row = accessor.GetRowSpan(y);
                    for (var x = 0; x < row.Length; x++)
                    {
                        var p = row[x];
                        if (p.A == 0) continue;
                        var dr = p.R - key.R;
                        var dg = p.G - key.G;
                        var db = p.B - key.B;
                        if (dr * dr + dg * dg + db * db <= tolSq)
                            row[x] = new Rgba32(p.R, p.G, p.B, 0);
                    }
                }
            });
            return;
        }

        var keyOk = Oklab.FromSrgb(key);
        var tolSqF = (float)(tolerance * tolerance);
        image.ProcessPixelRows(accessor =>
        {
            for (var y = 0; y < accessor.Height; y++)
            {
                var row = accessor.GetRowSpan(y);
                for (var x = 0; x < row.Length; x++)
                {
                    var p = row[x];
                    if (p.A == 0) continue;
                    if (Oklab.DistanceSquared(keyOk, Oklab.FromSrgb(p)) <= tolSqF)
                        row[x] = new Rgba32(p.R, p.G, p.B, 0);
                }
            }
        });
    }

    /// <summary>
    /// Porta i pixel vicini al colore chiave al valore RGB esatto del key (mantiene l'alfa originale).
    /// Utile prima di altre operazioni che richiedono uno sfondo uniforme senza introdurre trasparenza.
    /// </summary>
    public static void SnapKeyRgbInPlace(Image<Rgba32> image, Rgba32 key, double tolerance = 0,
                                          Metric metric = Metric.EuclideanRgb)
    {
        if (tolerance < 0) tolerance = 0;

        if (metric == Metric.EuclideanRgb)
        {
            var tolSq = tolerance * tolerance;
            image.ProcessPixelRows(accessor =>
            {
                for (var y = 0; y < accessor.Height; y++)
                {
                    var row = accessor.GetRowSpan(y);
                    for (var x = 0; x < row.Length; x++)
                    {
                        var p = row[x];
                        if (p.A == 0) continue;
                        var dr = p.R - key.R;
                        var dg = p.G - key.G;
                        var db = p.B - key.B;
                        if (dr * dr + dg * dg + db * db <= tolSq)
                            row[x] = new Rgba32(key.R, key.G, key.B, p.A);
                    }
                }
            });
            return;
        }

        var keyOk = Oklab.FromSrgb(key);
        var tolSqF = (float)(tolerance * tolerance);
        image.ProcessPixelRows(accessor =>
        {
            for (var y = 0; y < accessor.Height; y++)
            {
                var row = accessor.GetRowSpan(y);
                for (var x = 0; x < row.Length; x++)
                {
                    var p = row[x];
                    if (p.A == 0) continue;
                    if (Oklab.DistanceSquared(keyOk, Oklab.FromSrgb(p)) <= tolSqF)
                        row[x] = new Rgba32(key.R, key.G, key.B, p.A);
                }
            }
        });
    }
}

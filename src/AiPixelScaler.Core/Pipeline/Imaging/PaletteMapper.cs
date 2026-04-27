using AiPixelScaler.Core.Color;
using SixLabors.ImageSharp;
using SixLabors.ImageSharp.PixelFormats;

namespace AiPixelScaler.Core.Pipeline.Imaging;

/// <summary>
/// Mappa ogni pixel dell'immagine al colore della palette percettivamente più vicino (Oklab),
/// con dithering opzionale Floyd-Steinberg per nascondere il banding.
///
/// Floyd-Steinberg distribuisce l'errore di quantizzazione (in spazio Oklab):
///         X    7/16
///   3/16  5/16  1/16
/// Lavorare in Oklab garantisce che le sostituzioni siano percettivamente
/// minime — niente "shift" verso colori vicini-in-RGB-ma-lontani-percettivamente.
/// </summary>
public static class PaletteMapper
{
    public enum DitherMode { None, FloydSteinberg }

    public static void ApplyInPlace(Image<Rgba32> image, IReadOnlyList<Rgba32> palette, DitherMode dither = DitherMode.None)
    {
        if (palette.Count == 0) return;
        var paletteOk = palette.Select(c => Oklab.FromSrgb(c)).ToArray();

        if (dither == DitherMode.None)
        {
            ApplyNoDither(image, palette, paletteOk);
            return;
        }

        ApplyFloydSteinberg(image, palette, paletteOk);
    }

    private static void ApplyNoDither(Image<Rgba32> image, IReadOnlyList<Rgba32> palette, Oklab[] paletteOk)
    {
        image.ProcessPixelRows(accessor =>
        {
            for (var y = 0; y < accessor.Height; y++)
            {
                var row = accessor.GetRowSpan(y);
                for (var x = 0; x < row.Length; x++)
                {
                    var p = row[x];
                    if (p.A == 0) continue;
                    var k = NearestIndex(Oklab.FromSrgb(p), paletteOk);
                    var pal = palette[k];
                    row[x] = new Rgba32(pal.R, pal.G, pal.B, p.A);
                }
            }
        });
    }

    private static void ApplyFloydSteinberg(Image<Rgba32> image, IReadOnlyList<Rgba32> palette, Oklab[] paletteOk)
    {
        var w = image.Width;
        var h = image.Height;

        // Buffer Oklab dei pixel "lavorati" — l'errore propagato vive qui (fp32 evita clipping intermedio)
        var buf = new Oklab[w * h];
        var alpha = new byte[w * h];
        image.ProcessPixelRows(a =>
        {
            for (var y = 0; y < a.Height; y++)
            {
                var row = a.GetRowSpan(y);
                for (var x = 0; x < row.Length; x++)
                {
                    var p = row[x];
                    alpha[y * w + x] = p.A;
                    buf[y * w + x] = p.A > 0 ? Oklab.FromSrgb(p) : default;
                }
            }
        });

        for (var y = 0; y < h; y++)
        for (var x = 0; x < w; x++)
        {
            if (alpha[y * w + x] == 0) continue;
            var old = buf[y * w + x];
            var k   = NearestIndex(old, paletteOk);
            var nu  = paletteOk[k];
            buf[y * w + x] = nu;

            var eL = old.L - nu.L;
            var eA = old.A - nu.A;
            var eB = old.B - nu.B;

            Diffuse(buf, w, h, x + 1, y,     7f / 16f, eL, eA, eB);
            Diffuse(buf, w, h, x - 1, y + 1, 3f / 16f, eL, eA, eB);
            Diffuse(buf, w, h, x,     y + 1, 5f / 16f, eL, eA, eB);
            Diffuse(buf, w, h, x + 1, y + 1, 1f / 16f, eL, eA, eB);
        }

        // Scrittura finale
        image.ProcessPixelRows(accessor =>
        {
            for (var y = 0; y < accessor.Height; y++)
            {
                var row = accessor.GetRowSpan(y);
                for (var x = 0; x < row.Length; x++)
                {
                    var aval = alpha[y * w + x];
                    if (aval == 0) { row[x] = default; continue; }
                    var k = NearestIndex(buf[y * w + x], paletteOk);
                    var pal = palette[k];
                    row[x] = new Rgba32(pal.R, pal.G, pal.B, aval);
                }
            }
        });
    }

    private static void Diffuse(Oklab[] buf, int w, int h, int x, int y, float weight, float eL, float eA, float eB)
    {
        if (x < 0 || y < 0 || x >= w || y >= h) return;
        var b = buf[y * w + x];
        buf[y * w + x] = new Oklab(b.L + eL * weight, b.A + eA * weight, b.B + eB * weight);
    }

    private static int NearestIndex(in Oklab c, Oklab[] palette)
    {
        var best = 0;
        var bestD = float.MaxValue;
        for (var i = 0; i < palette.Length; i++)
        {
            var d = Oklab.DistanceSquared(c, palette[i]);
            if (d < bestD) { bestD = d; best = i; }
        }
        return best;
    }
}

using SixLabors.ImageSharp;
using SixLabors.ImageSharp.PixelFormats;

namespace AiPixelScaler.Core.Pipeline.Imaging;

/// <summary>
/// Filtro mediano 3×3 per canale (RGBA indipendenti).
///
/// La mediana è una statistica d'ordine: per ciascun canale prende il 5° valore
/// (su 9) della finestra ordinata. Rispetto al box-blur preserva i bordi netti
/// (non smussa transizioni di contrasto) ed elimina perfettamente il rumore "salt &amp; pepper".
///
/// Per gli sprite generati da IA è il filtro denoise di prima scelta:
/// rimuove i singoli pixel "fuori posto" senza ammorbidire le silhouette.
/// </summary>
public static class MedianFilter
{
    public static void ApplyInPlace(Image<Rgba32> image)
    {
        var w = image.Width;
        var h = image.Height;
        if (w < 3 || h < 3) return;

        var src = ImageUtils.ToFlatArray(image);

        image.ProcessPixelRows(accessor =>
        {
            // Buffer riutilizzati allocati UNA volta (CA2014: niente stackalloc-in-loop)
            Span<byte> rs = stackalloc byte[9];
            Span<byte> gs = stackalloc byte[9];
            Span<byte> bs = stackalloc byte[9];
            Span<byte> @as = stackalloc byte[9];

            for (var y = 0; y < accessor.Height; y++)
            {
                var row = accessor.GetRowSpan(y);
                for (var x = 0; x < row.Length; x++)
                {
                    var k = 0;
                    for (var dy = -1; dy <= 1; dy++)
                    for (var dx = -1; dx <= 1; dx++)
                    {
                        var nx = Math.Clamp(x + dx, 0, w - 1);
                        var ny = Math.Clamp(y + dy, 0, h - 1);
                        var p = src[ny * w + nx];
                        rs[k] = p.R; gs[k] = p.G; bs[k] = p.B; @as[k] = p.A;
                        k++;
                    }
                    row[x] = new Rgba32(Median9(rs), Median9(gs), Median9(bs), Median9(@as));
                }
            }
        });
    }

    /// <summary>Mediana di 9 byte tramite insertion-sort (9 elementi → cache-friendly).</summary>
    private static byte Median9(Span<byte> a)
    {
        for (var i = 1; i < 9; i++)
        {
            var key = a[i];
            var j = i - 1;
            while (j >= 0 && a[j] > key) { a[j + 1] = a[j]; j--; }
            a[j + 1] = key;
        }
        return a[4];
    }
}

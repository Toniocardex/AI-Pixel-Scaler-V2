using SixLabors.ImageSharp;
using SixLabors.ImageSharp.PixelFormats;

namespace AiPixelScaler.Core.Pipeline.Imaging;

/// <summary>
/// Color Decontamination — rimuove l'alone colorato dello sfondo nei pixel semi-trasparenti.
///
/// Modello premultiplied:  C_obs = α·C_fg + (1−α)·C_bg
/// Conoscendo il colore di sfondo C_bg si recupera il colore puro:
///   C_fg = (C_obs − (1−α)·C_bg) / α
///
/// È IL fix decisivo per gli sprite generati da IA: gli edge restano "morbidi"
/// ma senza l'alone verde/blu del background originale.
///
/// Modalità:
///  • <c>Background</c> noto (es. green-screen) → ricostruzione esatta
///  • <c>FromOpaqueNeighbors</c> → C_fg stimato dai pixel opachi vicini (8-conn)
///    → utile quando lo sfondo non è uniforme o sconosciuto.
///
/// In entrambi i casi i pixel completamente trasparenti o completamente opachi sono ignorati.
/// </summary>
public static class Defringe
{
    /// <summary>De-fringe contro uno sfondo noto.</summary>
    public static void RemoveBackgroundBleed(Image<Rgba32> image, Rgba32 background)
    {
        var bgR = background.R;
        var bgG = background.G;
        var bgB = background.B;

        image.ProcessPixelRows(accessor =>
        {
            for (var y = 0; y < accessor.Height; y++)
            {
                var row = accessor.GetRowSpan(y);
                for (var x = 0; x < row.Length; x++)
                {
                    var p = row[x];
                    if (p.A == 0 || p.A == 255) continue;

                    var a    = p.A / 255f;
                    var inv  = 1f - a;
                    // C_fg = (C_obs − inv·C_bg) / a
                    var fr = (p.R - inv * bgR) / a;
                    var fg = (p.G - inv * bgG) / a;
                    var fb = (p.B - inv * bgB) / a;

                    row[x] = new Rgba32(
                        (byte)Math.Clamp((int)MathF.Round(fr), 0, 255),
                        (byte)Math.Clamp((int)MathF.Round(fg), 0, 255),
                        (byte)Math.Clamp((int)MathF.Round(fb), 0, 255),
                        p.A);
                }
            }
        });
    }

    /// <summary>
    /// De-fringe stimando il colore puro dai vicini opachi (8-connessi).
    /// Per ogni pixel semi-trasparente: media RGB dei vicini con A &gt; <paramref name="opaqueThreshold"/>;
    /// se non ci sono vicini opachi, il pixel viene lasciato invariato.
    /// </summary>
    public static void FromOpaqueNeighbors(Image<Rgba32> image, byte opaqueThreshold = 250)
    {
        var w = image.Width;
        var h = image.Height;
        if (w < 2 || h < 2) return;

        // Snapshot per evitare letture inquinate dalle scritture
        var src = ImageUtils.ToFlatArray(image);

        image.ProcessPixelRows(accessor =>
        {
            for (var y = 0; y < accessor.Height; y++)
            {
                var row = accessor.GetRowSpan(y);
                for (var x = 0; x < row.Length; x++)
                {
                    var p = row[x];
                    if (p.A == 0 || p.A >= opaqueThreshold) continue;

                    int sumR = 0, sumG = 0, sumB = 0, n = 0;
                    for (var dy = -1; dy <= 1; dy++)
                    for (var dx = -1; dx <= 1; dx++)
                    {
                        if (dx == 0 && dy == 0) continue;
                        var nx = x + dx;
                        var ny = y + dy;
                        if (nx < 0 || ny < 0 || nx >= w || ny >= h) continue;
                        var np = src[ny * w + nx];
                        if (np.A < opaqueThreshold) continue;
                        sumR += np.R; sumG += np.G; sumB += np.B; n++;
                    }
                    if (n == 0) continue;

                    row[x] = new Rgba32((byte)(sumR / n), (byte)(sumG / n), (byte)(sumB / n), p.A);
                }
            }
        });
    }
}

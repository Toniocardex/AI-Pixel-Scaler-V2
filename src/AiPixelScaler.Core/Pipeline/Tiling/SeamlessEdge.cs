using AiPixelScaler.Core.Pipeline.Imaging;
using SixLabors.ImageSharp;
using SixLabors.ImageSharp.PixelFormats;

namespace AiPixelScaler.Core.Pipeline.Tiling;

/// <summary>
/// Rende un'immagine tileable (ripetibile senza giunture visibili).
///
/// Algoritmo (shift + Bayer dither):
///   1. <b>Wrap shift</b> di (W/2, H/2): l'immagine viene traslata circolarmente
///      così che gli angoli originali si congiungano al centro. I bordi destri/sinistri
///      e top/bottom risultano automaticamente identici (tile garantito).
///   2. <b>Heal del seam centrale</b>: la discontinuità si trova ora nelle
///      colonne/righe centrali. Si maschera con un <b>dithering Bayer 4×4 deterministico</b>
///      che alterna pixel originali e pixel speculari secondo una matrice di soglia.
///
/// Critico per pixel art: <b>NIENTE</b> alpha-blending lineare o gradient — solo scelte
/// binarie pixel-per-pixel, così lo stile pixel-art viene preservato.
///
/// Bayer 4×4 (matrice di soglia normalizzata 0..15):
///   0  8  2 10
///  12  4 14  6
///   3 11  1  9
///  15  7 13  5
///
/// Per pixel a distanza <c>d</c> dal seam (banda di larghezza <c>blendWidth</c>):
///   ratio   = 1 − d/blendWidth      (1 al seam, 0 al bordo banda)
///   thresh  = Bayer(x mod 4, y mod 4) / 15
///   if ratio &gt; thresh → sostituisci col pixel speculare oltre il seam
/// </summary>
public static class SeamlessEdge
{
    private static readonly int[,] Bayer4 =
    {
        {  0,  8,  2, 10 },
        { 12,  4, 14,  6 },
        {  3, 11,  1,  9 },
        { 15,  7, 13,  5 },
    };

    /// <summary>
    /// Genera una versione tileable. <paramref name="blendWidth"/> = larghezza in px della
    /// banda di dithering attorno al seam centrale (tipico 4–12 px).
    /// </summary>
    public static Image<Rgba32> MakeTileable(Image<Rgba32> source, int blendWidth = 4)
    {
        if (source.Width < 4 || source.Height < 4) return source.Clone();
        blendWidth = Math.Max(1, blendWidth);

        var w = source.Width;
        var h = source.Height;
        var halfW = w / 2;
        var halfH = h / 2;

        // Snapshot pixel da source (lettura lineare in array)
        var src = ImageUtils.ToFlatArray(source);

        // Wrap shift: dst[x,y] = src[(x+halfW)%w, (y+halfH)%h]
        var dst = new Rgba32[w * h];
        for (var y = 0; y < h; y++)
        for (var x = 0; x < w; x++)
            dst[y * w + x] = src[((y + halfH) % h) * w + ((x + halfW) % w)];

        // Heal seam verticale a x = halfW (banda halfW±blendWidth)
        for (var y = 0; y < h; y++)
        for (var dx = -blendWidth; dx < blendWidth; dx++)
        {
            var x = halfW + dx;
            if (x < 0 || x >= w) continue;
            var thresh = Bayer4[y & 3, x & 3] / 15.0;
            var ratio  = 1.0 - Math.Abs(dx) / (double)blendWidth;
            if (ratio > thresh)
            {
                // mirror across vertical seam at x = halfW
                var mx = halfW - dx - 1;
                if (mx >= 0 && mx < w) dst[y * w + x] = dst[y * w + mx];
            }
        }

        // Heal seam orizzontale a y = halfH (banda halfH±blendWidth)
        for (var x = 0; x < w; x++)
        for (var dy = -blendWidth; dy < blendWidth; dy++)
        {
            var y = halfH + dy;
            if (y < 0 || y >= h) continue;
            var thresh = Bayer4[y & 3, x & 3] / 15.0;
            var ratio  = 1.0 - Math.Abs(dy) / (double)blendWidth;
            if (ratio > thresh)
            {
                var my = halfH - dy - 1;
                if (my >= 0 && my < h) dst[y * w + x] = dst[my * w + x];
            }
        }

        // Scrittura su Image<Rgba32>
        var result = new Image<Rgba32>(w, h);
        result.ProcessPixelRows(a =>
        {
            for (var y = 0; y < a.Height; y++)
            {
                var row = a.GetRowSpan(y);
                for (var x = 0; x < row.Length; x++) row[x] = dst[y * w + x];
            }
        });
        return result;
    }
}

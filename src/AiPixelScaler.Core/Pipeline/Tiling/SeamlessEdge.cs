using AiPixelScaler.Core.Pipeline.Imaging;
using SixLabors.ImageSharp;
using SixLabors.ImageSharp.PixelFormats;

namespace AiPixelScaler.Core.Pipeline.Tiling;

/// <summary>
/// Rende un'immagine tileable (ripetibile senza giunture ai bordi esterni).
///
/// Algoritmo <b>Shift + dither Bayer</b>:
/// <list type="number">
/// <item><b>Base shiftata</b>: wrap-traslazione di (W/2, H/2) — i bordi esterni dell’originale
/// coincidono nel tiling; la discontinuità si sposta in una croce al centro.</item>
/// <item><b>Patch</b>: pixel dell’immagine <em>non</em> shiftata, che al centro è ancora coerente.</item>
/// <item><b>Dither</b>: vicino al centro si sceglie patch vs base con soglia Bayer 4×4 e peso
/// da distanza dal seam — solo pixel dell’originale, niente blend alpha (pixel art pulita).</item>
/// </list>
/// </summary>
public static class SeamlessEdge
{
    /// <summary>Matrice Bayer 4×4 normalizzata in [0, 1).</summary>
    private static readonly float[,] Bayer4x4 =
    {
        {  0f / 16f,  8f / 16f,  2f / 16f, 10f / 16f },
        { 12f / 16f,  4f / 16f, 14f / 16f,  6f / 16f },
        {  3f / 16f, 11f / 16f,  1f / 16f,  9f / 16f },
        { 15f / 16f,  7f / 16f, 13f / 16f,  5f / 16f },
    };

    /// <summary>
    /// Genera una versione seamless usando shift + patch + dither Bayer.
    /// </summary>
    /// <param name="original">Immagine sorgente.</param>
    /// <param name="blendRadius">Raggio in pixel della zona di transizione attorno al seam centrale (tipico 4–8).</param>
    /// <returns>Nuova immagine; la sorgente non viene modificata.</returns>
    public static Image<Rgba32> CreateSeamlessTile(Image<Rgba32> original, int blendRadius = 6)
    {
        ArgumentNullException.ThrowIfNull(original);

        var width = original.Width;
        var height = original.Height;
        if (width < 4 || height < 4)
            return original.Clone();

        blendRadius = Math.Max(1, blendRadius);

        var shiftX = width / 2;
        var shiftY = height / 2;

        var src = ImageUtils.ToFlatArray(original);
        var dst = new Rgba32[width * height];

        for (var y = 0; y < height; y++)
        {
            var shiftedY = (y + shiftY) % height;
            var rowShiftedBase = shiftedY * width;
            var rowPatchBase = y * width;

            for (var x = 0; x < width; x++)
            {
                var shiftedX = (x + shiftX) % width;

                var basePixel = src[rowShiftedBase + shiftedX];
                var patchPixel = src[rowPatchBase + x];

                var distX = Math.Abs(x - shiftX);
                var distY = Math.Abs(y - shiftY);

                var weightX = Math.Max(0f, 1f - distX / (float)blendRadius);
                var weightY = Math.Max(0f, 1f - distY / (float)blendRadius);
                var blendWeight = Math.Max(weightX, weightY);

                var bayerThreshold = Bayer4x4[y & 3, x & 3];

                dst[y * width + x] = blendWeight > bayerThreshold ? patchPixel : basePixel;
            }
        }

        var result = new Image<Rgba32>(width, height);
        result.ProcessPixelRows(accessor =>
        {
            for (var y = 0; y < height; y++)
                dst.AsSpan(y * width, width).CopyTo(accessor.GetRowSpan(y));
        });

        return result;
    }

    /// <summary>
    /// Alias con naming storico del progetto. <paramref name="blendWidth"/> equivale a <see cref="CreateSeamlessTile"/> blend radius.
    /// </summary>
    public static Image<Rgba32> MakeTileable(Image<Rgba32> source, int blendWidth = 6) =>
        CreateSeamlessTile(source, blendWidth);
}

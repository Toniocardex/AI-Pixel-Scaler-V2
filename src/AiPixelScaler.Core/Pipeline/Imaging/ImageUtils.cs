using SixLabors.ImageSharp;
using SixLabors.ImageSharp.PixelFormats;

namespace AiPixelScaler.Core.Pipeline.Imaging;

/// <summary>
/// Helper di basso livello condivisi tra i filtri del pipeline immagine.
/// </summary>
public static class ImageUtils
{
    /// <summary>
    /// Copia tutti i pixel in un array flat row-major
    /// (<c>pixels[y * width + x]</c>). Usato come snapshot read-only
    /// dagli algoritmi che leggono e scrivono simultaneamente.
    /// </summary>
    public static Rgba32[] ToFlatArray(Image<Rgba32> image)
    {
        var w = image.Width;
        var pixels = new Rgba32[w * image.Height];
        image.ProcessPixelRows(a =>
        {
            for (var y = 0; y < a.Height; y++)
            {
                var row = a.GetRowSpan(y);
                for (var x = 0; x < row.Length; x++) pixels[y * w + x] = row[x];
            }
        });
        return pixels;
    }

    /// <summary>
    /// Conta i pixel con <c>0 &lt; α &lt; opaqueThreshold</c>.
    /// Utile come pre-flight check per il defringe.
    /// </summary>
    public static int CountSemiTransparent(Image<Rgba32> image, byte opaqueThreshold)
    {
        var count = 0;
        image.ProcessPixelRows(a =>
        {
            for (var y = 0; y < a.Height; y++)
            {
                var row = a.GetRowSpan(y);
                for (var x = 0; x < row.Length; x++)
                {
                    var alpha = row[x].A;
                    if (alpha > 0 && alpha < opaqueThreshold) count++;
                }
            }
        });
        return count;
    }

    /// <summary>
    /// Azzera (alpha=0) un rettangolo in <paramref name="image"/> in-place.
    /// Le coordinate vengono clampate ai bordi dell'immagine.
    /// </summary>
    public static void ClearRect(Image<Rgba32> image, int x, int y, int w, int h)
    {
        var x2 = Math.Min(image.Width,  x + w);
        var y2 = Math.Min(image.Height, y + h);
        var x1 = Math.Max(0, x);
        var y1 = Math.Max(0, y);
        var clear = new Rgba32(0, 0, 0, 0);
        image.ProcessPixelRows(accessor =>
        {
            for (var py = y1; py < y2; py++)
            {
                var row = accessor.GetRowSpan(py);
                for (var px = x1; px < x2; px++) row[px] = clear;
            }
        });
    }
}

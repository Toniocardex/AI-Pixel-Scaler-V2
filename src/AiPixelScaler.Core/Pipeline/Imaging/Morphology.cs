using SixLabors.ImageSharp;
using SixLabors.ImageSharp.PixelFormats;

namespace AiPixelScaler.Core.Pipeline.Imaging;

/// <summary>
/// Operazioni morfologiche sul canale alpha dell'immagine.
///
/// Tutte le operazioni lavorano sulla maschera binaria opaco/trasparente:
///   Opaco  (A > 0)   = sprite content
///   Trasparente (A == 0) = sfondo rimosso
///
/// Pipeline tipica (dopo rimozione sfondo):
///   Erode  → rimuove frangia residua 1px ai bordi del sprite
///   Dilate → edge padding: estende i colori del bordo nello spazio trasparente
///             (previene artefatti nel bilinear filtering di Unity/Godot)
///   Open   = Erode + Dilate → rimuove protrusioni sottili e pixel isolati
///   Close  = Dilate + Erode → chiude buchi sottili all'interno dello sprite
///
/// I pixel rimossi vengono impostati a RGBA(0,0,0,0) — no alpha bleeding.
/// I pixel espansi (Dilate) ricevono il colore più frequente tra i vicini opachi.
/// </summary>
public static class Morphology
{
    /// <summary>
    /// Erode: ogni pixel opaco che tocca almeno un vicino trasparente (8-conn.) viene rimosso.
    /// Riduce il bordo opaco di 1px per iterazione.
    /// </summary>
    /// <returns>Numero di pixel rimossi.</returns>
    public static int Erode(Image<Rgba32> image, int iterations = 1)
    {
        var total = 0;
        for (var i = 0; i < Math.Max(1, iterations); i++)
            total += ErodeOnce(image);
        return total;
    }

    /// <summary>
    /// Dilate: ogni pixel trasparente che tocca almeno un vicino opaco (8-conn.) viene
    /// espanso, ricevendo il colore più frequente tra i vicini opachi.
    /// Espande il bordo opaco di 1px per iterazione (edge padding).
    /// </summary>
    /// <returns>Numero di pixel aggiunti.</returns>
    public static int Dilate(Image<Rgba32> image, int iterations = 1)
    {
        var total = 0;
        for (var i = 0; i < Math.Max(1, iterations); i++)
            total += DilateOnce(image);
        return total;
    }

    /// <summary>
    /// Open = Erode poi Dilate: rimuove protrusioni sottili e pixel opachi isolati
    /// senza ridurre la dimensione complessiva dello sprite.
    /// </summary>
    /// <returns>Coppia (pixelRimossiErode, pixelAggiuntiDilate).</returns>
    public static (int Eroded, int Dilated) Open(Image<Rgba32> image, int iterations = 1)
    {
        var eroded  = Erode(image, iterations);
        var dilated = Dilate(image, iterations);
        return (eroded, dilated);
    }

    /// <summary>
    /// Close = Dilate poi Erode: chiude buchi interni sottili senza espandere
    /// la forma esterna dello sprite.
    /// </summary>
    /// <returns>Coppia (pixelAggiuntiDilate, pixelRimossiErode).</returns>
    public static (int Dilated, int Eroded) Close(Image<Rgba32> image, int iterations = 1)
    {
        var dilated = Dilate(image, iterations);
        var eroded  = Erode(image, iterations);
        return (dilated, eroded);
    }

    // ─────────────────────────────────────────────────────────────────────────

    private static int ErodeOnce(Image<Rgba32> image)
    {
        var w       = image.Width;
        var h       = image.Height;
        var pixels  = ImageUtils.ToFlatArray(image);
        var toErase = new bool[w * h];

        for (var y = 0; y < h; y++)
        for (var x = 0; x < w; x++)
        {
            var idx = y * w + x;
            if (pixels[idx].A == 0) continue; // già trasparente

            // Se almeno un vicino (8-conn.) è trasparente → candidato all'erosione
            if (HasTransparentNeighbor(pixels, w, h, x, y))
                toErase[idx] = true;
        }

        var count = 0;
        image.ProcessPixelRows(accessor =>
        {
            for (var y = 0; y < accessor.Height; y++)
            {
                var row = accessor.GetRowSpan(y);
                for (var x = 0; x < row.Length; x++)
                {
                    var idx = y * w + x;
                    if (!toErase[idx]) continue;
                    row[x] = new Rgba32(0, 0, 0, 0);
                    count++;
                }
            }
        });
        return count;
    }

    private static int DilateOnce(Image<Rgba32> image)
    {
        var w        = image.Width;
        var h        = image.Height;
        var pixels   = ImageUtils.ToFlatArray(image);
        var newColor = new Rgba32[w * h]; // (0,0,0,0) = no expansion

        for (var y = 0; y < h; y++)
        for (var x = 0; x < w; x++)
        {
            var idx = y * w + x;
            if (pixels[idx].A > 0) continue; // già opaco

            // Cerca il colore più frequente tra i vicini opachi
            var modeColor = OpaqueNeighborMode(pixels, w, h, x, y);
            if (modeColor.HasValue)
                newColor[idx] = modeColor.Value;
        }

        var count = 0;
        image.ProcessPixelRows(accessor =>
        {
            for (var y = 0; y < accessor.Height; y++)
            {
                var row = accessor.GetRowSpan(y);
                for (var x = 0; x < row.Length; x++)
                {
                    var c = newColor[y * w + x];
                    if (c.A == 0) continue;
                    row[x] = c;
                    count++;
                }
            }
        });
        return count;
    }

    // ─────────────────────────────────────────────────────────────────────────

    private static bool HasTransparentNeighbor(Rgba32[] pixels, int w, int h, int x, int y)
    {
        for (var dy = -1; dy <= 1; dy++)
        for (var dx = -1; dx <= 1; dx++)
        {
            if (dx == 0 && dy == 0) continue;
            var nx = x + dx;
            var ny = y + dy;
            if (nx < 0 || nx >= w || ny < 0 || ny >= h) continue;
            if (pixels[ny * w + nx].A == 0) return true;
        }
        return false;
    }

    /// <summary>
    /// Ritorna il colore più frequente tra i vicini opachi, o null se nessun vicino è opaco.
    /// Usa un dizionario packed-RGB come chiave per la frequenza.
    /// </summary>
    private static Rgba32? OpaqueNeighborMode(Rgba32[] pixels, int w, int h, int x, int y)
    {
        // Max 8 vicini: array stack-friendly
        Span<uint> keys   = stackalloc uint[8];
        Span<int>  counts = stackalloc int[8];
        var n = 0;

        for (var dy = -1; dy <= 1; dy++)
        for (var dx = -1; dx <= 1; dx++)
        {
            if (dx == 0 && dy == 0) continue;
            var nx = x + dx;
            var ny = y + dy;
            if (nx < 0 || nx >= w || ny < 0 || ny >= h) continue;
            var p = pixels[ny * w + nx];
            if (p.A == 0) continue;

            var packed = (uint)(p.R << 16 | p.G << 8 | p.B);
            var found  = false;
            for (var i = 0; i < n; i++)
            {
                if (keys[i] != packed) continue;
                counts[i]++;
                found = true;
                break;
            }
            if (!found && n < 8)
            {
                keys[n]   = packed;
                counts[n] = 1;
                n++;
            }
        }

        if (n == 0) return null;

        uint bestKey  = 0;
        var  bestCnt  = 0;
        for (var i = 0; i < n; i++)
        {
            if (counts[i] <= bestCnt) continue;
            bestCnt = counts[i];
            bestKey = keys[i];
        }

        return new Rgba32(
            (byte)(bestKey >> 16),
            (byte)((bestKey >> 8) & 0xFF),
            (byte)(bestKey & 0xFF),
            255);
    }
}

using AiPixelScaler.Core.Geometry;
using SixLabors.ImageSharp;
using SixLabors.ImageSharp.PixelFormats;

namespace AiPixelScaler.Core.Pipeline.Imaging;

/// <summary>
/// Bounding box dei pixel con alpha ≥ <paramref name="alphaThreshold"/>.
///
/// Implementazione ottimizzata: scan dei 4 lati indipendenti — top→bottom,
/// bottom→top, left→right, right→left — ognuno si ferma alla prima riga/colonna
/// che contiene un pixel non-trasparente. Su sprite "respinti" al centro questo
/// è ~4× più veloce di un full-scan.
/// </summary>
public static class AlphaBoundingBox
{
    public static AxisAlignedBox Compute(Image<Rgba32> image, byte alphaThreshold = 1)
    {
        var w = image.Width;
        var h = image.Height;
        if (w == 0 || h == 0) return new AxisAlignedBox(0, 0, 0, 0);

        // Top
        var minY = -1;
        for (var y = 0; y < h && minY < 0; y++)
            if (RowHasOpaque(image, y, alphaThreshold)) minY = y;
        if (minY < 0) return new AxisAlignedBox(0, 0, 0, 0);

        // Bottom — default a minY (single-row case): se nessun'altra riga è opaca,
        // il box è alto 1 riga (la stessa di minY).
        var maxY = minY;
        for (var y = h - 1; y > minY; y--)
            if (RowHasOpaque(image, y, alphaThreshold)) { maxY = y; break; }

        // Left
        var minX = 0;
        for (var x = 0; x < w; x++)
            if (ColumnHasOpaqueInRange(image, x, minY, maxY, alphaThreshold)) { minX = x; break; }

        // Right — default a minX (single-column case)
        var maxX = minX;
        for (var x = w - 1; x > minX; x--)
            if (ColumnHasOpaqueInRange(image, x, minY, maxY, alphaThreshold)) { maxX = x; break; }

        return AxisAlignedBox.FromInclusivePixelBounds(minX, minY, maxX, maxY);
    }

    private static bool RowHasOpaque(Image<Rgba32> image, int y, byte threshold)
    {
        var found = false;
        image.ProcessPixelRows(a =>
        {
            var row = a.GetRowSpan(y);
            for (var x = 0; x < row.Length; x++)
                if (row[x].A >= threshold) { found = true; return; }
        });
        return found;
    }

    private static bool ColumnHasOpaqueInRange(Image<Rgba32> image, int x, int y0, int y1, byte threshold)
    {
        var found = false;
        image.ProcessPixelRows(a =>
        {
            for (var y = y0; y <= y1; y++)
                if (a.GetRowSpan(y)[x].A >= threshold) { found = true; return; }
        });
        return found;
    }
}

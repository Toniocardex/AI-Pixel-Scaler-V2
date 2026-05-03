using AiPixelScaler.Core.Color;
using SixLabors.ImageSharp;
using SixLabors.ImageSharp.PixelFormats;
using System.Collections.Generic;

namespace AiPixelScaler.Core.Pipeline.Imaging;

/// <summary>
/// Rilevamento e rimozione di pixel anomali dopo la rimozione sfondo.
///
/// Due algoritmi complementari:
///   <see cref="RemoveIsolatedIslands"/> — rimuove cluster opachi piccoli
///     (componenti connesse di dimensione &lt; minSize).
///   <see cref="RemoveColorOutliers"/>   — rimuove pixel il cui colore Oklab
///     è troppo distante da qualsiasi vicino opaco (pixel "fuori posto").
///
/// Entrambi impostano i pixel rimossi a RGBA(0,0,0,0) — no alpha bleeding.
/// Applicare dopo rimozione sfondo (flood o chroma-key).
/// </summary>
public static class AnomalyDetector
{
    /// <summary>
    /// Rimuove tutte le componenti connesse opache (8-connectivity) con meno
    /// di <paramref name="minSize"/> pixel.
    ///
    /// Algoritmo: BFS su flat array. O(w×h) tempo e spazio.
    /// Tipico uso: rimuovere residui isolati 1-3 px dopo rimozione sfondo.
    /// </summary>
    /// <param name="image">Immagine da modificare in-place.</param>
    /// <param name="minSize">Dimensione minima (esclusa) da mantenere. Default 4.</param>
    /// <returns>Numero di pixel rimossi.</returns>
    public static int RemoveIsolatedIslands(Image<Rgba32> image, int minSize = 4)
    {
        var w       = image.Width;
        var h       = image.Height;
        var pixels  = ImageUtils.ToFlatArray(image);
        var visited = new bool[w * h];
        var toErase = new bool[w * h];

        var q         = new Queue<int>();
        var component = new List<int>(minSize * 4);

        for (var startIdx = 0; startIdx < pixels.Length; startIdx++)
        {
            if (visited[startIdx] || pixels[startIdx].A == 0) continue;

            // BFS: raccoglie la componente connessa
            component.Clear();
            visited[startIdx] = true;
            q.Enqueue(startIdx);

            while (q.Count > 0)
            {
                var idx = q.Dequeue();
                component.Add(idx);

                var py = idx / w;
                var px = idx - py * w;

                // 8-connectivity
                for (var dy = -1; dy <= 1; dy++)
                for (var dx = -1; dx <= 1; dx++)
                {
                    if (dx == 0 && dy == 0) continue;
                    var nx = px + dx;
                    var ny = py + dy;
                    if ((uint)nx >= (uint)w || (uint)ny >= (uint)h) continue;
                    var nidx = ny * w + nx;
                    if (visited[nidx] || pixels[nidx].A == 0) continue;
                    visited[nidx] = true;
                    q.Enqueue(nidx);
                }
            }

            // Componente troppo piccola → segna per rimozione
            if (component.Count < minSize)
                foreach (var idx in component)
                    toErase[idx] = true;
        }

        var removedCount = 0;
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
                    removedCount++;
                }
            }
        });
        return removedCount;
    }

    /// <summary>
    /// Rimuove i pixel opachi il cui colore Oklab è troppo distante da tutti i
    /// vicini opachi (8-connectivity). Un pixel viene classificato "outlier" se:
    ///   - Non ha vicini opachi (pixel completamente isolato), OPPURE
    ///   - La distanza Oklab minima da qualsiasi vicino opaco supera la soglia.
    ///
    /// Utile per eliminare pixel "stray" di colore sbagliato rimasti dopo il flood
    /// (es. un pixel verde in una zona blu, invisibile ad occhio nudo ma presente).
    ///
    /// La soglia <paramref name="rgbTolerance"/> è calibrata tramite
    /// <see cref="BackgroundIsolation.CalibrateOklabToleranceSq"/> per coerenza
    /// con il resto della pipeline.
    /// </summary>
    /// <param name="image">Immagine da modificare in-place.</param>
    /// <param name="rgbTolerance">
    /// Distanza RGB [0-255] sotto la quale un pixel è considerato "simile" al vicino.
    /// Default 20 (differenza visiva percettibile ma non ovvia per pixel art).
    /// </param>
    /// <returns>Numero di pixel rimossi.</returns>
    public static int RemoveColorOutliers(Image<Rgba32> image, double rgbTolerance = 20)
    {
        var w       = image.Width;
        var h       = image.Height;
        var pixels  = ImageUtils.ToFlatArray(image);
        var toErase = new bool[w * h];

        for (var y = 0; y < h; y++)
        for (var x = 0; x < w; x++)
        {
            var idx = y * w + x;
            var p   = pixels[idx];
            if (p.A == 0) continue; // già trasparente

            var oklabP   = Oklab.FromSrgb(p);
            // Calibra la soglia per questo specifico colore (8 direzioni)
            var tolSq    = BackgroundIsolation.CalibrateOklabToleranceSq(p, rgbTolerance);

            var hasCompatibleNeighbor = false;

            for (var dy = -1; dy <= 1 && !hasCompatibleNeighbor; dy++)
            for (var dx = -1; dx <= 1 && !hasCompatibleNeighbor; dx++)
            {
                if (dx == 0 && dy == 0) continue;
                var nx = x + dx;
                var ny = y + dy;
                if ((uint)nx >= (uint)w || (uint)ny >= (uint)h) continue;
                var n = pixels[ny * w + nx];
                if (n.A == 0) continue; // vicino trasparente: non conta

                var distSq = (double)Oklab.DistanceSquared(oklabP, Oklab.FromSrgb(n));
                if (distSq <= tolSq)
                    hasCompatibleNeighbor = true;
            }

            if (!hasCompatibleNeighbor)
                toErase[idx] = true;
        }

        var removedCount = 0;
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
                    removedCount++;
                }
            }
        });
        return removedCount;
    }
}

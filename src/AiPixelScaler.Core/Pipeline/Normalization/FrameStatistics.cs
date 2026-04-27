using AiPixelScaler.Core.Geometry;

namespace AiPixelScaler.Core.Pipeline.Normalization;

/// <summary>
/// Statistiche di dimensioni su una sequenza di bounding box (tipicamente: AABB
/// di contenuto opaco frame-per-frame). Risolve il "problema dell'outlier":
/// usare <c>max(W)</c>/<c>max(H)</c> classico fa esplodere la cella di destinazione
/// se un singolo frame è molto più alto/largo degli altri (capelli, mantello, …).
///
/// Politiche disponibili:
///   • <see cref="NormalizePolicy.Max"/>          — classico, gonfia per outlier
///   • <see cref="NormalizePolicy.Median"/>       — robusto, ignora completamente la coda
///   • <see cref="NormalizePolicy.Percentile90"/> — clip al 90° percentile (perde alcuni px del frame max)
/// </summary>
public static class FrameStatistics
{
    public sealed record SizeStats(
        int MaxW,           int MaxH,
        int MedianW,        int MedianH,
        int Percentile90W,  int Percentile90H,
        int OutlierCountW,  int OutlierCountH,
        int Count);

    public enum NormalizePolicy
    {
        Max,
        Median,
        Percentile90,
    }

    /// <summary>
    /// Calcola statistiche su una lista di box. Considera "outlier" un box con
    /// dimensione &gt; 2× la mediana sull'asse corrispondente.
    /// </summary>
    public static SizeStats Compute(IReadOnlyList<AxisAlignedBox> boxes)
    {
        if (boxes is null || boxes.Count == 0)
            return new SizeStats(0, 0, 0, 0, 0, 0, 0, 0, 0);

        var widths  = new int[boxes.Count];
        var heights = new int[boxes.Count];
        for (var i = 0; i < boxes.Count; i++)
        {
            widths[i]  = boxes[i].Width;
            heights[i] = boxes[i].Height;
        }
        Array.Sort(widths);
        Array.Sort(heights);

        var medianW = widths[widths.Length   / 2];
        var medianH = heights[heights.Length / 2];

        var p90W = widths [Math.Min((int)Math.Ceiling(widths.Length  * 0.9) - 1, widths.Length  - 1)];
        var p90H = heights[Math.Min((int)Math.Ceiling(heights.Length * 0.9) - 1, heights.Length - 1)];
        if (widths.Length  == 1) p90W = widths[0];
        if (heights.Length == 1) p90H = heights[0];

        var outlierW = 0;
        var outlierH = 0;
        for (var i = 0; i < widths.Length; i++)
        {
            if (widths[i]  > 2 * medianW) outlierW++;
            if (heights[i] > 2 * medianH) outlierH++;
        }

        return new SizeStats(
            MaxW: widths[^1],          MaxH: heights[^1],
            MedianW: medianW,           MedianH: medianH,
            Percentile90W: p90W,        Percentile90H: p90H,
            OutlierCountW: outlierW,    OutlierCountH: outlierH,
            Count: boxes.Count);
    }

    /// <summary>Dimensione normalizzata (W,H) in base alla policy scelta.</summary>
    public static (int W, int H) SelectSize(SizeStats stats, NormalizePolicy policy) => policy switch
    {
        NormalizePolicy.Max          => (stats.MaxW,          stats.MaxH),
        NormalizePolicy.Median       => (stats.MedianW,       stats.MedianH),
        NormalizePolicy.Percentile90 => (stats.Percentile90W, stats.Percentile90H),
        _                            => (stats.MaxW,          stats.MaxH),
    };

    /// <summary>Avviso sintetico se ci sono outlier significativi (≥ 1 frame).</summary>
    public static string? FormatOutlierWarning(SizeStats stats)
    {
        if (stats.OutlierCountW == 0 && stats.OutlierCountH == 0) return null;
        var parts = new List<string>(2);
        if (stats.OutlierCountW > 0) parts.Add($"{stats.OutlierCountW} outlier W (W>2×mediana)");
        if (stats.OutlierCountH > 0) parts.Add($"{stats.OutlierCountH} outlier H (H>2×mediana)");
        return $"⚠ Max-policy gonfia la cella: {string.Join(", ", parts)}. Considera Median o Percentile90.";
    }
}

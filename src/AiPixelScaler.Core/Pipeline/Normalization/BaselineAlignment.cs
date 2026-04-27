using AiPixelScaler.Core.Geometry;
using AiPixelScaler.Core.Pipeline.Slicing;
using SixLabors.ImageSharp;
using SixLabors.ImageSharp.PixelFormats;
using SixLabors.ImageSharp.Processing;

namespace AiPixelScaler.Core.Pipeline.Normalization;

/// <summary>
/// Baseline Alignment — Allineamento dei piedi e Normalizzazione.
///
/// Obiettivo: inserire sprite di dimensioni diverse in celle identiche, allineandoli
/// per i piedi (baseline) per evitare l'effetto "Jittering" (tremolio o affondamento)
/// nel game engine durante la riproduzione dell'animazione.
///
/// Algoritmo:
///   1. Per ogni sprite si trova Y_bottom: la riga più in basso contenente almeno un pixel solido.
///   2. Si calcola H_max: altezza massima tra tutti gli sprite.
///   3. Per ogni sprite: ΔY = H_max - 1 - Y_bottom  →  lo sposta in modo che il pixel
///      più in basso coincida con il fondo della cella normalizzata.
///   4. Si centra orizzontalmente nella cella larga W_max.
///   5. Si riassembla un nuovo atlas in riga con le celle normalizzate.
/// </summary>
public static class BaselineAlignment
{
    public sealed class Result : IDisposable
    {
        public required Image<Rgba32> Atlas { get; init; }
        public required IReadOnlyList<SpriteCell> Cells { get; init; }
        public void Dispose() => Atlas.Dispose();
    }

    /// <summary>
    /// Allinea tutti gli sprite per i piedi e ritorna un nuovo atlas normalizzato.
    /// Usa policy <see cref="FrameStatistics.NormalizePolicy.Max"/> per default
    /// (compat con codice esistente).
    /// </summary>
    /// <param name="atlas">Immagine sorgente (atlas corrente).</param>
    /// <param name="cells">Celle da normalizzare.</param>
    /// <param name="alphaThreshold">Soglia alpha per considerare un pixel "solido".</param>
    public static Result Align(
        Image<Rgba32> atlas,
        IReadOnlyList<SpriteCell> cells,
        byte alphaThreshold = 1)
        => Align(atlas, cells, FrameStatistics.NormalizePolicy.Max, alphaThreshold);

    /// <summary>
    /// Overload con politica esplicita di normalizzazione cella:
    ///   • Max          → classica, gonfia se c'è un outlier (un singolo frame molto alto).
    ///   • Median       → robusta, ignora la coda. Frame outlier vengono CLIPPATI.
    ///   • Percentile90 → soft cap (perde solo i top-10% px del frame max).
    /// </summary>
    public static Result Align(
        Image<Rgba32> atlas,
        IReadOnlyList<SpriteCell> cells,
        FrameStatistics.NormalizePolicy policy,
        byte alphaThreshold = 1)
    {
        if (cells.Count == 0)
            return new Result { Atlas = new Image<Rgba32>(1, 1), Cells = [] };

        // 1. Ritaglia ogni sprite e calcola Y_bottom
        var crops = new List<(SpriteCell cell, Image<Rgba32> img, int yBottom)>(cells.Count);
        foreach (var cell in cells)
        {
            var img = AtlasCropper.Crop(atlas, cell.BoundsInAtlas);
            var yBottom = FindBottomSolidRow(img, alphaThreshold);
            crops.Add((cell, img, yBottom));
        }

        // 2. Dimensioni cella normalizzata via policy (max / median / p90)
        var boxes = crops.Select(c => new AxisAlignedBox(0, 0, c.img.Width, c.img.Height)).ToList();
        var stats = FrameStatistics.Compute(boxes);
        var (wTarget, hTarget) = FrameStatistics.SelectSize(stats, policy);
        if (wTarget < 1) wTarget = 1;
        if (hTarget < 1) hTarget = 1;

        // 3. Costruisci il nuovo atlas riga (frame outlier clippati alla cella target)
        var totalW = wTarget * crops.Count;
        var newAtlas = new Image<Rgba32>(totalW, hTarget, new Rgba32(0, 0, 0, 0));
        var newCells = new List<SpriteCell>(crops.Count);

        for (var i = 0; i < crops.Count; i++)
        {
            var (cell, img, yBottom) = crops[i];

            // ΔY = h_target - 1 - Y_bottom  (allinea il pixel più basso al fondo della cella)
            var dy = yBottom >= 0 ? hTarget - 1 - yBottom : 0;
            // Centra orizzontalmente
            var dx = (wTarget - img.Width) / 2;

            var destX = i * wTarget;
            var destPoint = new Point(destX + dx, dy);
            // Mutate gestisce il clipping al canvas di destinazione (frame outlier vengono tagliati)
            newAtlas.Mutate(ctx => ctx.DrawImage(img, destPoint, 1f));

            newCells.Add(new SpriteCell(
                cell.Id,
                new AxisAlignedBox(destX, 0, destX + wTarget, hTarget),
                cell.PivotNdcX,
                cell.PivotNdcY));

            img.Dispose();
        }

        return new Result { Atlas = newAtlas, Cells = newCells };
    }

    /// <summary>
    /// Trova la riga Y più in basso (indice più alto) che contiene almeno un pixel con Alpha &gt; soglia.
    /// Ritorna -1 se l'immagine è completamente trasparente.
    /// </summary>
    public static int FindBottomSolidRow(Image<Rgba32> img, byte alphaThreshold = 1)
    {
        for (var y = img.Height - 1; y >= 0; y--)
        {
            var rowHasSolid = false;
            img.ProcessPixelRows(accessor =>
            {
                var row = accessor.GetRowSpan(y);
                foreach (var p in row)
                    if (p.A > alphaThreshold) { rowHasSolid = true; break; }
            });
            if (rowHasSolid) return y;
        }
        return -1;
    }
}

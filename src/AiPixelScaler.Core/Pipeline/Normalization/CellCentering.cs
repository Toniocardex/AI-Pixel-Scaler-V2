using AiPixelScaler.Core.Geometry;
using AiPixelScaler.Core.Pipeline.Imaging;
using AiPixelScaler.Core.Pipeline.Slicing;
using SixLabors.ImageSharp;
using SixLabors.ImageSharp.PixelFormats;
using SixLabors.ImageSharp.Processing;

namespace AiPixelScaler.Core.Pipeline.Normalization;

/// <summary>
/// Centra il contenuto opaco di ogni sprite al centro della propria cella.
/// Utile dopo un'espansione POT: l'atlas mantiene le stesse dimensioni,
/// i frame vengono riposizionati in modo che l'AABB opaca sia centrata nella cella.
///
/// Opzionale: dopo il centro geometrico, offset minimo sull’asse X/Y così che l’angolo alto-sinistra
/// dell’AABB opaca nell’atlas cada su multipli di <paramref name="opaqueCornerSnapMultiple"/>
/// (default 8 — standard pixel art), così combacia con la griglia di overlay canvas senza “mezzi pixel”.
/// </summary>
public static class CellCentering
{
    public sealed class Result : IDisposable
    {
        public required Image<Rgba32> Atlas { get; init; }
        public required IReadOnlyList<SpriteCell> Cells { get; init; }
        public void Dispose() => Atlas.Dispose();
    }

    /// <summary>
    /// Dopo aver centrato il contenuto, sceglie la posizione di incollo del crop (origine crop = atlas)
    /// più vicina alla posizione ideale tale che <c>pasteDest + opaqueMinLocal ≡ 0 (mod g)</c>,
    /// restando dentro i limiti della cella. Così la silhouette risulta allineata alla griglia globale.
    /// </summary>
    public static int SnapPasteDestForOpaqueTopLeftModulo(
        int idealPasteDest,
        int opaqueMinLocal,
        int opaqueMaxExclusiveLocal,
        int cellMinAtlas,
        int cellMaxExclusiveAtlas,
        int gridMultiple)
    {
        if (gridMultiple < 2)
            return idealPasteDest;

        var lo = cellMinAtlas - opaqueMinLocal;
        var hi = cellMaxExclusiveAtlas - opaqueMaxExclusiveLocal;
        if (lo > hi)
            return idealPasteDest;

        // Celle più strette del passo griglia: non c'è margine per agganciare senza distruggere il centro.
        if (hi - lo < gridMultiple)
            return idealPasteDest;

        static int NormMod(int v, int m)
        {
            var r = v % m;
            return r < 0 ? r + m : r;
        }

        var best = idealPasteDest;
        var bestAbs = int.MaxValue;
        for (var d = lo; d <= hi; d++)
        {
            if (NormMod(d + opaqueMinLocal, gridMultiple) != 0)
                continue;
            var dist = Math.Abs(d - idealPasteDest);
            if (dist < bestAbs || (dist == bestAbs && d < best))
            {
                bestAbs = dist;
                best = d;
            }
        }

        return best;
    }

    public static Result Center(
        Image<Rgba32> atlas,
        IReadOnlyList<SpriteCell> cells,
        byte alphaThreshold = 1,
        int opaqueCornerSnapMultiple = 8)
    {
        if (cells.Count == 0)
            return new Result { Atlas = atlas.Clone(), Cells = cells };

        var newAtlas = new Image<Rgba32>(atlas.Width, atlas.Height, new Rgba32(0, 0, 0, 0));
        var g = opaqueCornerSnapMultiple >= 2 ? opaqueCornerSnapMultiple : 0;

        foreach (var cell in cells)
        {
            var box = cell.BoundsInAtlas;
            using var crop = AtlasCropper.Crop(atlas, box);

            var opaqueBox = FindOpaqueBox(crop, alphaThreshold);
            if (opaqueBox is null) continue;

            var ob = opaqueBox.Value;
            var opaqueCx = ob.MinX + ob.Width  / 2;
            var opaqueCy = ob.MinY + ob.Height / 2;

            var dx = box.Width  / 2 - opaqueCx;
            var dy = box.Height / 2 - opaqueCy;

            var destX = box.MinX + dx;
            var destY = box.MinY + dy;

            if (g >= 2)
            {
                destX = SnapPasteDestForOpaqueTopLeftModulo(destX, ob.MinX, ob.MaxX, box.MinX, box.MaxX, g);
                destY = SnapPasteDestForOpaqueTopLeftModulo(destY, ob.MinY, ob.MaxY, box.MinY, box.MaxY, g);
            }

            newAtlas.Mutate(ctx => ctx.DrawImage(crop, new Point(destX, destY), 1f));
        }

        return new Result { Atlas = newAtlas, Cells = cells };
    }

    public static AxisAlignedBox? FindOpaqueBox(Image<Rgba32> img, byte alphaThreshold = 1)
    {
        var box = AlphaBoundingBox.Compute(img, alphaThreshold);
        return box.IsEmpty ? null : box;
    }
}

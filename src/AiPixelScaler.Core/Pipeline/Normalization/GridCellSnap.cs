using AiPixelScaler.Core.Geometry;
using AiPixelScaler.Core.Pipeline.Slicing;
using SixLabors.ImageSharp;
using SixLabors.ImageSharp.PixelFormats;
using SixLabors.ImageSharp.Processing;
using Point = SixLabors.ImageSharp.Point;

namespace AiPixelScaler.Core.Pipeline.Normalization;

/// <summary>
/// Riposiziona ogni sprite nell’atlas così che l’angolo superiore sinistro della cella cada su multipli di una griglia globale
/// con origine (0,0), come il reticolo di overlay nel canvas (passo <paramref name="gridSize"/> px).
/// Le dimensioni delle celle restano invariate; in caso di collisione tra regioni dopo lo spostamento l’ordine di disegno
/// segue l’ordine della lista (l’ultima sovrascrive).
/// </summary>
public static class GridCellSnap
{
    public sealed class Result : IDisposable
    {
        public required Image<Rgba32> Atlas { get; init; }
        public required IReadOnlyList<SpriteCell> Cells { get; init; }

        public void Dispose() => Atlas.Dispose();
    }

    public static Result SnapToReferenceGrid(
        Image<Rgba32> atlas,
        IReadOnlyList<SpriteCell> cells,
        int gridSize)
    {
        if (cells.Count == 0)
            return new Result { Atlas = atlas.Clone(), Cells = cells };

        var g = Math.Max(1, gridSize);

        var newAtlas = new Image<Rgba32>(atlas.Width, atlas.Height, new Rgba32(0, 0, 0, 0));
        var newCells = new List<SpriteCell>(cells.Count);

        foreach (var cell in cells)
        {
            var box = cell.BoundsInAtlas;
            if (box.Width < 1 || box.Height < 1)
                continue;

            using var crop = AtlasCropper.Crop(atlas, box);

            var snappedX = SnapOriginToGrid(box.MinX, g);
            var snappedY = SnapOriginToGrid(box.MinY, g);
            snappedX = ClampOrigin(snappedX, box.Width, atlas.Width);
            snappedY = ClampOrigin(snappedY, box.Height, atlas.Height);

            newAtlas.Mutate(ctx => ctx.DrawImage(crop, new Point(snappedX, snappedY), 1f));

            var newBox = new AxisAlignedBox(
                snappedX,
                snappedY,
                snappedX + box.Width,
                snappedY + box.Height);

            newCells.Add(new SpriteCell(cell.Id, newBox, cell.PivotNdcX, cell.PivotNdcY));
        }

        return new Result { Atlas = newAtlas, Cells = newCells };
    }

    /// <summary>Pure: per interi non negativi, allinea verso il basso al multiplo di <paramref name="g"/> più vicino verso 0.</summary>
    internal static int SnapOriginToGrid(int minCoord, int g) =>
        minCoord - PositiveMod(minCoord, g);

    private static int PositiveMod(int x, int g)
    {
        var m = x % g;
        return m < 0 ? m + g : m;
    }

    private static int ClampOrigin(int origin, int extent, int atlasDim)
    {
        if (extent < 1 || extent > atlasDim)
            return Math.Clamp(origin, 0, Math.Max(0, atlasDim - 1));

        var maxOrigin = atlasDim - extent;
        return Math.Clamp(origin, 0, maxOrigin);
    }
}

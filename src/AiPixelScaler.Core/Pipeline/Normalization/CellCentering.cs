using AiPixelScaler.Core.Geometry;
using AiPixelScaler.Core.Pipeline.Slicing;
using SixLabors.ImageSharp;
using SixLabors.ImageSharp.PixelFormats;
using SixLabors.ImageSharp.Processing;

namespace AiPixelScaler.Core.Pipeline.Normalization;

/// <summary>
/// Centra il contenuto opaco di ogni sprite al centro della propria cella.
/// Utile dopo un'espansione POT: l'atlas mantiene le stesse dimensioni,
/// i frame vengono riposizionati in modo che l'AABB opaca sia centrata nella cella.
/// </summary>
public static class CellCentering
{
    public sealed class Result : IDisposable
    {
        public required Image<Rgba32> Atlas { get; init; }
        public required IReadOnlyList<SpriteCell> Cells { get; init; }
        public void Dispose() => Atlas.Dispose();
    }

    public static Result Center(
        Image<Rgba32> atlas,
        IReadOnlyList<SpriteCell> cells,
        byte alphaThreshold = 1)
    {
        if (cells.Count == 0)
            return new Result { Atlas = atlas.Clone(), Cells = cells };

        var newAtlas = new Image<Rgba32>(atlas.Width, atlas.Height, new Rgba32(0, 0, 0, 0));

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
            newAtlas.Mutate(ctx => ctx.DrawImage(crop, new Point(destX, destY), 1f));
        }

        return new Result { Atlas = newAtlas, Cells = cells };
    }

    public static AxisAlignedBox? FindOpaqueBox(Image<Rgba32> img, byte alphaThreshold = 1)
    {
        var minX = img.Width;
        var minY = img.Height;
        var maxX = -1;
        var maxY = -1;

        img.ProcessPixelRows(accessor =>
        {
            for (var y = 0; y < accessor.Height; y++)
            {
                var row = accessor.GetRowSpan(y);
                for (var x = 0; x < row.Length; x++)
                {
                    if (row[x].A >= alphaThreshold)
                    {
                        if (x < minX) minX = x;
                        if (x > maxX) maxX = x;
                        if (y < minY) minY = y;
                        if (y > maxY) maxY = y;
                    }
                }
            }
        });

        if (maxX < 0) return null;
        return new AxisAlignedBox(minX, minY, maxX + 1, maxY + 1);
    }
}

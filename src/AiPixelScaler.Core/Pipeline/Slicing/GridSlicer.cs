using AiPixelScaler.Core.Geometry;

namespace AiPixelScaler.Core.Pipeline.Slicing;

public static class GridSlicer
{
    public static IReadOnlyList<SpriteCell> Slice(int atlasWidth, int atlasHeight, int rows, int cols)
    {
        if (rows < 1 || cols < 1)
            return Array.Empty<SpriteCell>();

        var cellW = (atlasWidth / cols) & ~7;   // snap to nearest multiple of 8 (pixel art standard)
        var cellH = (atlasHeight / rows) & ~7;
        if (cellW < 1 || cellH < 1)
            return Array.Empty<SpriteCell>();

        var list = new List<SpriteCell>(rows * cols);
        for (var r = 0; r < rows; r++)
        for (var c = 0; c < cols; c++)
        {
            var minX = c * cellW;
            var minY = r * cellH;
            var id = $"r{r}c{c}";
            var box = new AxisAlignedBox(minX, minY, minX + cellW, minY + cellH);
            list.Add(new SpriteCell(id, box));
        }
        return list;
    }
}

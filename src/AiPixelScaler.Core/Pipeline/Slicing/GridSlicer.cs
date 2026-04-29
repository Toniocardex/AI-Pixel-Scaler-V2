using AiPixelScaler.Core.Geometry;

namespace AiPixelScaler.Core.Pipeline.Slicing;

public static class GridSlicer
{
    /// <summary>
    /// Stesse dimensioni cella usate da <see cref="Slice"/> — va riusata ovunque si disegni una griglia slice
    /// sopra l’atlas (overlay canvas), altrimenti linee e <see cref="SpriteCell"/> non coincidono.
    /// </summary>
    public static (int CellW, int CellH) ComputeCellSize(int atlasWidth, int atlasHeight, int rows, int cols)
    {
        if (rows < 1 || cols < 1)
            return (0, 0);
        var cellW = (atlasWidth / cols) & ~7;
        var cellH = (atlasHeight / rows) & ~7;
        return (cellW, cellH);
    }

    public static IReadOnlyList<SpriteCell> Slice(int atlasWidth, int atlasHeight, int rows, int cols)
    {
        if (rows < 1 || cols < 1)
            return Array.Empty<SpriteCell>();

        var (cellW, cellH) = ComputeCellSize(atlasWidth, atlasHeight, rows, cols);
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

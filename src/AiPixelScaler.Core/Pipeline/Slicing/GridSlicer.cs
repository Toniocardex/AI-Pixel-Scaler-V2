using AiPixelScaler.Core.Geometry;
using AiPixelScaler.Core.Pipeline.Templates;

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

    /// <summary>
    /// Suddivide il canvas come una griglia rettangolare di dimensioni fisse <paramref name="cellWidth"/>×<paramref name="cellHeight"/>
    /// (senza arrotondamento a multipli di 8). Va usato quando l’atlas è stato creato con le stesse dimensioni,
    /// es. da <see cref="GridTemplateGenerator"/> (<c>cols × cellWidth</c>, <c>rows × cellHeight</c>).
    /// </summary>
    public static IReadOnlyList<SpriteCell> SliceExact(int cols, int rows, int cellWidth, int cellHeight)
    {
        if (rows < 1 || cols < 1 || cellWidth < 1 || cellHeight < 1)
            return Array.Empty<SpriteCell>();

        var list = new List<SpriteCell>(rows * cols);
        for (var r = 0; r < rows; r++)
        for (var c = 0; c < cols; c++)
        {
            var minX = c * cellWidth;
            var minY = r * cellHeight;
            var box = new AxisAlignedBox(minX, minY, minX + cellWidth, minY + cellHeight);
            list.Add(new SpriteCell($"r{r}c{c}", box));
        }

        return list;
    }

    /// <summary>
    /// Griglia fissa con gutter tra tile: origine (0,0); ogni colonna è spostata di <c>cellWidth + spacingX</c> px sulla X.
    /// </summary>
    public static IReadOnlyList<SpriteCell> SliceExactWithSpacing(
        int cols,
        int rows,
        int cellWidth,
        int cellHeight,
        int spacingX,
        int spacingY)
    {
        if (rows < 1 || cols < 1 || cellWidth < 1 || cellHeight < 1)
            return Array.Empty<SpriteCell>();

        spacingX = Math.Max(0, spacingX);
        spacingY = Math.Max(0, spacingY);

        var list = new List<SpriteCell>(rows * cols);
        var periodX = cellWidth + spacingX;
        var periodY = cellHeight + spacingY;

        for (var r = 0; r < rows; r++)
        for (var c = 0; c < cols; c++)
        {
            var minX = c * periodX;
            var minY = r * periodY;
            var box = new AxisAlignedBox(minX, minY, minX + cellWidth, minY + cellHeight);
            list.Add(new SpriteCell($"r{r}c{c}", box));
        }

        return list;
    }
}

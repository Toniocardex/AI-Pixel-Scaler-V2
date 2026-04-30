using System.Collections.Generic;
using System.Linq;
using AiPixelScaler.Core.Pipeline.Slicing;
using SixLabors.ImageSharp;
using SixLabors.ImageSharp.PixelFormats;

namespace AiPixelScaler.Desktop.ViewModels;

public sealed class WorkspaceUndoSnapshot
{
    public required Image<Rgba32> Image { get; init; }
    public required List<SpriteCell> Cells { get; init; }
    public int GridRows { get; init; }
    public int GridCols { get; init; }
    public required List<SpriteCell> SpriteOverlay { get; init; }

    public static WorkspaceUndoSnapshot Capture(
        Image<Rgba32> doc,
        IReadOnlyList<SpriteCell> cells,
        int gridRows,
        int gridCols,
        IReadOnlyList<SpriteCell> overlay) =>
        new()
        {
            Image = doc.Clone(),
            Cells = CloneCells(cells),
            GridRows = gridRows,
            GridCols = gridCols,
            SpriteOverlay = CloneCells(overlay)
        };

    public static List<SpriteCell> CloneCells(IEnumerable<SpriteCell> src) =>
        src.Select(c => new SpriteCell(c.Id, c.BoundsInAtlas, c.PivotNdcX, c.PivotNdcY)).ToList();
}

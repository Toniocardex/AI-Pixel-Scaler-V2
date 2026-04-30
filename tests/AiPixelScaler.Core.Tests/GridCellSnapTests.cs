using AiPixelScaler.Core.Geometry;
using AiPixelScaler.Core.Pipeline.Normalization;
using AiPixelScaler.Core.Pipeline.Slicing;
using SixLabors.ImageSharp;
using SixLabors.ImageSharp.PixelFormats;

namespace AiPixelScaler.Core.Tests;

public class GridCellSnapTests
{
    [Fact]
    public void SnapToReferenceGrid_moves_cell_origin_to_grid_multiples()
    {
        using var atlas = new Image<Rgba32>(64, 64, new Rgba32(255, 0, 0, 255));
        var cells = new List<SpriteCell>
        {
            new("a", new AxisAlignedBox(10, 10, 26, 26)), // 16×16: min (10,10) → snap a (0,0) con G=16
        };

        using var r = GridCellSnap.SnapToReferenceGrid(atlas, cells, gridSize: 16);
        Assert.Single(r.Cells);
        var b = r.Cells[0].BoundsInAtlas;
        Assert.Equal(0, b.MinX);
        Assert.Equal(0, b.MinY);
        Assert.Equal(16, b.Width);
        Assert.Equal(16, b.Height);
    }

    [Fact]
    public void SnapToReferenceGrid_preserves_cell_size()
    {
        using var atlas = new Image<Rgba32>(128, 128, new Rgba32(0, 255, 0, 255));
        var cells = new List<SpriteCell>
        {
            new("a", new AxisAlignedBox(17, 33, 49, 65)), // 32×32
        };

        using var r = GridCellSnap.SnapToReferenceGrid(atlas, cells, gridSize: 16);
        var b = r.Cells[0].BoundsInAtlas;
        Assert.Equal(32, b.Width);
        Assert.Equal(32, b.Height);
        Assert.Equal(16, b.MinX); // 17 → 16
        Assert.Equal(32, b.MinY); // 33 → 32
    }
}

using AiPixelScaler.Core.Pipeline.Slicing;

namespace AiPixelScaler.Core.Tests;

public class GridSlicerTests
{
    [Fact]
    public void ComputeCellSize_matches_integer_floor_then_snap_to_multiple_of_8()
    {
        var (w, h) = GridSlicer.ComputeCellSize(960, 384, rows: 1, cols: 5);
        Assert.Equal(192, w);
        Assert.Equal(384 & ~7, h);
    }

    [Fact]
    public void Slice_cell_origins_align_with_compute_cell_width_steps()
    {
        var cells = GridSlicer.Slice(960, 512, rows: 1, cols: 5).ToList();
        var (cw, _) = GridSlicer.ComputeCellSize(960, 512, 1, 5);
        Assert.Equal(5, cells.Count);
        for (var c = 0; c < 5; c++)
            Assert.Equal(c * cw, cells[c].BoundsInAtlas.MinX);
    }
}

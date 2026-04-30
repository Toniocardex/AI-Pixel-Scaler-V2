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

    [Fact]
    public void SliceExact_tiles_cover_full_atlas_even_when_cell_not_multiple_of_8()
    {
        const int cols = 5;
        const int rows = 3;
        const int cw = 37;
        const int ch = 23;
        var atlasW = cols * cw;
        var atlasH = rows * ch;
        var cells = GridSlicer.SliceExact(cols, rows, cw, ch).ToList();

        Assert.Equal(cols * rows, cells.Count);
        Assert.All(cells, c =>
        {
            Assert.Equal(cw, c.BoundsInAtlas.Width);
            Assert.Equal(ch, c.BoundsInAtlas.Height);
        });

        var last = cells[^1];
        Assert.Equal(atlasW, last.BoundsInAtlas.MaxX);
        Assert.Equal(atlasH, last.BoundsInAtlas.MaxY);
    }

    [Fact]
    public void SliceExact_cell_bounds_differ_from_Slice_when_snapped_to_multiple_of_8()
    {
        const int cols = 4;
        const int rows = 2;
        const int cw = 37;
        const int ch = 40;
        var atlasW = cols * cw;
        var atlasH = rows * ch;

        var exact = GridSlicer.SliceExact(cols, rows, cw, ch)[0].BoundsInAtlas;
        var snapped = GridSlicer.Slice(atlasW, atlasH, rows, cols)[0].BoundsInAtlas;

        Assert.NotEqual(exact.Width, snapped.Width);
        Assert.True(snapped.Width % 8 == 0);
    }
}

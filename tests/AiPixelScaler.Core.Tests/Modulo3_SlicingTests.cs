using AiPixelScaler.Core.Pipeline.Slicing;
using SixLabors.ImageSharp;
using SixLabors.ImageSharp.PixelFormats;

namespace AiPixelScaler.Core.Tests;

public class Modulo3_SlicingTests
{
    [Fact]
    public void GridSlicer_2x2_snaps_to_multiple_of_8()
    {
        // 100/2 = 50 → snapped to 48 (nearest multiple of 8)
        var cells = GridSlicer.Slice(100, 100, 2, 2);
        Assert.Equal(4, cells.Count);
        Assert.Equal(48, cells[0].BoundsInAtlas.Width);
        Assert.Equal(48, cells[0].BoundsInAtlas.Height);
    }

    [Fact]
    public void GridSlicer_2x2_128x128_exact()
    {
        // 128/2 = 64 → already a multiple of 8, unchanged
        var cells = GridSlicer.Slice(128, 128, 2, 2);
        Assert.Equal(4, cells.Count);
        Assert.Equal(64, cells[0].BoundsInAtlas.Width);
        Assert.Equal(64, cells[0].BoundsInAtlas.Height);
    }

    [Fact]
    public void CclAutoSlicer_two_islands()
    {
        using var img = new Image<Rgba32>(20, 20, new Rgba32(0, 0, 0, 0));
        for (var y = 0; y < 3; y++)
        for (var x = 0; x < 3; x++)
            img[x, y] = new Rgba32(255, 0, 0, 255);
        for (var y = 0; y < 3; y++)
        for (var x = 0; x < 3; x++)
            img[10 + x, 10 + y] = new Rgba32(0, 255, 0, 255);

        var cells = CclAutoSlicer.Slice(img);
        Assert.Equal(2, cells.Count);
    }
}

using AiPixelScaler.Core.Geometry;

namespace AiPixelScaler.Core.Tests;

public class AxisAlignedBoxTests
{
    [Fact]
    public void Intersects_overlapping_boxes()
    {
        var a = new AxisAlignedBox(0, 0, 10, 10);
        var b = new AxisAlignedBox(5, 5, 15, 15);
        Assert.True(AxisAlignedBox.Intersects(a, b));
    }

    [Fact]
    public void Intersects_roadmap_touching_separated_on_x()
    {
        var a = new AxisAlignedBox(0, 0, 1, 10);
        var b = new AxisAlignedBox(1, 0, 2, 10);
        Assert.False(AxisAlignedBox.Intersects(a, b));
    }

    /// <summary>
    /// Angoli alla griglia (multipli di 32): il bordo destro/basso è un limite esclusivo → larghezze multipli di 32,
    /// non +1 come con Floor + FromInclusivePixelBounds.
    /// </summary>
    [Fact]
    public void FromWorldCornersHalfOpen_grid_aligned_dimensions_are_exact_multiples_not_plus_one()
    {
        var box = AxisAlignedBox.FromWorldCornersHalfOpen(0, 0, 1280, 608, imgW: 4000, imgH: 4000);
        Assert.Equal(1280, box.Width);
        Assert.Equal(608, box.Height);
        Assert.Equal(0, box.MinX);
        Assert.Equal(0, box.MinY);
        Assert.Equal(1280, box.MaxX);
        Assert.Equal(608, box.MaxY);
    }

    [Fact]
    public void FromWorldCornersHalfOpen_reverse_corners_same_box()
    {
        var a = AxisAlignedBox.FromWorldCornersHalfOpen(1280, 608, 0, 0, 4000, 4000);
        var b = AxisAlignedBox.FromWorldCornersHalfOpen(0, 0, 1280, 608, 4000, 4000);
        Assert.Equal(a, b);
    }
}

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
}

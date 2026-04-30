using AiPixelScaler.Core.Pipeline.Editor;
using Xunit;

namespace AiPixelScaler.Core.Tests;

public class FloatingPasteGeometryTests
{
    [Theory]
    [InlineData(10, 10, 100, 100, 1.0)]
    [InlineData(200, 50, 100, 100, 0.5)]   // width limits
    [InlineData(50, 200, 100, 100, 0.5)]   // height limits
    [InlineData(200, 200, 100, 100, 0.5)]
    public void ComputeUniformFitScale(int sw, int sh, int cw, int ch, double expected)
    {
        var s = FloatingPasteGeometry.ComputeUniformFitScale(sw, sh, cw, ch);
        Assert.Equal(expected, s, precision: 10);
    }

    [Fact]
    public void ComputeDisplayDimensions_atLeastOne()
    {
        var (w, h) = FloatingPasteGeometry.ComputeDisplayDimensions(100, 80, 0.01);
        Assert.True(w >= 1 && h >= 1);
    }

    [Fact]
    public void ComputeCenteredTopLeft_centers()
    {
        var (x, y) = FloatingPasteGeometry.ComputeCenteredTopLeft(100, 100, 40, 30);
        Assert.Equal(30, x);
        Assert.Equal(35, y);
    }
}

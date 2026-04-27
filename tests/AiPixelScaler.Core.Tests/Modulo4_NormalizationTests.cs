using AiPixelScaler.Core.Geometry;
using AiPixelScaler.Core.Pipeline.Normalization;
using AiPixelScaler.Core.Pipeline.Slicing;
using SixLabors.ImageSharp;
using SixLabors.ImageSharp.PixelFormats;

namespace AiPixelScaler.Core.Tests;

public class Modulo4_NormalizationTests
{
    [Fact]
    public void GlobalLayout_max_of_two_different_sized_blobs()
    {
        using var img = new Image<Rgba32>(20, 20, new Rgba32(0, 0, 0, 0));
        for (var y = 0; y < 2; y++)
        for (var x = 0; x < 4; x++)
            img[x, y] = new Rgba32(255, 0, 0, 255);
        for (var y = 0; y < 3; y++)
        for (var x = 10; x < 15; x++)
            img[x, y] = new Rgba32(0, 255, 0, 255);

        var cells = new[]
        {
            new SpriteCell("a", new AxisAlignedBox(0, 0, 10, 10)),
            new SpriteCell("b", new AxisAlignedBox(10, 0, 20, 10))
        };
        var (gw, gh) = GlobalLayout.ComputeGlobalContentSize(img, cells);
        Assert.Equal(5, gw);
        Assert.Equal(3, gh);
    }

}

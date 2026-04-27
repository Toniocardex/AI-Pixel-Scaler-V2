using AiPixelScaler.Core.Pipeline.Imaging;
using SixLabors.ImageSharp;
using SixLabors.ImageSharp.PixelFormats;
namespace AiPixelScaler.Core.Tests;

public class Modulo1_ImageProcessingTests
{
    [Fact]
    public void AlphaBoundingBox_3x3_block_offset()
    {
        using var img = new Image<Rgba32>(10, 10, new Rgba32(0, 0, 0, 0));
        for (var y = 2; y < 5; y++)
        for (var x = 3; x < 6; x++)
            img[x, y] = new Rgba32(255, 0, 0, 255);

        var box = AlphaBoundingBox.Compute(img);
        Assert.Equal(3, box.Width);
        Assert.Equal(3, box.Height);
        Assert.Equal(3, box.MinX);
        Assert.Equal(2, box.MinY);
    }

    [Fact]
    public void IslandDenoise_removes_single_pixel_island()
    {
        using var img = new Image<Rgba32>(8, 8, new Rgba32(0, 0, 0, 0));
        for (var y = 1; y < 4; y++)
        for (var x = 1; x < 4; x++)
            img[x, y] = new Rgba32(0, 255, 0, 255);
        img[7, 7] = new Rgba32(255, 0, 0, 255);

        IslandDenoise.ApplyInPlace(img, new IslandDenoise.Options { MinIslandArea = 2, AlphaThreshold = 1 });
        Assert.Equal(0, img[7, 7].A);
        Assert.Equal(255, img[2, 2].A);
    }
}

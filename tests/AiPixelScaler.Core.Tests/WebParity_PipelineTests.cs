using AiPixelScaler.Core.Pipeline.Imaging;
using SixLabors.ImageSharp;
using SixLabors.ImageSharp.PixelFormats;

namespace AiPixelScaler.Core.Tests;

public class WebParity_PipelineTests
{
    [Fact]
    public void ChromaKey_removes_exact_green()
    {
        using var img = new Image<Rgba32>(2, 1);
        img[0, 0] = new Rgba32(0, 255, 0, 255);
        img[1, 0] = new Rgba32(0, 0, 0, 255);
        ChromaKey.ApplyInPlace(img, new Rgba32(0, 255, 0, 255), 0);
        Assert.Equal(0, img[0, 0].A);
        Assert.Equal(255, img[1, 0].A);
    }

    [Fact]
    public void NearestNeighborResize_doubles()
    {
        using var src = new Image<Rgba32>(1, 1);
        src[0, 0] = new Rgba32(10, 20, 30, 255);
        using var dst = NearestNeighborResize.Resize(src, 2, 2, 0, 0);
        Assert.Equal(2, dst.Width);
        Assert.Equal(10, dst[0, 0].R);
    }

    [Fact]
    public void EdgeBackground_removes_edge_connected_magenta()
    {
        using var img = new Image<Rgba32>(3, 3);
        for (var y = 0; y < 3; y++)
        for (var x = 0; x < 3; x++)
            img[x, y] = new Rgba32(255, 0, 255, 255);
        img[1, 1] = new Rgba32(0, 0, 0, 255);
        EdgeBackgroundFill.ApplyInPlace(img, new Rgba32(255, 0, 255, 255), 0);
        Assert.Equal(0, img[0, 0].A);
        Assert.Equal(255, img[1, 1].A);
    }

    [Fact]
    public void EdgeBackground_stops_at_sobel_edge()
    {
        using var img = new Image<Rgba32>(7, 7, new Rgba32(240, 240, 240, 255));
        for (var y = 2; y <= 4; y++)
        for (var x = 2; x <= 4; x++)
            img[x, y] = new Rgba32(20, 20, 20, 255);

        EdgeBackgroundFill.ApplyInPlace(img, new Rgba32(240, 240, 240, 255), 0);

        Assert.Equal(0, img[0, 0].A);
        Assert.Equal(255, img[3, 3].A);
    }
}

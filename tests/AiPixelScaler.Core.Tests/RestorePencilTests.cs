using AiPixelScaler.Core.Pipeline.Editor;
using SixLabors.ImageSharp;
using SixLabors.ImageSharp.PixelFormats;

namespace AiPixelScaler.Core.Tests;

public class RestorePencilTests
{
    [Fact]
    public void ApplyInPlace_RestoresTransparentPixelWithOpaqueSelectedColor()
    {
        using var image = new Image<Rgba32>(2, 2, new Rgba32(0, 0, 0, 0));
        var changed = RestorePencil.ApplyInPlace(image, 1, 1, 1, new Rgba32(10, 20, 30, 80));

        Assert.Equal(1, changed);
        Assert.Equal(new Rgba32(10, 20, 30, 255), image[1, 1]);
        Assert.Equal(0, image[0, 0].A);
    }

    [Fact]
    public void ApplyInPlace_DoesNotTouchVisiblePixels()
    {
        using var image = new Image<Rgba32>(2, 1);
        image[0, 0] = new Rgba32(1, 2, 3, 1);
        image[1, 0] = new Rgba32(4, 5, 6, 255);

        var changed = RestorePencil.ApplyInPlace(image, 0, 0, 2, new Rgba32(200, 100, 50, 255));

        Assert.Equal(0, changed);
        Assert.Equal(new Rgba32(1, 2, 3, 1), image[0, 0]);
        Assert.Equal(new Rgba32(4, 5, 6, 255), image[1, 0]);
    }

    [Fact]
    public void ApplyInPlace_BrushOnePixelChangesOnlyTargetPixel()
    {
        using var image = new Image<Rgba32>(3, 3, new Rgba32(0, 0, 0, 0));

        var changed = RestorePencil.ApplyInPlace(image, 1, 1, 1, new Rgba32(255, 0, 0, 255));

        Assert.Equal(1, changed);
        Assert.Equal(255, image[1, 1].R);
        Assert.Equal(0, image[0, 1].A);
        Assert.Equal(0, image[1, 0].A);
        Assert.Equal(0, image[2, 1].A);
        Assert.Equal(0, image[1, 2].A);
    }

    [Fact]
    public void ApplyInPlace_ClipsBrushToImageBounds()
    {
        using var image = new Image<Rgba32>(2, 2, new Rgba32(0, 0, 0, 0));

        var changed = RestorePencil.ApplyInPlace(image, -1, -1, 3, new Rgba32(0, 255, 0, 255));

        Assert.Equal(4, changed);
        Assert.Equal(255, image[0, 0].A);
        Assert.Equal(255, image[1, 0].A);
        Assert.Equal(255, image[0, 1].A);
        Assert.Equal(255, image[1, 1].A);
    }
}

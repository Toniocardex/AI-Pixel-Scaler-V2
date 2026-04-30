using AiPixelScaler.Core.Pipeline.Editor;
using SixLabors.ImageSharp;
using SixLabors.ImageSharp.PixelFormats;
using Xunit;

namespace AiPixelScaler.Core.Tests;

public class FloatingPasteComposerTests
{
    [Fact]
    public void Commit_scaleOne_drawsPixel()
    {
        using var doc = new Image<Rgba32>(16, 16, new Rgba32(0, 0, 0, 0));
        using var paste = new Image<Rgba32>(2, 2);
        paste[0, 0] = new Rgba32(255, 0, 0, 255);
        paste[1, 0] = paste[0, 1] = paste[1, 1] = new Rgba32(0, 255, 0, 255);

        FloatingPasteComposer.Commit(doc, paste, 5, 6, 1.0);

        Assert.Equal(new Rgba32(255, 0, 0, 255), doc[5, 6]);
    }

    [Fact]
    public void Commit_fitScale_resizesWithNn()
    {
        using var doc = new Image<Rgba32>(8, 8, new Rgba32(0, 0, 0, 0));
        using var paste = new Image<Rgba32>(4, 4);
        for (var y = 0; y < 4; y++)
        for (var x = 0; x < 4; x++)
            paste[x, y] = new Rgba32((byte)(x * 40), (byte)(y * 40), 0, 255);

        var scale = FloatingPasteGeometry.ComputeUniformFitScale(4, 4, 2, 2);
        Assert.Equal(0.5, scale, precision: 10);
        FloatingPasteComposer.Commit(doc, paste, 1, 1, scale);

        var (dw, dh) = FloatingPasteGeometry.ComputeDisplayDimensions(4, 4, scale);
        Assert.Equal(2, dw);
        Assert.Equal(2, dh);
        Assert.NotEqual(new Rgba32(0, 0, 0, 0), doc[1, 1]);
    }
}

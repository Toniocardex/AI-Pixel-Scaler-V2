using AiPixelScaler.Core.Pipeline.Imaging;
using SixLabors.ImageSharp;
using SixLabors.ImageSharp.PixelFormats;

namespace AiPixelScaler.Core.Tests;

public class WebParity_PipelineTests
{
    [Fact]
    public void BackgroundIsolation_removes_exact_edge_connected_green()
    {
        using var img = new Image<Rgba32>(2, 1);
        img[0, 0] = new Rgba32(0, 255, 0, 255);
        img[1, 0] = new Rgba32(0, 0, 0, 255);
        BackgroundIsolation.ApplyInPlace(img, new(new Rgba32(0, 255, 0, 255), ColorTolerance: 0, ProtectStrongEdges: false, UseOklab: false));
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
    public void BackgroundIsolation_removes_edge_connected_magenta()
    {
        using var img = new Image<Rgba32>(3, 3);
        for (var y = 0; y < 3; y++)
        for (var x = 0; x < 3; x++)
            img[x, y] = new Rgba32(255, 0, 255, 255);
        img[1, 1] = new Rgba32(0, 0, 0, 255);
        BackgroundIsolation.ApplyInPlace(img, new(new Rgba32(255, 0, 255, 255), ColorTolerance: 0, ProtectStrongEdges: false, UseOklab: false));
        Assert.Equal(0, img[0, 0].A);
        Assert.Equal(255, img[1, 1].A);
    }

    [Fact]
    public void BackgroundIsolation_stops_at_sobel_edge()
    {
        using var img = new Image<Rgba32>(7, 7, new Rgba32(240, 240, 240, 255));
        for (var y = 2; y <= 4; y++)
        for (var x = 2; x <= 4; x++)
            img[x, y] = new Rgba32(20, 20, 20, 255);

        BackgroundIsolation.ApplyInPlace(img, new(new Rgba32(240, 240, 240, 255), ColorTolerance: 0, EdgeThreshold: 48, ProtectStrongEdges: true));

        Assert.Equal(0, img[0, 0].A);
        Assert.Equal(255, img[3, 3].A);
    }

    [Fact]
    public void BackgroundIsolation_edge_threshold_zero_disables_sobel_blocking()
    {
        using var img = new Image<Rgba32>(5, 5, new Rgba32(240, 240, 240, 255));

        var removed = BackgroundIsolation.ApplyInPlace(img, new(
            new Rgba32(240, 240, 240, 255),
            ColorTolerance: 0,
            EdgeThreshold: 0,
            ProtectStrongEdges: true,
            UseOklab: false));

        Assert.Equal(25, removed);
        Assert.Equal(0, img[2, 2].A);
    }

    [Fact]
    public void BackgroundIsolation_transparent_pixels_are_passable_for_second_run()
    {
        using var img = new Image<Rgba32>(5, 3, new Rgba32(30, 30, 30, 255));
        for (var y = 0; y < img.Height; y++)
            img[0, y] = new Rgba32(10, 10, 10, 255);
        for (var y = 0; y < img.Height; y++)
            img[1, y] = new Rgba32(20, 20, 20, 255);

        var first = BackgroundIsolation.ApplyInPlace(img, new(
            new Rgba32(10, 10, 10, 255),
            ColorTolerance: 0,
            ProtectStrongEdges: false,
            UseOklab: false));
        var second = BackgroundIsolation.ApplyInPlace(img, new(
            new Rgba32(20, 20, 20, 255),
            ColorTolerance: 0,
            ProtectStrongEdges: false,
            UseOklab: false));

        Assert.Equal(3, first);
        Assert.Equal(3, second);
        Assert.Equal(0, img[0, 1].A);
        Assert.Equal(0, img[1, 1].A);
        Assert.Equal(255, img[2, 1].A);
    }

    [Fact]
    public void BackgroundIsolation_snap_key_to_border_color_uses_quantized_border_color()
    {
        using var img = new Image<Rgba32>(3, 3, new Rgba32(190, 159, 135, 255));
        img[1, 1] = new Rgba32(20, 20, 20, 255);

        var snapped = BackgroundIsolation.SnapKeyToBorderColor(
            img,
            new Rgba32(193, 167, 144, 255),
            maxRgbDistancePerChannel: 40);

        Assert.Equal((byte)190, snapped.R);
        Assert.Equal((byte)159, snapped.G);
        Assert.Equal((byte)135, snapped.B);
    }

    [Fact]
    public void BackgroundIsolation_snap_key_to_border_color_skips_transparent_border_pixels()
    {
        using var img = new Image<Rgba32>(3, 3, new Rgba32(0, 0, 0, 0));
        img[1, 0] = new Rgba32(190, 159, 135, 255);
        var original = new Rgba32(193, 167, 144, 255);

        var snapped = BackgroundIsolation.SnapKeyToBorderColor(img, original, maxRgbDistancePerChannel: 40);

        Assert.Equal((byte)190, snapped.R);
        Assert.Equal((byte)159, snapped.G);
        Assert.Equal((byte)135, snapped.B);
    }

    [Fact]
    public void BackgroundIsolation_snap_key_to_border_color_negative_threshold_does_not_snap()
    {
        using var img = new Image<Rgba32>(3, 3, new Rgba32(190, 159, 135, 255));
        var original = new Rgba32(193, 167, 144, 255);

        var snapped = BackgroundIsolation.SnapKeyToBorderColor(img, original, maxRgbDistancePerChannel: -40);

        Assert.Equal(original.R, snapped.R);
        Assert.Equal(original.G, snapped.G);
        Assert.Equal(original.B, snapped.B);
    }

    [Fact]
    public void PixelArtPipeline_reports_background_removed_count()
    {
        using var img = new Image<Rgba32>(2, 1);
        img[0, 0] = new Rgba32(0, 255, 0, 255);
        img[1, 0] = new Rgba32(0, 0, 0, 255);

        var report = PixelArtPipeline.ApplyInPlace(img, new PixelArtPipeline.Options(
            EnableBackgroundIsolation: true,
            BackgroundColor: new Rgba32(0, 255, 0, 255),
            BackgroundTolerance: 0,
            EnableQuantize: false));

        Assert.Contains("removed 1", report.Steps[0]);
    }
}

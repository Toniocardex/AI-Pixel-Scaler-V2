using System;
using AiPixelScaler.Core.Pipeline.Imaging;
using SixLabors.ImageSharp;
using SixLabors.ImageSharp.PixelFormats;

namespace AiPixelScaler.Core.Tests;

public class PipelineHardening_Tests
{
    [Fact]
    public void ApplyInPlace_uses_default_green_background_when_color_is_default()
    {
        using var img = new Image<Rgba32>(1, 1);
        img[0, 0] = new Rgba32(0, 255, 0, 255);

        var report = PixelArtPipeline.ApplyInPlace(img, new PixelArtPipeline.Options(
            EnableBackgroundIsolation: true,
            BackgroundColor: default,
            BackgroundTolerance: 0,
            EnableQuantize: false));

        Assert.Equal(0, img[0, 0].A);
        Assert.Contains(report.Steps, s => s.StartsWith("background isolation", StringComparison.Ordinal));
    }

    [Fact]
    public void ApplyInPlace_clamps_quantize_maxcolors_lower_bound()
    {
        using var img = new Image<Rgba32>(2, 1);
        img[0, 0] = new Rgba32(255, 0, 0, 255);
        img[1, 0] = new Rgba32(0, 255, 0, 255);

        var report = PixelArtPipeline.ApplyInPlace(img, new PixelArtPipeline.Options(
            EnableBackgroundIsolation: false,
            EnableQuantize: true,
            MaxColors: 1));

        Assert.InRange(report.Palette.Count, 1, 2);
        Assert.Contains(report.Steps, s => s.StartsWith("quantize", StringComparison.Ordinal));
    }

    [Fact]
    public void ApplyInPlace_with_all_steps_disabled_keeps_pixels_and_reports_no_steps()
    {
        using var img = new Image<Rgba32>(2, 1);
        img[0, 0] = new Rgba32(10, 20, 30, 255);
        img[1, 0] = new Rgba32(40, 50, 60, 255);
        var before0 = img[0, 0];
        var before1 = img[1, 0];

        var report = PixelArtPipeline.ApplyInPlace(img, new PixelArtPipeline.Options(
            EnableBackgroundIsolation: false,
            EnableQuantize: false,
            EnableMajorityDenoise: false,
            EnableOutline: false,
            AlphaThreshold: null));

        Assert.Empty(report.Steps);
        Assert.Equal(before0, img[0, 0]);
        Assert.Equal(before1, img[1, 0]);
        Assert.Equal(report.UniqueColorsBefore, report.UniqueColorsAfter);
    }

}

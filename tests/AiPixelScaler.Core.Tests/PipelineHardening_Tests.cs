using System;
using AiPixelScaler.Core.Pipeline.Imaging;
using SixLabors.ImageSharp;
using SixLabors.ImageSharp.PixelFormats;

namespace AiPixelScaler.Core.Tests;

public class PipelineHardening_Tests
{
    [Fact]
    public void ApplyInPlace_uses_default_green_chroma_when_color_is_default()
    {
        using var img = new Image<Rgba32>(1, 1);
        img[0, 0] = new Rgba32(0, 255, 0, 255);

        var report = PixelArtPipeline.ApplyInPlace(img, new PixelArtPipeline.Options(
            EnableChroma: true,
            ChromaColor: default,
            ChromaTolerance: 0,
            EnableQuantize: false));

        Assert.Equal(0, img[0, 0].A);
        Assert.Contains(report.Steps, s => s.StartsWith("chroma key", StringComparison.Ordinal));
    }

    [Fact]
    public void ApplyInPlace_clamps_recover_iterations_to_at_least_one()
    {
        using var img = new Image<Rgba32>(5, 5);
        for (var y = 0; y < 5; y++)
        for (var x = 0; x < 5; x++)
            img[x, y] = new Rgba32(40, 80, 120, 255);
        img[2, 2] = new Rgba32(40, 80, 120, 0);

        var report = PixelArtPipeline.ApplyInPlace(img, new PixelArtPipeline.Options(
            EnableChroma: false,
            EnableQuantize: false,
            EnableRecoverFill: true,
            RecoverIterations: 0));

        Assert.True(report.RecoveredPixels >= 1);
        Assert.Equal(255, img[2, 2].A);
    }

    [Fact]
    public void ApplyInPlace_clamps_quantize_maxcolors_lower_bound()
    {
        using var img = new Image<Rgba32>(2, 1);
        img[0, 0] = new Rgba32(255, 0, 0, 255);
        img[1, 0] = new Rgba32(0, 255, 0, 255);

        var report = PixelArtPipeline.ApplyInPlace(img, new PixelArtPipeline.Options(
            EnableChroma: false,
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
            EnableChroma: false,
            EnableQuantize: false,
            EnableMajorityDenoise: false,
            EnableOutline: false,
            AlphaThreshold: null,
            EnableRecoverFill: false));

        Assert.Empty(report.Steps);
        Assert.Equal(before0, img[0, 0]);
        Assert.Equal(before1, img[1, 0]);
        Assert.Equal(report.UniqueColorsBefore, report.UniqueColorsAfter);
    }

    [Fact]
    public void ApplyInPlace_advanced_cleaner_pixel_grid_enforce_preserves_image_size()
    {
        using var img = new Image<Rgba32>(16, 16);
        for (var y = 0; y < img.Height; y++)
        for (var x = 0; x < img.Width; x++)
            img[x, y] = ((x + y) % 2 == 0) ? new Rgba32(220, 60, 60, 255) : new Rgba32(60, 60, 220, 255);

        var report = PixelArtPipeline.ApplyInPlace(img, new PixelArtPipeline.Options(
            EnableChroma: false,
            EnableQuantize: true,
            MaxColors: 16,
            EnableAdvancedCleaner: true,
            EnablePixelGridEnforce: true,
            NativeWidth: 8,
            NativeHeight: 8));

        Assert.Equal(16, img.Width);
        Assert.Equal(16, img.Height);
        Assert.Contains(report.Steps, s => s.StartsWith("advanced pixel-grid", StringComparison.Ordinal));
    }

    [Fact]
    public void ApplyInPlace_advanced_cleaner_reports_palette_when_outer_quantize_disabled()
    {
        using var img = new Image<Rgba32>(4, 1);
        img[0, 0] = new Rgba32(220, 60, 60, 255);
        img[1, 0] = new Rgba32(215, 58, 58, 255);
        img[2, 0] = new Rgba32(60, 60, 220, 255);
        img[3, 0] = new Rgba32(58, 58, 215, 255);

        var report = PixelArtPipeline.ApplyInPlace(img, new PixelArtPipeline.Options(
            EnableChroma: false,
            EnableQuantize: false,
            EnableAdvancedCleaner: true,
            MaxColors: 2,
            BilateralPasses: 1));

        Assert.NotEmpty(report.Palette);
        Assert.Contains(report.Steps, s => s.StartsWith("advanced quantize", StringComparison.Ordinal));
    }

    [Fact]
    public void ApplyInPlace_advanced_cleaner_does_not_run_outer_quantize_again()
    {
        using var img = new Image<Rgba32>(4, 1);
        img[0, 0] = new Rgba32(220, 60, 60, 255);
        img[1, 0] = new Rgba32(215, 58, 58, 255);
        img[2, 0] = new Rgba32(60, 60, 220, 255);
        img[3, 0] = new Rgba32(58, 58, 215, 255);

        var report = PixelArtPipeline.ApplyInPlace(img, new PixelArtPipeline.Options(
            EnableChroma: false,
            EnableQuantize: true,
            EnableAdvancedCleaner: true,
            MaxColors: 2,
            BilateralPasses: 1));

        Assert.Single(report.Steps.Where(s => s.Contains("quantize", StringComparison.Ordinal)));
        Assert.Contains(report.Steps, s => s.StartsWith("advanced quantize", StringComparison.Ordinal));
    }

    [Fact]
    public void ApplyInPlace_palette_snap_uses_palette_id_when_enabled()
    {
        using var img = new Image<Rgba32>(1, 1);
        img[0, 0] = new Rgba32(10, 200, 10, 255);

        _ = PixelArtPipeline.ApplyInPlace(img, new PixelArtPipeline.Options(
            EnableChroma: false,
            EnableQuantize: false,
            EnableAdvancedCleaner: false,
            EnablePaletteSnap: true,
            PaletteId: "gameboydmg"));

        var allowed = PalettePresets.Get(PalettePresets.Preset.GameBoyDMG);
        Assert.Contains(allowed, c => c.R == img[0, 0].R && c.G == img[0, 0].G && c.B == img[0, 0].B);
    }
}

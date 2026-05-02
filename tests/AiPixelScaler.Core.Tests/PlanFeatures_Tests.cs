using AiPixelScaler.Core.Pipeline.Export;
using AiPixelScaler.Core.Pipeline.Imaging;
using SixLabors.ImageSharp;
using SixLabors.ImageSharp.Formats.Png;
using SixLabors.ImageSharp.PixelFormats;

namespace AiPixelScaler.Core.Tests;

public class PlanFeatures_Tests
{
    [Fact]
    public void PixelArtProcessor_ProcessImageInPlace_runs_core_pipeline()
    {
        using var img = new Image<Rgba32>(3, 1);
        img[0, 0] = new Rgba32(0, 255, 0, 255);
        img[1, 0] = new Rgba32(10, 20, 30, 255);
        img[2, 0] = new Rgba32(12, 22, 32, 255);

        var result = PixelArtProcessor.ProcessImageInPlace(img, new PixelArtProcessor.Options(
            NormalizeBackground: true,
            BackgroundSnapRgb: false,
            BackgroundColor: new Rgba32(0, 255, 0, 255),
            BackgroundTolerance: 0,
            QuantizePalette: true,
            MaxColors: 2,
            Quantizer: PixelArtProcessor.QuantizerKind.Wu));

        Assert.True(result.UniqueColorsBefore >= result.UniqueColorsAfter);
        Assert.InRange(result.UniqueColorsAfter, 1, 2);
        Assert.Equal(0, img[0, 0].A);
    }

    [Fact]
    public void PixelArtProcessor_ProcessFile_exports_indexed_png()
    {
        var tempDir = Path.Combine(Path.GetTempPath(), "aipixel-tests", Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(tempDir);
        try
        {
            var input = Path.Combine(tempDir, "in.png");
            var output = Path.Combine(tempDir, "out.png");
            using (var img = new Image<Rgba32>(4, 4))
            {
                for (var y = 0; y < 4; y++)
                for (var x = 0; x < 4; x++)
                    img[x, y] = new Rgba32((byte)(x * 30), (byte)(y * 30), 120, 255);
                img.Save(input, new PngEncoder());
            }

            var result = PixelArtProcessor.ProcessFile(input, output, new PixelArtProcessor.Options(
                NormalizeBackground: false,
                QuantizePalette: true,
                MaxColors: 4,
                Export: PixelArtProcessor.ExportKind.IndexedPng8));

            Assert.True(File.Exists(output));
            Assert.InRange(result.UniqueColorsAfter, 1, 4);
            var info = Image.Identify(output);
            Assert.Equal(PngColorType.Palette, info.Metadata.GetPngMetadata().ColorType);
        }
        finally
        {
            if (Directory.Exists(tempDir))
                Directory.Delete(tempDir, recursive: true);
        }
    }

    [Fact]
    public void BackgroundIsolation_SnapBackgroundRgbInPlace_sets_exact_key_rgb()
    {
        using var img = new Image<Rgba32>(1, 1);
        img[0, 0] = new Rgba32(0, 250, 5, 200);
        var key = new Rgba32(0, 255, 0, 255);
        BackgroundIsolation.SnapBackgroundRgbInPlace(img, key, tolerance: 30);
        Assert.Equal(key.R, img[0, 0].R);
        Assert.Equal(key.G, img[0, 0].G);
        Assert.Equal(key.B, img[0, 0].B);
        Assert.Equal(200, img[0, 0].A);
    }

    [Fact]
    public void PixelArtValidation_CountUniqueColors_ignores_transparent_by_default()
    {
        using var img = new Image<Rgba32>(2, 1);
        img[0, 0] = new Rgba32(10, 20, 30, 0);
        img[1, 0] = new Rgba32(10, 20, 30, 255);
        Assert.Equal(1, PixelArtValidation.CountUniqueColors(img, ignoreFullyTransparent: true));
        Assert.Equal(2, PixelArtValidation.CountUniqueColors(img, ignoreFullyTransparent: false));
    }

    [Fact]
    public void PixelArtValidation_Validate_flags_power_of_two()
    {
        using var img = new Image<Rgba32>(3, 4);
        img[0, 0] = new Rgba32(1, 2, 3, 255);
        var r = PixelArtValidation.Validate(img, new PixelArtValidation.Options(RequirePowerOfTwoDimensions: true));
        Assert.False(r.IsValid);
        Assert.Contains("potenza", string.Join(" ", r.Issues), StringComparison.OrdinalIgnoreCase);
        Assert.Contains(r.StructuredIssues, x => x.Code == "WIDTH_NOT_POW2");
    }

    [Fact]
    public void PixelArtValidation_Validate_flags_BACKGROUND_MISMATCH()
    {
        using var img = new Image<Rgba32>(2, 1);
        img[0, 0] = new Rgba32(0, 255, 0, 255);
        img[1, 0] = new Rgba32(255, 0, 0, 255);
        var r = PixelArtValidation.Validate(img, new PixelArtValidation.Options(
            ExpectedBackgroundColor: new Rgba32(0, 255, 0, 255),
            MaxBackgroundMismatchRatio: 0.2,
            BackgroundTolerance: 0));
        Assert.False(r.IsValid);
        Assert.Contains(r.StructuredIssues, x => x.Code == "BACKGROUND_MISMATCH");
    }

    [Fact]
    public void MajorityNeighborDenoise_replaces_lone_pixel()
    {
        using var img = new Image<Rgba32>(3, 3);
        for (var y = 0; y < 3; y++)
        for (var x = 0; x < 3; x++)
            img[x, y] = new Rgba32(10, 10, 10, 255);
        img[1, 1] = new Rgba32(99, 99, 99, 255);
        MajorityNeighborDenoise.ApplyInPlace(img, minSameNeighbors: 2);
        Assert.Equal(10, img[1, 1].R);
    }

    [Fact]
    public void PaletteExtractorAlgorithms_Wu_returns_limited_colors()
    {
        using var img = new Image<Rgba32>(4, 4);
        for (var y = 0; y < 4; y++)
        for (var x = 0; x < 4; x++)
            img[x, y] = new Rgba32((byte)(x * 40), (byte)(y * 40), 128, 255);
        var pal = PaletteExtractorAlgorithms.ExtractWu(img, maxColors: 4);
        Assert.InRange(pal.Count, 1, 4);
    }

    [Fact]
    public void IndexedPngExporter_locked_palette_roundtrip()
    {
        var palette = new[]
        {
            new Rgba32(255, 0, 0, 255),
            new Rgba32(0, 255, 0, 255),
        };
        using var img = new Image<Rgba32>(2, 1);
        img[0, 0] = palette[0];
        img[1, 0] = palette[1];
        using var ms = new MemoryStream();
        IndexedPngExporter.SaveWithLockedPalette(img, ms, palette);
        ms.Position = 0;
        using var back = Image.Load<Rgba32>(ms);
        Assert.Equal(2, back.Width);
        Assert.Equal(1, back.Height);
        Assert.Equal(palette[0].R, back[0, 0].R);
        Assert.Equal(palette[1].G, back[1, 0].G);
    }

    [Fact]
    public void IndexedPngExporter_wu_produces_palette_png()
    {
        using var img = new Image<Rgba32>(8, 8);
        for (var y = 0; y < 8; y++)
        for (var x = 0; x < 8; x++)
            img[x, y] = new Rgba32((byte)x, (byte)y, 50, 255);
        using var ms = new MemoryStream();
        IndexedPngExporter.SaveWithWuQuantize(img, ms);
        ms.Position = 0;
        var info = Image.Identify(ms);
        Assert.Equal(PngColorType.Palette, info.Metadata.GetPngMetadata().ColorType);
    }

    [Fact]
    public void SharedPaletteBuilder_BuildFromFiles_returns_limited_palette()
    {
        var tempDir = Path.Combine(Path.GetTempPath(), "aipixel-tests", Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(tempDir);
        try
        {
            var a = Path.Combine(tempDir, "a.png");
            var b = Path.Combine(tempDir, "b.png");
            using (var img = new Image<Rgba32>(2, 1))
            {
                img[0, 0] = new Rgba32(255, 0, 0, 255);
                img[1, 0] = new Rgba32(0, 255, 0, 255);
                img.Save(a, new PngEncoder());
            }
            using (var img = new Image<Rgba32>(2, 1))
            {
                img[0, 0] = new Rgba32(0, 0, 255, 255);
                img[1, 0] = new Rgba32(255, 255, 0, 255);
                img.Save(b, new PngEncoder());
            }

            var palette = SharedPaletteBuilder.BuildFromFiles([a, b], maxColors: 3);
            Assert.InRange(palette.Count, 1, 3);
        }
        finally
        {
            if (Directory.Exists(tempDir))
                Directory.Delete(tempDir, recursive: true);
        }
    }

}

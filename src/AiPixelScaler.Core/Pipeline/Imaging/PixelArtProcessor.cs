using AiPixelScaler.Core.Pipeline.Export;
using SixLabors.ImageSharp;
using SixLabors.ImageSharp.Formats.Png;
using SixLabors.ImageSharp.PixelFormats;

namespace AiPixelScaler.Core.Pipeline.Imaging;

public static class PixelArtProcessor
{
    public enum QuantizerKind { Wu, Octree }
    public enum ExportKind { RgbaPng, IndexedPng8 }

    public sealed record Options(
        bool NormalizeBackground = true,
        bool BackgroundSnapRgb = false,
        Rgba32 BackgroundColor = default,
        double BackgroundTolerance = 0,
        bool QuantizePalette = true,
        int MaxColors = 32,
        QuantizerKind Quantizer = QuantizerKind.Wu,
        ExportKind Export = ExportKind.RgbaPng);

    public sealed record Result(
        int UniqueColorsBefore,
        int UniqueColorsAfter,
        IReadOnlyList<Rgba32> Palette,
        string Summary);

    public static Result ProcessImageInPlace(Image<Rgba32> image, Options options)
    {
        var report = PixelArtPipeline.ApplyInPlace(image, new PixelArtPipeline.Options(
            EnableBackgroundIsolation: options.NormalizeBackground,
            BackgroundSnapRgb: options.BackgroundSnapRgb,
            BackgroundColor: options.BackgroundColor,
            BackgroundTolerance: options.BackgroundTolerance,
            EnableQuantize: options.QuantizePalette,
            MaxColors: options.MaxColors,
            Quantizer: options.Quantizer));

        return new Result(
            report.UniqueColorsBefore,
            report.UniqueColorsAfter,
            report.Palette,
            string.Join(" -> ", report.Steps));
    }

    public static Result ProcessFile(string inputPath, string outputPath, Options options)
    {
        using var image = Image.Load<Rgba32>(inputPath);
        var result = ProcessImageInPlace(image, options);
        Save(image, outputPath, options.Export, result.Palette);
        return result;
    }

    public static void Save(Image<Rgba32> image, string outputPath, ExportKind export, IReadOnlyList<Rgba32>? palette = null)
    {
        using var fs = File.Create(outputPath);
        if (export == ExportKind.IndexedPng8)
        {
            if (palette is { Count: > 0 })
                IndexedPngExporter.SaveWithLockedPalette(image, fs, palette);
            else
                IndexedPngExporter.SaveWithWuQuantize(image, fs);
            return;
        }

        image.Save(fs, new PngEncoder());
    }
}

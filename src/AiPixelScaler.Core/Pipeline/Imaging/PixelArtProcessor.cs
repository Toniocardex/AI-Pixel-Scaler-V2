using AiPixelScaler.Core.Pipeline.Export;
using SixLabors.ImageSharp;
using SixLabors.ImageSharp.Formats.Png;
using SixLabors.ImageSharp.PixelFormats;

namespace AiPixelScaler.Core.Pipeline.Imaging;

public static class PixelArtProcessor
{
    public enum QuantizerKind { KMeansOklab, Wu, Octree }
    public enum ExportKind { RgbaPng, IndexedPng8 }

    public sealed record Options(
        bool NormalizeChroma = true,
        bool ChromaSnapRgb = false,
        Rgba32 ChromaColor = default,
        double ChromaTolerance = 0,
        bool QuantizePalette = true,
        int MaxColors = 32,
        QuantizerKind Quantizer = QuantizerKind.KMeansOklab,
        ExportKind Export = ExportKind.RgbaPng);

    public sealed record Result(
        int UniqueColorsBefore,
        int UniqueColorsAfter,
        IReadOnlyList<Rgba32> Palette,
        string Summary);

    public static Result ProcessImageInPlace(Image<Rgba32> image, Options options)
    {
        var report = PixelArtPipeline.ApplyInPlace(image, new PixelArtPipeline.Options(
            EnableChroma: options.NormalizeChroma,
            ChromaSnapRgb: options.ChromaSnapRgb,
            ChromaColor: options.ChromaColor,
            ChromaTolerance: options.ChromaTolerance,
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

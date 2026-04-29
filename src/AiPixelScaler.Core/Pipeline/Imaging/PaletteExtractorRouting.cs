using SixLabors.ImageSharp;
using SixLabors.ImageSharp.PixelFormats;

namespace AiPixelScaler.Core.Pipeline.Imaging;

/// <summary>
/// Routing centralizzato per estrazione palette (KMeans OKLab vs Wu vs Octree).
/// Evita duplicazioni tra PixelArtPipeline, PixelArtProcessor e AdvancedPixelCleaner.
/// </summary>
internal static class PaletteExtractorRouting
{
    public static IReadOnlyList<Rgba32> Extract(Image<Rgba32> image, PixelArtProcessor.QuantizerKind quantizer, int maxColors)
    {
        var n = Math.Clamp(maxColors, 2, 256);
        return quantizer switch
        {
            PixelArtProcessor.QuantizerKind.Wu => PaletteExtractorAlgorithms.ExtractWu(image, n),
            PixelArtProcessor.QuantizerKind.Octree => PaletteExtractorAlgorithms.ExtractOctree(image, n),
            _ => PaletteExtractor.Extract(image, new PaletteExtractor.Options(Colors: n)),
        };
    }
}

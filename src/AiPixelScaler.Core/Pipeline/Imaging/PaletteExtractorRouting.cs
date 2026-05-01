using SixLabors.ImageSharp;
using SixLabors.ImageSharp.PixelFormats;

namespace AiPixelScaler.Core.Pipeline.Imaging;

/// <summary>
/// Routing centralizzato per estrazione palette tramite quantizzatori ImageSharp.
/// Evita duplicazioni tra PixelArtPipeline, PixelArtProcessor e AdvancedPixelCleaner.
/// </summary>
internal static class PaletteExtractorRouting
{
    public static IReadOnlyList<Rgba32> Extract(Image<Rgba32> image, PixelArtProcessor.QuantizerKind quantizer, int maxColors)
    {
        var n = Math.Clamp(maxColors, 2, 256);
        return quantizer switch
        {
            PixelArtProcessor.QuantizerKind.Octree => PaletteExtractorAlgorithms.ExtractOctree(image, n),
            _ => PaletteExtractorAlgorithms.ExtractWu(image, n),
        };
    }
}

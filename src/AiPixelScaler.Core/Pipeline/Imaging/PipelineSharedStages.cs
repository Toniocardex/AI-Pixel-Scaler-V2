using AiPixelScaler.Core.Geometry;
using SixLabors.ImageSharp;
using SixLabors.ImageSharp.PixelFormats;

namespace AiPixelScaler.Core.Pipeline.Imaging;

internal static class PipelineSharedStages
{
    public static void ApplyAlphaThreshold(Image<Rgba32> image, byte threshold)
        => AlphaThreshold.ApplyInPlace(image, threshold);

    public static void ApplyIslandDenoise(Image<Rgba32> image, int minIslandArea, PixelConnectivity connectivity = PixelConnectivity.Eight)
        => IslandDenoise.ApplyInPlace(
            image,
            new IslandDenoise.Options(
                alphaThreshold: 1,
                minIslandArea: Math.Max(1, minIslandArea),
                connectivity: connectivity));

    public static IReadOnlyList<Rgba32> ApplyPaletteReduction(
        Image<Rgba32> image,
        int maxColors,
        PaletteMapper.DitherMode dither,
        PixelArtProcessor.QuantizerKind quantizer)
    {
        var palette = PaletteExtractorRouting.Extract(image, quantizer, maxColors);
        if (palette.Count > 0)
            PaletteMapper.ApplyInPlace(image, palette, dither);
        return palette;
    }
}

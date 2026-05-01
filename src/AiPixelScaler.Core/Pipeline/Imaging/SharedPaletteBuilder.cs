using SixLabors.ImageSharp;
using SixLabors.ImageSharp.PixelFormats;

namespace AiPixelScaler.Core.Pipeline.Imaging;

public static class SharedPaletteBuilder
{
    public static IReadOnlyList<Rgba32> BuildFromFiles(
        IReadOnlyList<string> files,
        int maxColors,
        Rgba32? chromaSnapKey = null,
        double chromaSnapTolerance = 0)
    {
        var union = new HashSet<Rgba32>();
        foreach (var path in files)
        {
            using var img = Image.Load<Rgba32>(path);
            if (chromaSnapKey.HasValue)
                ChromaKey.SnapKeyRgbInPlace(img, chromaSnapKey.Value, Math.Max(0, chromaSnapTolerance));
            foreach (var c in PaletteExtractorAlgorithms.ExtractWu(img, Math.Clamp(maxColors, 2, 256)))
                union.Add(c);
        }

        if (union.Count == 0) return [];
        using var synthetic = new Image<Rgba32>(union.Count, 1);
        var i = 0;
        foreach (var c in union)
            synthetic[i++, 0] = c;
        return PaletteExtractorAlgorithms.ExtractWu(synthetic, Math.Clamp(maxColors, 2, 256));
    }
}

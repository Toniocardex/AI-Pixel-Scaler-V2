using SixLabors.ImageSharp;
using SixLabors.ImageSharp.PixelFormats;

namespace AiPixelScaler.Core.Pipeline.Imaging;

public static class SharedPaletteBuilder
{
    public static IReadOnlyList<Rgba32> BuildFromFiles(
        IReadOnlyList<string> files,
        int maxColors,
        Rgba32? backgroundSnapKey = null,
        double backgroundSnapTolerance = 0)
    {
        var union = new HashSet<Rgba32>();
        foreach (var path in files)
        {
            using var img = Image.Load<Rgba32>(path);
            if (backgroundSnapKey.HasValue)
                BackgroundIsolation.SnapBackgroundRgbInPlace(img, backgroundSnapKey.Value, Math.Max(0, backgroundSnapTolerance));
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

using SixLabors.ImageSharp;
using SixLabors.ImageSharp.PixelFormats;
using SixLabors.ImageSharp.Processing;
using SixLabors.ImageSharp.Processing.Processors.Quantization;

namespace AiPixelScaler.Core.Pipeline.Imaging;

/// <summary>
/// Estrazione palette tramite quantizzatori ImageSharp (Wu, Octree), alternativi al K-Means OKLab.
/// </summary>
public static class PaletteExtractorAlgorithms
{
    public static IReadOnlyList<Rgba32> ExtractWu(Image<Rgba32> image, int maxColors)
        => ExtractWithQuantizer(image, new WuQuantizer(MakeOptions(maxColors)));

    public static IReadOnlyList<Rgba32> ExtractOctree(Image<Rgba32> image, int maxColors)
        => ExtractWithQuantizer(image, new OctreeQuantizer(MakeOptions(maxColors)));

    private static QuantizerOptions MakeOptions(int maxColors)
    {
        var k = Math.Clamp(maxColors, 2, 256);
        return new QuantizerOptions { MaxColors = k, Dither = null };
    }

    private static IReadOnlyList<Rgba32> ExtractWithQuantizer(Image<Rgba32> image, IQuantizer quantizer)
    {
        using var work = image.Clone(ctx => ctx.Quantize(quantizer));
        var set = new HashSet<Rgba32>();
        work.ProcessPixelRows(accessor =>
        {
            for (var y = 0; y < accessor.Height; y++)
            {
                var row = accessor.GetRowSpan(y);
                foreach (ref readonly var p in row)
                {
                    if (p.A == 0) continue;
                    set.Add(p);
                }
            }
        });

        return set
            .OrderByDescending(c => 0.299 * c.R + 0.587 * c.G + 0.114 * c.B)
            .ToList();
    }
}

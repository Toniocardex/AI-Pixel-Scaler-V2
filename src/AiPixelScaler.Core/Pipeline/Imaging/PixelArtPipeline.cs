using SixLabors.ImageSharp;
using SixLabors.ImageSharp.PixelFormats;

namespace AiPixelScaler.Core.Pipeline.Imaging;

public static class PixelArtPipeline
{
    public sealed record Options(
        bool EnableChroma = true,
        bool ChromaSnapRgb = false,
        Rgba32 ChromaColor = default,
        double ChromaTolerance = 0,
        bool EnableQuantize = true,
        int MaxColors = 16,
        PixelArtProcessor.QuantizerKind Quantizer = PixelArtProcessor.QuantizerKind.KMeansOklab,
        bool EnableMajorityDenoise = false,
        int MajorityMinSameNeighbors = 2,
        int? IslandMinArea = null,
        bool EnableOutline = false,
        Rgba32 OutlineColor = default,
        byte? AlphaThreshold = null,
        bool EnableRecoverFill = false,
        int RecoverIterations = 2,
        bool RequantizeAfterRecover = true);

    public sealed record Report(
        int UniqueColorsBefore,
        int UniqueColorsAfter,
        IReadOnlyList<Rgba32> Palette,
        int RecoveredPixels,
        IReadOnlyList<string> Steps);

    public static Report ApplyInPlace(Image<Rgba32> image, Options options)
    {
        var before = PixelArtValidation.CountUniqueColors(image);
        var steps = new List<string>();
        var key = options.ChromaColor.Equals(default(Rgba32)) ? new Rgba32(0, 255, 0, 255) : options.ChromaColor;

        if (options.EnableChroma)
        {
            var tol = Math.Max(0, options.ChromaTolerance);
            if (options.ChromaSnapRgb)
            {
                ChromaKey.SnapKeyRgbInPlace(image, key, tol);
                steps.Add($"chroma snap (tol {tol:0.##})");
            }
            else
            {
                ChromaKey.ApplyInPlace(image, key, tol);
                steps.Add($"chroma key (tol {tol:0.##})");
            }
        }

        IReadOnlyList<Rgba32> palette = [];
        if (options.EnableQuantize)
        {
            palette = ExtractPalette(image, options.Quantizer, options.MaxColors);
            if (palette.Count > 0)
            {
                PaletteMapper.ApplyInPlace(image, palette, PaletteMapper.DitherMode.None);
                steps.Add($"quantize {options.Quantizer} ({palette.Count})");
            }
        }

        if (options.EnableMajorityDenoise)
        {
            MajorityNeighborDenoise.ApplyInPlace(image, Math.Max(1, options.MajorityMinSameNeighbors));
            steps.Add("majority denoise");
        }

        if (options.IslandMinArea is { } minIsland && minIsland > 0)
        {
            IslandDenoise.ApplyInPlace(image, new IslandDenoise.Options(alphaThreshold: 1, minIslandArea: minIsland));
            steps.Add($"island denoise (min {minIsland})");
        }

        if (options.EnableOutline)
        {
            var line = options.OutlineColor.Equals(default(Rgba32)) ? new Rgba32(0, 0, 0, 255) : options.OutlineColor;
            Outline1Px.ApplyOuterInPlace(image, line);
            steps.Add("outline 1px");
        }

        if (options.AlphaThreshold is { } thr)
        {
            AlphaThreshold.ApplyInPlace(image, thr);
            steps.Add($"alpha {thr}");
        }

        var recovered = 0;
        if (options.EnableRecoverFill)
        {
            recovered = MissingPixelFill.FillInternalTransparentInPlace(image, Math.Max(1, options.RecoverIterations));
            steps.Add($"recover +{recovered}px");

            if (options.RequantizeAfterRecover && options.EnableQuantize)
            {
                var pal2 = ExtractPalette(image, options.Quantizer, options.MaxColors);
                if (pal2.Count > 0)
                {
                    PaletteMapper.ApplyInPlace(image, pal2, PaletteMapper.DitherMode.None);
                    palette = pal2;
                    steps.Add($"requantize ({pal2.Count})");
                }
            }
        }

        var after = PixelArtValidation.CountUniqueColors(image);
        return new Report(before, after, palette, recovered, steps);
    }

    private static IReadOnlyList<Rgba32> ExtractPalette(Image<Rgba32> image, PixelArtProcessor.QuantizerKind quantizer, int maxColors)
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

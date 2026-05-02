using SixLabors.ImageSharp;
using SixLabors.ImageSharp.PixelFormats;

namespace AiPixelScaler.Core.Pipeline.Imaging;

public static class PixelArtPipeline
{
    public sealed record Options(
        bool EnableBackgroundIsolation = true,
        bool BackgroundSnapRgb = false,
        Rgba32 BackgroundColor = default,
        double BackgroundTolerance = 0,
        bool EnableQuantize = true,
        int MaxColors = 16,
        PixelArtProcessor.QuantizerKind Quantizer = PixelArtProcessor.QuantizerKind.Wu,
        bool EnableMajorityDenoise = false,
        int MajorityMinSameNeighbors = 2,
        int? IslandMinArea = null,
        bool EnableOutline = false,
        Rgba32 OutlineColor = default,
        byte? AlphaThreshold = null);

    public sealed record Report(
        int UniqueColorsBefore,
        int UniqueColorsAfter,
        IReadOnlyList<Rgba32> Palette,
        IReadOnlyList<string> Steps);

    public static Report ApplyInPlace(Image<Rgba32> image, Options options)
    {
        var before = PixelArtValidation.CountUniqueColors(image);
        var steps = new List<string>();
        var key = options.BackgroundColor.Equals(default(Rgba32)) ? new Rgba32(0, 255, 0, 255) : options.BackgroundColor;

        if (options.EnableBackgroundIsolation)
        {
            var tol = Math.Max(0, options.BackgroundTolerance);
            if (options.BackgroundSnapRgb)
            {
                BackgroundIsolation.SnapBackgroundRgbInPlace(image, key, tol);
                steps.Add($"background snap (tol {tol:0.##})");
            }
            else
            {
                BackgroundIsolation.ApplyInPlace(
                    image,
                    new BackgroundIsolation.Options(
                        BackgroundColor: key,
                        ColorTolerance: tol,
                        EdgeThreshold: tol <= 0 ? 48 : Math.Clamp(tol * 6, 8, 255),
                        UseOklab: true,
                        ProtectStrongEdges: true));
                steps.Add($"background isolation (tol {tol:0.##})");
            }
        }

        IReadOnlyList<Rgba32> palette = [];

        if (options.EnableQuantize && palette.Count == 0)
        {
            palette = PipelineSharedStages.ApplyPaletteReduction(image, options.MaxColors, PaletteMapper.DitherMode.None, options.Quantizer);
            if (palette.Count > 0)
                steps.Add($"quantize {options.Quantizer} ({palette.Count})");
        }

        if (options.EnableMajorityDenoise)
        {
            MajorityNeighborDenoise.ApplyInPlace(image, Math.Max(1, options.MajorityMinSameNeighbors));
            steps.Add("majority denoise");
        }

        if (options.IslandMinArea is { } minIsland && minIsland > 0)
        {
            PipelineSharedStages.ApplyIslandDenoise(image, minIsland);
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
            PipelineSharedStages.ApplyAlphaThreshold(image, thr);
            steps.Add($"alpha {thr}");
        }

        var after = PixelArtValidation.CountUniqueColors(image);
        return new Report(before, after, palette, steps);
    }

}

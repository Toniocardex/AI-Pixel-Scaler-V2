using AiPixelScaler.Core.Color;
using SixLabors.ImageSharp;
using SixLabors.ImageSharp.PixelFormats;

namespace AiPixelScaler.Core.Pipeline.Imaging;

public static class AdvancedPixelCleaner
{
    public sealed record Options(
        double SigmaSpatial = 1.25,
        double SigmaRange = 0.085,
        int BilateralPasses = 1,
        bool EnablePixelGridEnforce = false,
        int NativeWidth = 64,
        int NativeHeight = 64,
        bool EnablePaletteSnap = false,
        string? PaletteId = null);

    public static IReadOnlyList<Rgba32> ApplyInPlace(
        Image<Rgba32> image,
        int maxColors,
        PixelArtProcessor.QuantizerKind quantizer,
        Options options,
        List<string>? steps = null)
    {
        var safePasses = Math.Clamp(options.BilateralPasses, 1, 3);
        var safeSigmaSpatial = Math.Clamp(options.SigmaSpatial, 0.5, 6.0);
        var safeSigmaRange = Math.Clamp(options.SigmaRange, 0.01, 0.35);

        for (var i = 0; i < safePasses; i++)
            ApplyBilateralPassInPlace(image, safeSigmaSpatial, safeSigmaRange);
        steps?.Add($"advanced bilateral x{safePasses}");

        if (options.EnablePixelGridEnforce)
        {
            var w = Math.Max(1, options.NativeWidth);
            var h = Math.Max(1, options.NativeHeight);
            using var down = NearestNeighborResize.Resize(image, w, h);
            using var up = NearestNeighborResize.Resize(down, image.Width, image.Height);
            for (var y = 0; y < image.Height; y++)
            {
                for (var x = 0; x < image.Width; x++)
                    image[x, y] = up[x, y];
            }
            steps?.Add($"advanced pixel-grid {w}x{h}");
        }

        var palette = PaletteExtractorRouting.Extract(image, quantizer, maxColors);
        if (palette.Count > 0)
        {
            PaletteMapper.ApplyInPlace(image, palette, PaletteMapper.DitherMode.None);
            steps?.Add($"advanced quantize {quantizer} ({palette.Count})");
        }

        if (options.EnablePaletteSnap && PaletteIdResolver.TryResolve(options.PaletteId, out var lockedPalette))
        {
            PaletteMapper.ApplyInPlace(image, lockedPalette, PaletteMapper.DitherMode.None);
            steps?.Add($"palette snap {options.PaletteId}");
            return lockedPalette;
        }

        return palette;
    }

    private static void ApplyBilateralPassInPlace(Image<Rgba32> image, double sigmaSpatial, double sigmaRange)
    {
        var source = image.Clone();
        var radius = Math.Max(1, (int)Math.Ceiling(sigmaSpatial * 2.0));
        var spatialLut = BuildSpatialKernel(radius, sigmaSpatial);
        var invRangeSigmaSq = 1.0 / (2.0 * sigmaRange * sigmaRange);

        for (var y = 0; y < image.Height; y++)
        {
            for (var x = 0; x < image.Width; x++)
            {
                var center = source[x, y];
                if (center.A == 0)
                {
                    image[x, y] = center;
                    continue;
                }

                var centerLab = Oklab.FromSrgb(center);
                double sumW = 0;
                double accR = 0;
                double accG = 0;
                double accB = 0;

                for (var oy = -radius; oy <= radius; oy++)
                {
                    var yy = Math.Clamp(y + oy, 0, image.Height - 1);
                    for (var ox = -radius; ox <= radius; ox++)
                    {
                        var xx = Math.Clamp(x + ox, 0, image.Width - 1);
                        var p = source[xx, yy];
                        if (p.A == 0)
                            continue;

                        var spatial = spatialLut[oy + radius, ox + radius];
                        var lab = Oklab.FromSrgb(p);
                        var distSq = Oklab.DistanceSquared(centerLab, lab);
                        var range = Math.Exp(-(distSq * invRangeSigmaSq));
                        var w = spatial * range;
                        if (w <= 0)
                            continue;

                        accR += p.R * w;
                        accG += p.G * w;
                        accB += p.B * w;
                        sumW += w;
                    }
                }

                if (sumW <= 1e-9)
                {
                    image[x, y] = center;
                    continue;
                }

                image[x, y] = new Rgba32(
                    (byte)Math.Clamp((int)Math.Round(accR / sumW), 0, 255),
                    (byte)Math.Clamp((int)Math.Round(accG / sumW), 0, 255),
                    (byte)Math.Clamp((int)Math.Round(accB / sumW), 0, 255),
                    center.A);
            }
        }

        source.Dispose();
    }

    private static double[,] BuildSpatialKernel(int radius, double sigmaSpatial)
    {
        var size = radius * 2 + 1;
        var kernel = new double[size, size];
        var invSigmaSq = 1.0 / (2.0 * sigmaSpatial * sigmaSpatial);
        for (var y = -radius; y <= radius; y++)
        {
            for (var x = -radius; x <= radius; x++)
            {
                var d2 = x * x + y * y;
                kernel[y + radius, x + radius] = Math.Exp(-d2 * invSigmaSq);
            }
        }
        return kernel;
    }
}

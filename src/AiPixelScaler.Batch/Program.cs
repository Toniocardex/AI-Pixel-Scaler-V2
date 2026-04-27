using AiPixelScaler.Core.Pipeline.Export;
using AiPixelScaler.Core.Pipeline.Imaging;
using SixLabors.ImageSharp;
using SixLabors.ImageSharp.Formats.Png;
using SixLabors.ImageSharp.PixelFormats;

namespace AiPixelScaler.Batch;

/// <summary>
/// Elaborazione batch: cartella in → cartella out (solo PNG nel primo livello).
/// Uso: aipixel-batch &lt;inputDir&gt; &lt;outputDir&gt; [--indexed] [--palette N] [--chroma-snap #RRGGBB tol] [--shared-palette]
/// </summary>
internal static class Program
{
    private static int Main(string[] args)
    {
        if (args.Length < 2)
        {
            Console.Error.WriteLine(
                "Uso: aipixel-batch <cartellaIngresso> <cartellaUscita> [--indexed] [--palette N] [--chroma-snap #RRGGBB tol] [--shared-palette]");
            return 1;
        }

        var inputDir = Path.GetFullPath(args[0]);
        var outputDir = Path.GetFullPath(args[1]);
        var indexed = args.Contains("--indexed", StringComparer.OrdinalIgnoreCase);
        var sharedPalette = args.Contains("--shared-palette", StringComparer.OrdinalIgnoreCase);
        var paletteN = ParseFlagInt(args, "--palette");
        ParseChromaSnap(args, out var snapKey, out var snapTol);

        if (!Directory.Exists(inputDir))
        {
            Console.Error.WriteLine($"Cartella non trovata: {inputDir}");
            return 2;
        }

        Directory.CreateDirectory(outputDir);

        var files = Directory.EnumerateFiles(inputDir, "*.png", SearchOption.TopDirectoryOnly).ToList();
        if (files.Count == 0)
        {
            Console.WriteLine("Nessun PNG nella cartella (solo primo livello).");
            return 0;
        }

        IReadOnlyList<Rgba32>? globalPalette = null;
        if (sharedPalette && paletteN is >= 2 and <= 256)
            globalPalette = SharedPaletteBuilder.BuildFromFiles(files, paletteN.Value, snapKey, snapTol);

        var processed = 0;
        var failed = 0;
        var started = DateTime.UtcNow;
        foreach (var path in files)
        {
            try
            {
                var name = Path.GetFileName(path);
                var outPath = Path.Combine(outputDir, name);
                using var image = Image.Load<Rgba32>(path);

                var options = new PixelArtPipeline.Options(
                    EnableChroma: snapKey.HasValue,
                    ChromaSnapRgb: snapKey.HasValue,
                    ChromaColor: snapKey ?? new Rgba32(0, 255, 0, 255),
                    ChromaTolerance: snapTol,
                    EnableQuantize: globalPalette is null && paletteN is >= 2 and <= 256,
                    MaxColors: paletteN.GetValueOrDefault(16),
                    Quantizer: PixelArtProcessor.QuantizerKind.KMeansOklab);
                PixelArtPipeline.ApplyInPlace(image, options);

                IReadOnlyList<Rgba32>? palette = globalPalette;
                if (palette is { Count: > 0 })
                    PaletteMapper.ApplyInPlace(image, palette, PaletteMapper.DitherMode.None);

                if (indexed)
                {
                    using var fs = File.Create(outPath);
                    if (palette is { Count: > 0 })
                        IndexedPngExporter.SaveWithLockedPalette(image, fs, palette);
                    else
                        IndexedPngExporter.SaveWithWuQuantize(image, fs);
                }
                else
                {
                    image.Save(outPath, new PngEncoder());
                }

                processed++;
                Console.WriteLine($"OK  {outPath}");
            }
            catch (Exception ex)
            {
                failed++;
                Console.Error.WriteLine($"ERR {path} :: {ex.Message}");
            }
        }

        var elapsed = DateTime.UtcNow - started;
        Console.WriteLine(
            $"Report: files={files.Count}, processed={processed}, failed={failed}, sharedPalette={(globalPalette is { Count: > 0 })}, elapsedMs={(int)elapsed.TotalMilliseconds}");

        return 0;
    }

    private static int? ParseFlagInt(string[] args, string flag)
    {
        for (var i = 0; i < args.Length - 1; i++)
        {
            if (!string.Equals(args[i], flag, StringComparison.OrdinalIgnoreCase)) continue;
            return int.TryParse(args[i + 1], out var n) ? n : null;
        }
        return null;
    }

    private static void ParseChromaSnap(string[] args, out Rgba32? key, out double tol)
    {
        key = null;
        tol = 0;
        for (var i = 0; i < args.Length - 2; i++)
        {
            if (!string.Equals(args[i], "--chroma-snap", StringComparison.OrdinalIgnoreCase)) continue;
            if (!TryParseHexRgb(args[i + 1], out var k)) break;
            key = k;
            if (double.TryParse(args[i + 2], System.Globalization.NumberStyles.Float,
                    System.Globalization.CultureInfo.InvariantCulture, out var t))
                tol = Math.Max(0, t);
            break;
        }
    }

    private static bool TryParseHexRgb(string s, out Rgba32 color)
    {
        color = default;
        var t = s.Trim();
        if (t.StartsWith('#')) t = t[1..];
        if (t.Length != 6) return false;
        if (!uint.TryParse(t, System.Globalization.NumberStyles.HexNumber,
                System.Globalization.CultureInfo.InvariantCulture, out var v)) return false;
        color = new Rgba32((byte)(v >> 16), (byte)(v >> 8), (byte)v, 255);
        return true;
    }
}

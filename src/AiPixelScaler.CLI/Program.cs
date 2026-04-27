using AiPixelScaler.Core.Pipeline.Imaging;
using SixLabors.ImageSharp;
using SixLabors.ImageSharp.PixelFormats;

namespace AiPixelScaler.CLI;

internal static class Program
{
    private static int Main(string[] args)
    {
        if (args.Length == 0 || args[0] is "--help" or "-h")
        {
            PrintHelp();
            return 0;
        }

        if (!string.Equals(args[0], "process", StringComparison.OrdinalIgnoreCase) || args.Length < 3)
        {
            Console.Error.WriteLine("Comando non valido.");
            PrintHelp();
            return 1;
        }

        var input = args[1];
        var output = args[2];
        if (!File.Exists(input))
        {
            Console.Error.WriteLine($"Input non trovato: {input}");
            return 2;
        }

        using var image = Image.Load<Rgba32>(input);
        var report = PixelArtPipeline.ApplyInPlace(image, new PixelArtPipeline.Options());
        image.Save(output);
        Console.WriteLine($"OK {output} :: {string.Join(" -> ", report.Steps)}");
        return 0;
    }

    private static void PrintHelp()
    {
        Console.WriteLine("AiPixelScaler CLI");
        Console.WriteLine("Uso:");
        Console.WriteLine("  aipixel-cli process <input.png> <output.png>");
    }
}

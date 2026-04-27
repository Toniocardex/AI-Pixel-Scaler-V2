using SixLabors.ImageSharp;
using SixLabors.ImageSharp.Formats.Png;
using SixLabors.ImageSharp.PixelFormats;
using SixLabors.ImageSharp.Processing;
using SixLabors.ImageSharp.Processing.Processors.Quantization;

namespace AiPixelScaler.Core.Pipeline.Export;

/// <summary>
/// Export PNG in modalità palette 8-bit (PLTE + IDAT indicizzato), usando
/// una palette fissa (lock) oppure quantizzazione Wu integrata nell'encoder.
/// </summary>
public static class IndexedPngExporter
{
    private static readonly PngEncoder EncoderPalette8 = new()
    {
        ColorType = PngColorType.Palette,
        BitDepth = PngBitDepth.Bit8,
    };

    /// <summary>
    /// Salva con quantizzazione Wu automatica (nessuna palette preimpostata).
    /// </summary>
    public static void SaveWithWuQuantize(Image<Rgba32> image, Stream destination)
    {
        image.Save(destination, EncoderPalette8);
    }

    /// <summary>
    /// Salva usando la palette fornita (max 256 voci, incluso eventualmente il trasparente).
    /// L'immagine deve essere già vicina ai colori della palette; l'encoder rimappa ai indici.
    /// </summary>
    public static void SaveWithLockedPalette(Image<Rgba32> image, Stream destination,
        IReadOnlyList<Rgba32> palette)
    {
        if (palette.Count == 0 || palette.Count > 256)
            throw new ArgumentOutOfRangeException(nameof(palette), "La palette deve avere tra 1 e 256 colori.");

        using var work = image.Clone();
        var png = work.Metadata.GetPngMetadata();
        png.ColorType = PngColorType.Palette;
        png.BitDepth = PngBitDepth.Bit8;

        var colors = new global::SixLabors.ImageSharp.Color[palette.Count];
        for (var i = 0; i < palette.Count; i++)
            colors[i] = global::SixLabors.ImageSharp.Color.FromPixel(palette[i]);
        png.ColorTable = colors;

        work.Save(destination, EncoderPalette8);
    }
}

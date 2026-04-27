using SixLabors.ImageSharp;
using SixLabors.ImageSharp.PixelFormats;
using SixLabors.ImageSharp.Processing;

namespace AiPixelScaler.Core.Pipeline.Imaging;

public static class QuantizeChannels
{
    /// <summary>Posterizzazione: <paramref name="levelsPerChannel"/> ∈ [2, 256] livelli per canale (come scale discrete lungo 0..255).</summary>
    public static void ApplyInPlace(Image<Rgba32> image, int levelsPerChannel)
    {
        if (levelsPerChannel is < 2 or > 256) return;
        var levels = levelsPerChannel;
        image.ProcessPixelRows(accessor =>
        {
            for (var y = 0; y < accessor.Height; y++)
            {
                var row = accessor.GetRowSpan(y);
                for (var x = 0; x < row.Length; x++)
                {
                    var p = row[x];
                    if (p.A == 0) continue;
                    var r = Q(p.R, levels);
                    var g = Q(p.G, levels);
                    var b = Q(p.B, levels);
                    row[x] = new Rgba32(r, g, b, p.A);
                }
            }
        });
    }

    private static byte Q(byte v, int levels)
    {
        if (levels <= 1) return 0;
        var step = 255.0 / (levels - 1);
        return (byte)Math.Clamp((int)(Math.Round(v / step) * step), 0, 255);
    }
}

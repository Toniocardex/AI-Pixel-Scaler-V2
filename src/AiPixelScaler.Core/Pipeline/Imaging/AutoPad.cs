using SixLabors.ImageSharp;
using SixLabors.ImageSharp.PixelFormats;

namespace AiPixelScaler.Core.Pipeline.Imaging;

/// <summary>
/// Aggiunge bordo trasparente intorno all'immagine. Utile per:
///   • Evitare che pixel toccanti il bordo vengano tagliati dai filtri/scaling
///   • Creare "respiro" intorno allo sprite per outline o shadow successivi
///   • Allineare a multipli di tile size (es. 8/16/32 px)
/// </summary>
public static class AutoPad
{
    public static Image<Rgba32> Apply(Image<Rgba32> source, int padding)
    {
        if (padding <= 0) return source.Clone();
        return Apply(source, padding, padding, padding, padding);
    }

    public static Image<Rgba32> Apply(Image<Rgba32> source, int padLeft, int padTop, int padRight, int padBottom)
    {
        padLeft   = Math.Max(0, padLeft);
        padTop    = Math.Max(0, padTop);
        padRight  = Math.Max(0, padRight);
        padBottom = Math.Max(0, padBottom);

        var sw = source.Width;
        var sh = source.Height;
        var newW = sw + padLeft + padRight;
        var newH = sh + padTop + padBottom;
        var dst  = new Image<Rgba32>(newW, newH, new Rgba32(0, 0, 0, 0));

        // Copia attraverso buffer intermedio (CS9108: PixelAccessor non può vivere in lambda annidate)
        var buf = new Rgba32[sw * sh];
        source.ProcessPixelRows(a =>
        {
            for (var y = 0; y < a.Height; y++)
            {
                var row = a.GetRowSpan(y);
                for (var x = 0; x < row.Length; x++) buf[y * sw + x] = row[x];
            }
        });
        dst.ProcessPixelRows(a =>
        {
            for (var y = 0; y < sh; y++)
            {
                var dRow = a.GetRowSpan(y + padTop);
                for (var x = 0; x < sw; x++) dRow[x + padLeft] = buf[y * sw + x];
            }
        });

        return dst;
    }

    public static Image<Rgba32> PadToMultiple(Image<Rgba32> source, int multiple)
    {
        if (multiple < 2) return source.Clone();
        var targetW = ((source.Width  + multiple - 1) / multiple) * multiple;
        var targetH = ((source.Height + multiple - 1) / multiple) * multiple;
        return Apply(source, 0, 0, targetW - source.Width, targetH - source.Height);
    }
}

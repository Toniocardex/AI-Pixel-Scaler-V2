using SixLabors.ImageSharp;
using SixLabors.ImageSharp.PixelFormats;

namespace AiPixelScaler.Core.Pipeline.Imaging;

/// <summary>
/// Forza la simmetria orizzontale o verticale: copia una metà sull'altra.
/// Tipico fix per sprite IA dove il character è quasi-frontale ma con piccole asimmetrie.
///
/// La metà di riferimento è scelta con il flag <c>fromLeft</c> / <c>fromTop</c>:
///   • <c>fromLeft=true</c>: la sinistra è la "verità", viene specchiata sulla destra.
///   • <c>fromLeft=false</c>: viceversa.
///
/// L'asse di simmetria coincide con la colonna/riga centrale; per immagini con
/// larghezza dispari, la colonna centrale resta invariata.
/// </summary>
public static class SymmetryMirror
{
    public static void MirrorHorizontal(Image<Rgba32> image, bool fromLeft = true)
    {
        var w = image.Width;
        var h = image.Height;
        if (w < 2 || h < 1) return;
        var halfW = w / 2;

        image.ProcessPixelRows(accessor =>
        {
            for (var y = 0; y < accessor.Height; y++)
            {
                var row = accessor.GetRowSpan(y);
                if (fromLeft)
                {
                    for (var x = 0; x < halfW; x++)
                        row[w - 1 - x] = row[x];
                }
                else
                {
                    for (var x = 0; x < halfW; x++)
                        row[x] = row[w - 1 - x];
                }
            }
        });
    }

    public static void MirrorVertical(Image<Rgba32> image, bool fromTop = true)
    {
        var w = image.Width;
        var h = image.Height;
        if (w < 1 || h < 2) return;
        var halfH = h / 2;

        var src = new Rgba32[w];
        for (var y = 0; y < halfH; y++)
        {
            var srcY = fromTop ? y          : h - 1 - y;
            var dstY = fromTop ? h - 1 - y  : y;
            // copia riga completa
            image.ProcessPixelRows(a =>
            {
                a.GetRowSpan(srcY).CopyTo(src);
                src.CopyTo(a.GetRowSpan(dstY));
            });
        }
    }
}

using AiPixelScaler.Core.Geometry;
using SixLabors.ImageSharp;
using SixLabors.ImageSharp.PixelFormats;

namespace AiPixelScaler.Core.Pipeline.Imaging;

/// <summary>
/// Genera un contorno esterno di 1 px attorno a tutti i pixel opachi.
///
/// Vicinato:
///   • <see cref="PixelConnectivity.Four"/>  (Von Neumann, croce)  — outline più sottile
///   • <see cref="PixelConnectivity.Eight"/> (Moore, 3×3)         — outline pieno (anche diagonali)
///
/// Per pixel art retrò: 4-conn è il default classico.
/// Per sprite con dettagli "a punte" o per separare nettamente sprite contigui: 8-conn.
/// </summary>
public static class Outline1Px
{
    public static void ApplyOuterInPlace(Image<Rgba32> image, Rgba32 outline,
                                          PixelConnectivity connectivity = PixelConnectivity.Four)
    {
        var w = image.Width;
        var h = image.Height;
        if (w < 1 || h < 1) return;

        var opaque = new bool[w * h];
        var snapshot = new Rgba32[w * h];
        image.ProcessPixelRows(a =>
        {
            for (var y = 0; y < a.Height; y++)
            {
                var row = a.GetRowSpan(y);
                for (var x = 0; x < row.Length; x++)
                {
                    var p = row[x];
                    opaque[y * w + x] = p.A > 0;
                    snapshot[y * w + x] = p;
                }
            }
        });

        for (var y = 0; y < h; y++)
        for (var x = 0; x < w; x++)
        {
            var i = y * w + x;
            if (snapshot[i].A > 0) continue;
            if (TouchesOpaque(x, y, w, h, opaque, connectivity))
                snapshot[i] = outline;
        }

        image.ProcessPixelRows(a =>
        {
            for (var y = 0; y < a.Height; y++)
            {
                var row = a.GetRowSpan(y);
                for (var x = 0; x < row.Length; x++) row[x] = snapshot[y * w + x];
            }
        });
    }

    private static bool TouchesOpaque(int x, int y, int w, int h, bool[] opaque, PixelConnectivity c)
    {
        bool O(int nx, int ny) => nx >= 0 && ny >= 0 && nx < w && ny < h && opaque[ny * w + nx];

        if (O(x - 1, y) || O(x + 1, y) || O(x, y - 1) || O(x, y + 1)) return true;
        if (c == PixelConnectivity.Four) return false;
        return O(x - 1, y - 1) || O(x + 1, y - 1) || O(x - 1, y + 1) || O(x + 1, y + 1);
    }
}

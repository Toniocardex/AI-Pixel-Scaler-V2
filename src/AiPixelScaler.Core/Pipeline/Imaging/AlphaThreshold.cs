using SixLabors.ImageSharp;
using SixLabors.ImageSharp.PixelFormats;

namespace AiPixelScaler.Core.Pipeline.Imaging;

/// <summary>
/// Binarizzazione alpha. Due strategie:
///
/// 1) <see cref="ApplyInPlace(Image{Rgba32}, byte)"/> — soglia singola "hard":
///       A &gt; T → 255 ; A ≤ T → 0
///    Veloce, senza ambiguità sui bordi.
///
/// 2) <see cref="ApplyHysteresis(Image{Rgba32}, byte, byte)"/> — doppia soglia (Canny-style):
///       A ≥ T_high             → opaco (255)
///       A ≤ T_low              → trasparente (0)
///       T_low &lt; A &lt; T_high  → opaco se 4-connesso a un pixel ≥ T_high, altrimenti 0
///    Mantiene gli edge sottili che la soglia singola taglierebbe.
///
/// Per sprite IA: hysteresis ≈ T_low=64, T_high=160 produce silhouette pulite
/// senza "scalettature" su capelli, dita, antenne.
/// </summary>
public static class AlphaThreshold
{
    public const byte DefaultThreshold = 128;

    public static void ApplyInPlace(Image<Rgba32> image, byte threshold = DefaultThreshold)
    {
        image.ProcessPixelRows(accessor =>
        {
            for (var y = 0; y < accessor.Height; y++)
            {
                var row = accessor.GetRowSpan(y);
                for (var x = 0; x < row.Length; x++)
                {
                    var p = row[x];
                    row[x] = new Rgba32(p.R, p.G, p.B, p.A > threshold ? (byte)255 : (byte)0);
                }
            }
        });
    }

    public static void ApplyHysteresis(Image<Rgba32> image, byte tLow, byte tHigh)
    {
        if (tLow > tHigh) (tLow, tHigh) = (tHigh, tLow);
        var w = image.Width;
        var h = image.Height;
        if (w == 0 || h == 0) return;

        var state = new byte[w * h]; // 0=trasparente, 1=ambiguo, 2=opaco
        image.ProcessPixelRows(a =>
        {
            for (var y = 0; y < a.Height; y++)
            {
                var row = a.GetRowSpan(y);
                for (var x = 0; x < row.Length; x++)
                {
                    var av = row[x].A;
                    state[y * w + x] = av >= tHigh ? (byte)2 : av > tLow ? (byte)1 : (byte)0;
                }
            }
        });

        // Propagazione "strong" sugli ambigui via BFS 4-conn
        var queue = new Queue<int>();
        for (var i = 0; i < state.Length; i++)
            if (state[i] == 2) queue.Enqueue(i);

        while (queue.Count > 0)
        {
            var i = queue.Dequeue();
            var x = i % w;
            var y = i / w;
            void Try(int nx, int ny)
            {
                if (nx < 0 || ny < 0 || nx >= w || ny >= h) return;
                var ni = ny * w + nx;
                if (state[ni] == 1) { state[ni] = 2; queue.Enqueue(ni); }
            }
            Try(x + 1, y); Try(x - 1, y); Try(x, y + 1); Try(x, y - 1);
        }

        image.ProcessPixelRows(accessor =>
        {
            for (var y = 0; y < accessor.Height; y++)
            {
                var row = accessor.GetRowSpan(y);
                for (var x = 0; x < row.Length; x++)
                {
                    var p = row[x];
                    row[x] = new Rgba32(p.R, p.G, p.B, state[y * w + x] == 2 ? (byte)255 : (byte)0);
                }
            }
        });
    }
}

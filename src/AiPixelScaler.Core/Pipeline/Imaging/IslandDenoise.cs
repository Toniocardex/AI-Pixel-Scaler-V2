using AiPixelScaler.Core.Geometry;
using SixLabors.ImageSharp;
using SixLabors.ImageSharp.PixelFormats;

namespace AiPixelScaler.Core.Pipeline.Imaging;

/// <summary>
/// Rimuove componenti connesse di pixel "foreground" (alpha ≥ soglia) con area &lt; <see cref="Options.MinIslandArea"/>.
///
/// Vicinato selezionabile:
///   • <see cref="PixelConnectivity.Four"/>  (Von Neumann) — più conservativo, due "isole" diagonali
///     non vengono fuse, quindi vengono cancellate se piccole.
///   • <see cref="PixelConnectivity.Eight"/> (Moore) — fonde anche le diagonali, mantiene cluster
///     "a scacchiera" che il 4-conn taglierebbe.
///
/// Per sprite IA: 8-conn è il default consigliato (preserva texture sottili).
/// </summary>
public static class IslandDenoise
{
    public readonly struct Options
    {
        public byte AlphaThreshold              { get; init; }
        public int  MinIslandArea               { get; init; }
        public PixelConnectivity Connectivity   { get; init; }

        public Options(byte alphaThreshold = 1, int minIslandArea = 2,
                        PixelConnectivity connectivity = PixelConnectivity.Eight)
        {
            AlphaThreshold = alphaThreshold;
            MinIslandArea  = minIslandArea;
            Connectivity   = connectivity;
        }
    }

    public static void ApplyInPlace(Image<Rgba32> image, Options options)
    {
        if (image.Width == 0 || image.Height == 0 || options.MinIslandArea < 1) return;

        var w = image.Width;
        var h = image.Height;
        var visited = new bool[w * h];
        var stack = new Stack<(int x, int y)>(256);
        var component = new List<(int x, int y)>(64);
        Rgba32 clear = default;

        bool IsForeground(int x, int y) => image[x, y].A >= options.AlphaThreshold;
        int Index(int x, int y) => y * w + x;

        for (var y = 0; y < h; y++)
        for (var x = 0; x < w; x++)
        {
            var i = Index(x, y);
            if (visited[i] || !IsForeground(x, y)) continue;

            component.Clear();
            stack.Clear();
            stack.Push((x, y));
            visited[i] = true;

            while (stack.Count > 0)
            {
                var (cx, cy) = stack.Pop();
                component.Add((cx, cy));
                void Try(int nx, int ny)
                {
                    if (nx < 0 || ny < 0 || nx >= w || ny >= h) return;
                    var ni = Index(nx, ny);
                    if (visited[ni] || !IsForeground(nx, ny)) return;
                    visited[ni] = true;
                    stack.Push((nx, ny));
                }
                Try(cx + 1, cy); Try(cx - 1, cy); Try(cx, cy + 1); Try(cx, cy - 1);
                if (options.Connectivity == PixelConnectivity.Eight)
                {
                    Try(cx + 1, cy + 1); Try(cx - 1, cy + 1);
                    Try(cx + 1, cy - 1); Try(cx - 1, cy - 1);
                }
            }

            if (component.Count < options.MinIslandArea)
                foreach (var (px, py) in component) image[px, py] = clear;
        }
    }
}

using AiPixelScaler.Core.Geometry;
using SixLabors.ImageSharp;
using SixLabors.ImageSharp.PixelFormats;

namespace AiPixelScaler.Core.Pipeline.Slicing;

/// <summary>
/// Auto-slicing per Connected Component Labeling (CCL).
///
/// Per ogni componente connessa (4 o 8-conn) di pixel con A ≥ <c>alphaThreshold</c>:
///   • bounding box → SpriteCell
///   • opzionalmente filtra cluster con area &lt; <c>minArea</c> (rumore)
///   • opzionalmente espande il bbox di <c>padding</c> px (clampato all'atlas)
///
/// L'8-conn raggruppa anche cluster diagonali — utile per sprite con dettagli sottili
/// (capelli, antenne, parti staccate per anti-aliasing).
/// </summary>
public static class CclAutoSlicer
{
    public record Options(
        byte Alpha = 1,
        int MinArea = 4,
        int Padding = 0,
        PixelConnectivity Connectivity = PixelConnectivity.Eight);

    public static IReadOnlyList<SpriteCell> Slice(Image<Rgba32> image, Options? options = null)
    {
        options ??= new Options();
        if (image.Width == 0 || image.Height == 0) return [];

        var w = image.Width;
        var h = image.Height;
        var visited = new bool[w * h];
        var stack = new Stack<(int x, int y)>(256);
        var list = new List<SpriteCell>();
        var cc = 0;
        int Index(int x, int y) => y * w + x;
        bool IsForeground(int x, int y) => image[x, y].A >= options.Alpha;

        for (var y = 0; y < h; y++)
        for (var x = 0; x < w; x++)
        {
            var i = Index(x, y);
            if (visited[i] || !IsForeground(x, y)) continue;

            int minX = x, maxX = x, minY = y, maxY = y, area = 0;
            stack.Clear();
            stack.Push((x, y));
            visited[i] = true;

            while (stack.Count > 0)
            {
                var (cx, cy) = stack.Pop();
                area++;
                if (cx < minX) minX = cx;
                if (cx > maxX) maxX = cx;
                if (cy < minY) minY = cy;
                if (cy > maxY) maxY = cy;
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

            if (area < options.MinArea) continue;

            var pad = Math.Max(0, options.Padding);
            var box = AxisAlignedBox.FromInclusivePixelBounds(
                Math.Max(0, minX - pad),
                Math.Max(0, minY - pad),
                Math.Min(w - 1, maxX + pad),
                Math.Min(h - 1, maxY + pad));
            list.Add(new SpriteCell($"c{cc}", box));
            cc++;
        }

        return list;
    }
}

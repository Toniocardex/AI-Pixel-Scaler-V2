using SixLabors.ImageSharp;
using SixLabors.ImageSharp.PixelFormats;

namespace AiPixelScaler.Core.Pipeline.Imaging;

public static class NearestNeighborResize
{
    /// <summary>
    /// Nearest-neighbor: per ogni pixel destinazione (dx,dy), campiona sorgente
    /// sx = floor(ox + dx * Ws / Wd), sy = floor(oy + dy * Hs / Hd) (allineato alla reference roadmap).
    /// </summary>
    public static Image<Rgba32> Resize(Image<Rgba32> source, int targetWidth, int targetHeight, double offsetX = 0, double offsetY = 0)
    {
        if (targetWidth < 1 || targetHeight < 1)
            return new Image<Rgba32>(0, 0);
        var sw = source.Width;
        var sh = source.Height;
        if (sw < 1 || sh < 1)
            return new Image<Rgba32>(0, 0);
        var dest = new Image<Rgba32>(targetWidth, targetHeight);
        for (var dy = 0; dy < targetHeight; dy++)
        {
            for (var dx = 0; dx < targetWidth; dx++)
            {
                var sx = (int)Math.Floor(offsetX + dx * (double)sw / targetWidth);
                var sy = (int)Math.Floor(offsetY + dy * (double)sh / targetHeight);
                sx = Math.Clamp(sx, 0, sw - 1);
                sy = Math.Clamp(sy, 0, sh - 1);
                dest[dx, dy] = source[sx, sy];
            }
        }
        return dest;
    }
}

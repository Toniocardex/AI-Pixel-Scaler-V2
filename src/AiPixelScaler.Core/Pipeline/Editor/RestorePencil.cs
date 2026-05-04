using SixLabors.ImageSharp;
using SixLabors.ImageSharp.PixelFormats;

namespace AiPixelScaler.Core.Pipeline.Editor;

public static class RestorePencil
{
    public static int ApplyInPlace(Image<Rgba32> image, int x, int y, int sideLength, Rgba32 color)
    {
        var side = Math.Max(1, sideLength);
        var xMin = Math.Clamp(x, 0, image.Width);
        var yMin = Math.Clamp(y, 0, image.Height);
        var xMax = Math.Clamp(x + side, 0, image.Width);
        var yMax = Math.Clamp(y + side, 0, image.Height);
        if (xMin >= xMax || yMin >= yMax)
            return 0;

        color.A = 255;
        var changed = 0;
        image.ProcessPixelRows(accessor =>
        {
            for (var py = yMin; py < yMax; py++)
            {
                var row = accessor.GetRowSpan(py);
                for (var px = xMin; px < xMax; px++)
                {
                    if (row[px].A != 0)
                        continue;

                    row[px] = color;
                    changed++;
                }
            }
        });

        return changed;
    }
}

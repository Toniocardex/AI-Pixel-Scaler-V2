using AiPixelScaler.Core.Geometry;
using SixLabors.ImageSharp;
using SixLabors.ImageSharp.PixelFormats;
using SixLabors.ImageSharp.Processing;

namespace AiPixelScaler.Core.Pipeline.Normalization;

public static class AtlasCropper
{
    public static Image<Rgba32> Crop(Image<Rgba32> atlas, in AxisAlignedBox box)
    {
        var w = box.Width;
        var h = box.Height;
        if (w < 1 || h < 1)
            return new Image<Rgba32>(0, 0);
        if (box.MinX < 0 || box.MinY < 0 || box.MaxX > atlas.Width || box.MaxY > atlas.Height)
            throw new ArgumentOutOfRangeException(nameof(box), "Crop fuori dal bordo atlas.");

        var x0 = box.MinX;
        var y0 = box.MinY;
        return atlas.Clone(c => c.Crop(new Rectangle(x0, y0, w, h)));
    }
}

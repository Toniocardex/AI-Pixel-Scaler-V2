using AiPixelScaler.Core.Geometry;
using AiPixelScaler.Core.Pipeline.Imaging;
using AiPixelScaler.Core.Pipeline.Slicing;
using SixLabors.ImageSharp;
using SixLabors.ImageSharp.PixelFormats;

namespace AiPixelScaler.Core.Pipeline.Normalization;

public static class GlobalLayout
{
    /// <summary>
    /// Scansione globale: massima larghezza e altezza dei contenuti (alpha ≥ soglia) nelle celle.
    /// </summary>
    public static (int globalW, int globalH) ComputeGlobalContentSize(
        Image<Rgba32> atlas,
        IReadOnlyList<SpriteCell> cells,
        byte alphaThreshold = 1)
    {
        var maxW = 0;
        var maxH = 0;
        foreach (var cell in cells)
        {
            using var crop = AtlasCropper.Crop(atlas, cell.BoundsInAtlas);
            if (crop.Width == 0)
                continue;
            var b = AlphaBoundingBox.Compute(crop, alphaThreshold);
            if (b.IsEmpty)
                continue;
            if (b.Width > maxW) maxW = b.Width;
            if (b.Height > maxH) maxH = b.Height;
        }
        return (maxW, maxH);
    }

    /// <summary>
    /// Offset per allineare il bounding box del contenuto al centro di un rettangolo <paramref name="globalW"/>x<paramref name="globalH"/>
    /// (coordinate 0,0 = angolo in alto a sinistra dello slot normalizzato).
    /// </summary>
    public static (int offX, int offY) CenterContent(in AxisAlignedBox contentLocal, int globalW, int globalH)
    {
        if (contentLocal.IsEmpty)
            return (0, 0);
        var w = contentLocal.Width;
        var h = contentLocal.Height;
        var ox = (globalW - w) / 2 - contentLocal.MinX;
        var oy = (globalH - h) / 2 - contentLocal.MinY;
        return (ox, oy);
    }
}

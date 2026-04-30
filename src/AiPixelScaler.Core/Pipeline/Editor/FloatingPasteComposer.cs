using SixLabors.ImageSharp;
using SixLabors.ImageSharp.PixelFormats;
using SixLabors.ImageSharp.Processing;
using AiPixelScaler.Core.Pipeline.Imaging;

namespace AiPixelScaler.Core.Pipeline.Editor;

/// <summary>
/// Fonde la selezione fluttuante sul documento con nearest-neighbor se serve ridimensionare.
/// </summary>
public static class FloatingPasteComposer
{
    /// <summary>
    /// Disegna <paramref name="paste"/> su <paramref name="document"/> in <paramref name="destX"/>, <paramref name="destY"/>.
    /// Usa <paramref name="displayScale"/> con <see cref="FloatingPasteGeometry.ComputeDisplayDimensions"/> per decidere
    /// se ridimensionare con nearest-neighbor prima del disegno.
    /// </summary>
    public static void Commit(Image<Rgba32> document, Image<Rgba32> paste, int destX, int destY, double displayScale)
    {
        if (document.Width < 1 || document.Height < 1 || paste.Width < 1 || paste.Height < 1)
            return;

        var scale = Math.Clamp(displayScale, double.Epsilon, 1.0);
        var (dw, dh) = FloatingPasteGeometry.ComputeDisplayDimensions(paste.Width, paste.Height, scale);

        if (dw == paste.Width && dh == paste.Height)
        {
            document.Mutate(ctx => ctx.DrawImage(paste, new SixLabors.ImageSharp.Point(destX, destY), 1f));
            return;
        }

        using var scaled = NearestNeighborResize.Resize(paste, dw, dh);
        document.Mutate(ctx => ctx.DrawImage(scaled, new SixLabors.ImageSharp.Point(destX, destY), 1f));
    }
}

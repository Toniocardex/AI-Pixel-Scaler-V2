using SixLabors.ImageSharp;
using SixLabors.ImageSharp.PixelFormats;
using SixLabors.ImageSharp.Processing;
using Rectangle = SixLabors.ImageSharp.Rectangle;

namespace AiPixelScaler.Core.Pipeline.Normalization;

/// <summary>
/// Ritaglia l’atlas all’area coperta da un numero intero di tile (cell + spacing) a partire da offset.
/// </summary>
public static class GridAlignmentCropper
{
    public static Image<Rgba32> CropToValidGrid(
        Image<Rgba32> sourceImage,
        int offsetX,
        int offsetY,
        int cellWidth,
        int cellHeight,
        int spacingX = 0,
        int spacingY = 0)
    {
        ArgumentNullException.ThrowIfNull(sourceImage);

        if (sourceImage.Width < 1 || sourceImage.Height < 1)
            return sourceImage.Clone();

        spacingX = Math.Max(0, spacingX);
        spacingY = Math.Max(0, spacingY);
        cellWidth = Math.Max(1, cellWidth);
        cellHeight = Math.Max(1, cellHeight);

        offsetX = Math.Clamp(offsetX, 0, Math.Max(0, sourceImage.Width - 1));
        offsetY = Math.Clamp(offsetY, 0, Math.Max(0, sourceImage.Height - 1));

        var availableWidth = sourceImage.Width - offsetX;
        var availableHeight = sourceImage.Height - offsetY;

        var cols = (availableWidth + spacingX) / (cellWidth + spacingX);
        var rows = (availableHeight + spacingY) / (cellHeight + spacingY);

        if (cols <= 0 || rows <= 0)
            return sourceImage.Clone();

        var finalWidth = cols * cellWidth + (cols - 1) * spacingX;
        var finalHeight = rows * cellHeight + (rows - 1) * spacingY;

        var cropRect = new Rectangle(offsetX, offsetY, finalWidth, finalHeight);
        if (cropRect.Right > sourceImage.Width || cropRect.Bottom > sourceImage.Height)
            return sourceImage.Clone();

        return sourceImage.Clone(ctx => ctx.Crop(cropRect));
    }
}

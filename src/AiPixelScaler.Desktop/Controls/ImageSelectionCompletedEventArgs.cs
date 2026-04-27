using System;
using AiPixelScaler.Core.Geometry;

namespace AiPixelScaler.Desktop.Controls;

/// <param name="Box">AABB in coordinate atlas (come <see cref="AtlasCropper"/>): intervalli [Min,Max) in pixel.</param>
public sealed class ImageSelectionCompletedEventArgs : EventArgs
{
    public ImageSelectionCompletedEventArgs(AxisAlignedBox box) => Box = box;

    public AxisAlignedBox Box { get; }
}

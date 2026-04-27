using System;

namespace AiPixelScaler.Desktop.Controls;

public sealed class ImagePixelPickedEventArgs : EventArgs
{
    public int X { get; }
    public int Y { get; }

    public ImagePixelPickedEventArgs(int x, int y)
    {
        X = x;
        Y = y;
    }
}

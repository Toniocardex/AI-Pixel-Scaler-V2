using System;

namespace AiPixelScaler.Desktop.Controls;

public sealed class FloatingOverlayMoveEventArgs : EventArgs
{
    public FloatingOverlayMoveEventArgs(int x, int y)
    {
        X = x;
        Y = y;
    }

    public int X { get; }
    public int Y { get; }
}

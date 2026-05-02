using System;

namespace AiPixelScaler.Desktop.Controls;

/// <summary>
/// Emesso da <see cref="EditorSurface"/> durante il drag della gomma.
/// Le coordinate sono il pixel alto-sinistra del quadrato gomma in spazio immagine.
/// </summary>
public sealed class EraserStrokeEventArgs(int imageX, int imageY, int size) : EventArgs
{
    public int ImageX  { get; } = imageX;
    public int ImageY  { get; } = imageY;
    /// <summary>Dimensione del quadrato gomma in pixel-immagine.</summary>
    public int Size  { get; } = size;
    public int Radius => Size;
}

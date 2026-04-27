using System;

namespace AiPixelScaler.Desktop.Controls;

/// <summary>
/// Emesso da <see cref="EditorSurface"/> durante il drag della gomma.
/// Le coordinate sono in pixel-immagine (spazio mondo).
/// </summary>
public sealed class EraserStrokeEventArgs(int imageX, int imageY, int radius) : EventArgs
{
    public int ImageX  { get; } = imageX;
    public int ImageY  { get; } = imageY;
    /// <summary>Raggio del pennello in pixel-immagine.</summary>
    public int Radius  { get; } = radius;
}

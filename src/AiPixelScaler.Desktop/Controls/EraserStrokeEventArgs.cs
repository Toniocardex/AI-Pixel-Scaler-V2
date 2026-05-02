using System;

namespace AiPixelScaler.Desktop.Controls;

/// <summary>
/// Emesso da <see cref="EditorSurface"/> durante il drag della gomma.
/// Le coordinate sono il pixel alto-sinistra del quadrato gomma in spazio immagine.
/// La gomma è sempre un quadrato: il cursore cancella un blocco di
/// <see cref="SideLength"/>×<see cref="SideLength"/> pixel.
/// </summary>
public sealed class EraserStrokeEventArgs(int imageX, int imageY, int sideLength) : EventArgs
{
    /// <summary>Coordinata X in pixel-immagine dell'angolo superiore sinistro del quadrato gomma.</summary>
    public int ImageX { get; } = imageX;

    /// <summary>Coordinata Y in pixel-immagine dell'angolo superiore sinistro del quadrato gomma.</summary>
    public int ImageY { get; } = imageY;

    /// <summary>Lato del quadrato gomma in pixel-immagine. Sempre ≥ 1.</summary>
    public int SideLength { get; } = Math.Max(1, sideLength);
}

using System;

namespace AiPixelScaler.Desktop.Controls;

/// <summary>
/// Drag di un frame in workbench mode. Le delta sono in pixel-immagine
/// rispetto alla posizione di inizio drag (NON cumulative dall'inizio della modalità).
/// </summary>
public sealed class FrameDragEventArgs(int frameIndex, int dxImage, int dyImage, bool isCommit) : EventArgs
{
    public int  FrameIndex { get; } = frameIndex;
    public int  DxImage    { get; } = dxImage;
    public int  DyImage    { get; } = dyImage;
    /// <summary>True se è il rilascio finale del drag (mouse up), false durante il movimento.</summary>
    public bool IsCommit   { get; } = isCommit;
}

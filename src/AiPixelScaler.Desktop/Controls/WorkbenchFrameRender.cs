using System;
using AiPixelScaler.Core.Geometry;
using Avalonia.Media.Imaging;

namespace AiPixelScaler.Desktop.Controls;

/// <summary>
/// Frame del workbench in formato pronto al render: <see cref="Bitmap"/> Avalonia
/// (convertito una sola volta all'ingresso del workbench) + offset modificabile in tempo reale.
///
/// Permette il rendering diretto in <see cref="EditorSurface"/> senza ricomporre l'intero
/// atlas ad ogni mouse-move durante il drag — fix critico per la fluidità del workbench.
/// </summary>
public sealed class WorkbenchFrameRender : IDisposable
{
    public required Bitmap          Content     { get; init; }
    public required AxisAlignedBox  Cell        { get; init; }
    public required int             Padding     { get; init; }
    public required int             ContentW    { get; init; }
    public required int             ContentH    { get; init; }

    /// <summary>Offset corrente nello spazio cella (mutabile, aggiornato dal drag).</summary>
    public Avalonia.Point Offset;

    public void Dispose() => Content.Dispose();
}

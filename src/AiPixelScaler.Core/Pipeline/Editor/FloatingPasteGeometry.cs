namespace AiPixelScaler.Core.Pipeline.Editor;

/// <summary>
/// Geometria per incolla fluttuante: scala uniforme “entra nel canvas”, dimensioni anteprima intere, posizione centrata.
/// </summary>
public static class FloatingPasteGeometry
{
    /// <summary>
    /// Fattore in (0, 1] per far stare l’intera sorgente dentro il canvas mantenendo le proporzioni.
    /// 1 se la sorgente è già contenuta (o dimensioni non valide).
    /// </summary>
    public static double ComputeUniformFitScale(int sourceWidth, int sourceHeight, int canvasWidth, int canvasHeight)
    {
        if (sourceWidth < 1 || sourceHeight < 1 || canvasWidth < 1 || canvasHeight < 1)
            return 1.0;
        if (sourceWidth <= canvasWidth && sourceHeight <= canvasHeight)
            return 1.0;
        return Math.Min(canvasWidth / (double)sourceWidth, canvasHeight / (double)sourceHeight);
    }

    /// <summary>
    /// Dimensioni in pixel dell’anteprima (o del commit con NN) quando si applica <paramref name="displayScale"/> alla sorgente.
    /// Intere, almeno 1×1.
    /// </summary>
    public static (int width, int height) ComputeDisplayDimensions(int sourceWidth, int sourceHeight, double displayScale)
    {
        if (sourceWidth < 1 || sourceHeight < 1)
            return (1, 1);
        var s = Math.Clamp(displayScale, double.Epsilon, 1.0);
        var w = Math.Max(1, (int)Math.Floor(sourceWidth * s));
        var h = Math.Max(1, (int)Math.Floor(sourceHeight * s));
        return (w, h);
    }

    /// <summary>
    /// Angolo in alto a sinistra per centrare un rettangolo <paramref name="contentWidth"/>×<paramref name="contentHeight"/> nel canvas.
    /// </summary>
    public static (int x, int y) ComputeCenteredTopLeft(int canvasWidth, int canvasHeight, int contentWidth, int contentHeight)
    {
        var x = (canvasWidth - contentWidth) / 2;
        var y = (canvasHeight - contentHeight) / 2;
        return (x, y);
    }
}

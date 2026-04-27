namespace AiPixelScaler.Core.Editor;

/// <summary>
/// Zoom e pan per il workspace. Convenzione: <c>Screen = World * Zoom + Pan</c> (in pixel schermo), coerente con
/// traslazione dopo scala nella catena affina (θ=0, Sx=Sy=Zoom).
/// </summary>
public sealed class Viewport2D
{
    public double Zoom { get; set; } = 1.0;
    public double PanX { get; set; }
    public double PanY { get; set; }

    public (double x, double y) WorldToScreen(double worldX, double worldY) =>
        (worldX * Zoom + PanX, worldY * Zoom + PanY);

    public (double x, double y) ScreenToWorld(double screenX, double screenY) =>
        ((screenX - PanX) / Zoom, (screenY - PanY) / Zoom);

    public void ZoomAtScreenPoint(double newZoom, double screenAnchorX, double screenAnchorY)
    {
        newZoom = Math.Clamp(newZoom, 0.05, 64.0);
        var (wx, wy) = ScreenToWorld(screenAnchorX, screenAnchorY);
        Zoom = newZoom;
        PanX = screenAnchorX - wx * Zoom;
        PanY = screenAnchorY - wy * Zoom;
    }
}

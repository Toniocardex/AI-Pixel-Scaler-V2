using AiPixelScaler.Core.Editor;

namespace AiPixelScaler.Core.Tests;

public class Viewport2DTests
{
    [Fact]
    public void WorldToScreen_ScreenToWorld_roundtrip()
    {
        var v = new Viewport2D { Zoom = 2, PanX = 10, PanY = -5 };
        var (sx, sy) = v.WorldToScreen(3, 4);
        Assert.Equal(16, sx);
        Assert.Equal(3, sy);
        var (wx, wy) = v.ScreenToWorld(sx, sy);
        Assert.Equal(3, wx);
        Assert.Equal(4, wy);
    }

    [Fact]
    public void ZoomAtScreenPoint_preserves_world_under_cursor()
    {
        var v = new Viewport2D { Zoom = 1, PanX = 0, PanY = 0 };
        v.ZoomAtScreenPoint(2, 100, 200);
        var (wx, wy) = v.ScreenToWorld(100, 200);
        Assert.InRange(wx, 99.5, 100.5);
        Assert.InRange(wy, 199.5, 200.5);
    }
}

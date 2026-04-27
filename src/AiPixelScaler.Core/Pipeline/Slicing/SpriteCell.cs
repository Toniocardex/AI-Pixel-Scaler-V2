using AiPixelScaler.Core.Geometry;

namespace AiPixelScaler.Core.Pipeline.Slicing;

public sealed class SpriteCell
{
    public SpriteCell(string id, AxisAlignedBox boundsInAtlas, double pivotNdcX = 0.5, double pivotNdcY = 0.5)
    {
        Id = id;
        BoundsInAtlas = boundsInAtlas;
        PivotNdcX = pivotNdcX;
        PivotNdcY = pivotNdcY;
    }

    public string Id { get; }
    public AxisAlignedBox BoundsInAtlas { get; }
    public double PivotNdcX { get; }
    public double PivotNdcY { get; }
}

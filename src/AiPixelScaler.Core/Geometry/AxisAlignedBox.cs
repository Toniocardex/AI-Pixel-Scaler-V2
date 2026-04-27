namespace AiPixelScaler.Core.Geometry;

/// <summary>
/// AABB in integer pixel space using half-open intervals: Min &lt;= coord &lt; Max on each axis.
/// Matches the collision reference: A and B overlap iff all of
/// A_minX &lt; B_maxX, A_maxX &gt; B_minX, A_minY &lt; B_maxY, A_maxY &gt; B_minY.
/// </summary>
public readonly struct AxisAlignedBox : IEquatable<AxisAlignedBox>
{
    public AxisAlignedBox(int minX, int minY, int maxX, int maxY)
    {
        MinX = minX;
        MinY = minY;
        MaxX = maxX;
        MaxY = maxY;
    }

    public int MinX { get; }
    public int MinY { get; }
    public int MaxX { get; }
    public int MaxY { get; }

    public int Width => MaxX - MinX;
    public int Height => MaxY - MinY;

    public bool IsEmpty => MinX >= MaxX || MinY >= MaxY;

    public static bool Intersects(in AxisAlignedBox a, in AxisAlignedBox b) =>
        a.MinX < b.MaxX && a.MaxX > b.MinX && a.MinY < b.MaxY && a.MaxY > b.MinY;

    public static AxisAlignedBox FromInclusivePixelBounds(int minX, int minY, int maxXIncl, int maxYIncl) =>
        new(minX, minY, maxXIncl + 1, maxYIncl + 1);

    public bool Equals(AxisAlignedBox other) =>
        MinX == other.MinX && MinY == other.MinY && MaxX == other.MaxX && MaxY == other.MaxY;

    public override bool Equals(object? obj) => obj is AxisAlignedBox other && Equals(other);

    public override int GetHashCode() => HashCode.Combine(MinX, MinY, MaxX, MaxY);
}

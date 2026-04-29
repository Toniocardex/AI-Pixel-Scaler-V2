using System;

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

    /// <summary>
    /// Converte due angoli in coordinate mondo (pixel continue, tipicamente già agganciati alla griglia)
    /// in un box half-open dentro [0,<paramref name="imgW"/>) × [0,<paramref name="imgH"/>).
    /// Usa <see cref="Math.Floor"/> sul minimo e <see cref="Math.Ceiling"/> sul massimo così uno span da 0 a 1280
    /// (bordo esclusivo dopo l’ultimo pixel della tessera) ha larghezza 1280, non 1281 come succedeva
    /// combinando Floor su entrambi gli angoli con <see cref="FromInclusivePixelBounds"/>.
    /// </summary>
    public static AxisAlignedBox FromWorldCornersHalfOpen(
        double wx0, double wy0, double wx1, double wy1, int imgW, int imgH)
    {
        if (imgW < 1 || imgH < 1)
            return new AxisAlignedBox(0, 0, 0, 0);

        var loX = Math.Min(wx0, wx1);
        var hiX = Math.Max(wx0, wx1);
        var loY = Math.Min(wy0, wy1);
        var hiY = Math.Max(wy0, wy1);

        var minX = (int)Math.Floor(loX);
        var maxX = (int)Math.Ceiling(hiX);
        var minY = (int)Math.Floor(loY);
        var maxY = (int)Math.Ceiling(hiY);

        minX = Math.Clamp(minX, 0, imgW);
        maxX = Math.Clamp(maxX, 0, imgW);
        minY = Math.Clamp(minY, 0, imgH);
        maxY = Math.Clamp(maxY, 0, imgH);

        if (maxX <= minX || maxY <= minY)
            return new AxisAlignedBox(0, 0, 0, 0);

        return new AxisAlignedBox(minX, minY, maxX, maxY);
    }

    public bool Equals(AxisAlignedBox other) =>
        MinX == other.MinX && MinY == other.MinY && MaxX == other.MaxX && MaxY == other.MaxY;

    public override bool Equals(object? obj) => obj is AxisAlignedBox other && Equals(other);

    public override int GetHashCode() => HashCode.Combine(MinX, MinY, MaxX, MaxY);
}

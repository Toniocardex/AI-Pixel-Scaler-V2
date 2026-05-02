using AiPixelScaler.Core.Geometry;
using AiPixelScaler.Core.Pipeline.Imaging;
using AiPixelScaler.Core.Pipeline.Normalization;
using AiPixelScaler.Core.Pipeline.Slicing;
using SixLabors.ImageSharp;
using SixLabors.ImageSharp.PixelFormats;

namespace AiPixelScaler.Core.Tests;

// ─── AlphaThreshold ──────────────────────────────────────────────────────────

public class AlphaThresholdTests
{
    [Fact]
    public void PixelAboveThreshold_BecomesFullyOpaque()
    {
        using var img = new Image<Rgba32>(1, 1);
        img[0, 0] = new Rgba32(200, 100, 50, 200); // alpha 200 > 128

        AlphaThreshold.ApplyInPlace(img, 128);

        Assert.Equal(255, img[0, 0].A);
        Assert.Equal(200, img[0, 0].R); // colore invariato
    }

    [Fact]
    public void PixelAtThreshold_BecomesTransparent()
    {
        using var img = new Image<Rgba32>(1, 1);
        img[0, 0] = new Rgba32(255, 0, 0, 128); // alpha 128 = soglia → trasparente

        AlphaThreshold.ApplyInPlace(img, 128);

        Assert.Equal(0, img[0, 0].A);
    }

    [Fact]
    public void PixelBelowThreshold_BecomesTransparent()
    {
        using var img = new Image<Rgba32>(1, 1);
        img[0, 0] = new Rgba32(255, 255, 0, 50); // alpha 50 < 128

        AlphaThreshold.ApplyInPlace(img, 128);

        Assert.Equal(0, img[0, 0].A);
    }

    [Fact]
    public void FullyOpaque_StaysOpaque()
    {
        using var img = new Image<Rgba32>(3, 3, new Rgba32(100, 150, 200, 255));
        AlphaThreshold.ApplyInPlace(img, 128);

        for (var y = 0; y < 3; y++)
        for (var x = 0; x < 3; x++)
            Assert.Equal(255, img[x, y].A);
    }

    [Fact]
    public void FullyTransparent_StaysTransparent()
    {
        using var img = new Image<Rgba32>(2, 2, new Rgba32(0, 0, 0, 0));
        AlphaThreshold.ApplyInPlace(img, 128);

        for (var y = 0; y < 2; y++)
        for (var x = 0; x < 2; x++)
            Assert.Equal(0, img[x, y].A);
    }

    [Fact]
    public void RgbChannels_Unchanged_AfterThresholding()
    {
        using var img = new Image<Rgba32>(1, 1);
        img[0, 0] = new Rgba32(11, 22, 33, 200);
        AlphaThreshold.ApplyInPlace(img, 100);

        var p = img[0, 0];
        Assert.Equal(11, p.R);
        Assert.Equal(22, p.G);
        Assert.Equal(33, p.B);
        Assert.Equal(255, p.A);
    }
}

// ─── BackgroundIsolation (distanza Euclidea) ───────────────────────────────────────────

public class BackgroundIsolationEuclideanTests
{
    [Fact]
    public void ExactMatch_ToleranceZero_RemovesPixel()
    {
        using var img = new Image<Rgba32>(1, 1);
        img[0, 0] = new Rgba32(0, 255, 0, 255);

        BackgroundIsolation.ApplyInPlace(img, new(new Rgba32(0, 255, 0, 255), ColorTolerance: 0, ProtectStrongEdges: false, UseOklab: false));

        Assert.Equal(0, img[0, 0].A);
    }

    [Fact]
    public void SlightlyDifferent_ToleranceZero_KeepsPixel()
    {
        using var img = new Image<Rgba32>(1, 1);
        img[0, 0] = new Rgba32(1, 255, 0, 255); // distanza = √1 > 0

        BackgroundIsolation.ApplyInPlace(img, new(new Rgba32(0, 255, 0, 255), ColorTolerance: 0, ProtectStrongEdges: false, UseOklab: false));

        Assert.Equal(255, img[0, 0].A);
    }

    [Fact]
    public void WithinEuclideanTolerance_RemovesPixel()
    {
        // distanza Euclidea da (0,255,0) a (3,255,4) = √(9+0+16) = 5
        using var img = new Image<Rgba32>(1, 1);
        img[0, 0] = new Rgba32(3, 255, 4, 255);

        BackgroundIsolation.ApplyInPlace(img, new(new Rgba32(0, 255, 0, 255), ColorTolerance: 5, ProtectStrongEdges: false, UseOklab: false));

        Assert.Equal(0, img[0, 0].A);
    }

    [Fact]
    public void OutsideEuclideanTolerance_KeepsPixel()
    {
        // distanza Euclidea da (0,255,0) a (6,255,0) = 6 > tolerance 5
        using var img = new Image<Rgba32>(1, 1);
        img[0, 0] = new Rgba32(6, 255, 0, 255);

        BackgroundIsolation.ApplyInPlace(img, new(new Rgba32(0, 255, 0, 255), ColorTolerance: 5, ProtectStrongEdges: false, UseOklab: false));

        Assert.Equal(255, img[0, 0].A);
    }

    [Fact]
    public void AlreadyTransparent_IsSkipped()
    {
        using var img = new Image<Rgba32>(1, 1);
        img[0, 0] = new Rgba32(0, 255, 0, 0); // già trasparente

        BackgroundIsolation.ApplyInPlace(img, new(new Rgba32(0, 255, 0, 255), ColorTolerance: 100, ProtectStrongEdges: false, UseOklab: false));

        // non deve cambiare A (era già 0)
        Assert.Equal(0, img[0, 0].A);
    }
}

// ─── BaselineAlignment ───────────────────────────────────────────────────────

public class BaselineAlignmentTests
{
    [Fact]
    public void SingleCell_FindsBottomRow()
    {
        // 4x4, sprite occupa righe 1-2 (Y=1 e Y=2), bottom = 2
        using var img = new Image<Rgba32>(4, 4, new Rgba32(0, 0, 0, 0));
        for (var x = 0; x < 4; x++)
        {
            img[x, 1] = new Rgba32(255, 0, 0, 255);
            img[x, 2] = new Rgba32(255, 0, 0, 255);
        }

        var yBottom = BaselineAlignment.FindBottomSolidRow(img, alphaThreshold: 1);

        Assert.Equal(2, yBottom);
    }

    [Fact]
    public void FindBottomSolidRow_AllTransparent_ReturnsMinusOne()
    {
        using var img = new Image<Rgba32>(4, 4, new Rgba32(0, 0, 0, 0));
        Assert.Equal(-1, BaselineAlignment.FindBottomSolidRow(img));
    }

    [Fact]
    public void Align_NormalizesHeightToMaxAndAlignsBaseline()
    {
        // Sprite A: 4x4, solido in riga 1 (yBottom=1, H=4 → ΔY=4-1-1=2)
        // Sprite B: 4x6, solido in riga 4 (yBottom=4, H=6 → ΔY=6-1-4=1)
        // H_max = max(4,6) = 6

        using var atlasA = new Image<Rgba32>(4, 4, new Rgba32(0, 0, 0, 0));
        for (var x = 0; x < 4; x++) atlasA[x, 1] = new Rgba32(255, 0, 0, 255);

        using var atlasB = new Image<Rgba32>(4, 6, new Rgba32(0, 0, 0, 0));
        for (var x = 0; x < 4; x++) atlasB[x, 4] = new Rgba32(0, 255, 0, 255);

        // costruisci atlante orizzontale 8x6
        using var combined = new Image<Rgba32>(8, 6, new Rgba32(0, 0, 0, 0));
        for (var y = 0; y < 4; y++)
        for (var x = 0; x < 4; x++)
            combined[x, y] = atlasA[x, y];
        for (var y = 0; y < 6; y++)
        for (var x = 0; x < 4; x++)
            combined[4 + x, y] = atlasB[x, y];

        var cells = new List<SpriteCell>
        {
            new("A", new AxisAlignedBox(0, 0, 4, 4)),
            new("B", new AxisAlignedBox(4, 0, 8, 6))
        };

        using var result = BaselineAlignment.Align(combined, cells);

        // L'atlas risultante ha H = H_max = 6
        Assert.Equal(6, result.Atlas.Height);
        // Tutte le celle hanno la stessa larghezza e altezza
        Assert.Equal(2, result.Cells.Count);
        Assert.Equal(result.Cells[0].BoundsInAtlas.Height, result.Cells[1].BoundsInAtlas.Height);
    }

    [Fact]
    public void Align_EmptyCells_ReturnsMinimalAtlas()
    {
        using var img = new Image<Rgba32>(10, 10);
        var result = BaselineAlignment.Align(img, []);
        Assert.NotNull(result.Atlas);
        Assert.Empty(result.Cells);
        result.Dispose();
    }
}

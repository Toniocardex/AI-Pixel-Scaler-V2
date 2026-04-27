using AiPixelScaler.Core.Geometry;
using AiPixelScaler.Core.Pipeline.Normalization;
using SixLabors.ImageSharp;
using SixLabors.ImageSharp.PixelFormats;

namespace AiPixelScaler.Core.Tests;

public class SizingMathTests
{
    [Theory]
    [InlineData(0,    1)]
    [InlineData(1,    1)]
    [InlineData(2,    2)]
    [InlineData(3,    4)]
    [InlineData(8,    8)]
    [InlineData(9,    16)]
    [InlineData(192,  256)]   // esempio del prompt
    [InlineData(1024, 1024)]  // POT invariata
    [InlineData(1025, 2048)]
    public void NextPow2_ReturnsCorrect(int input, int expected)
    {
        Assert.Equal(expected, SizingMath.NextPow2(input));
    }

    [Theory]
    [InlineData(1,    true)]
    [InlineData(2,    true)]
    [InlineData(64,   true)]
    [InlineData(1024, true)]
    [InlineData(0,    false)]
    [InlineData(3,    false)]
    [InlineData(192,  false)]
    public void IsPow2_DetectsCorrectly(int n, bool expected)
    {
        Assert.Equal(expected, SizingMath.IsPow2(n));
    }
}

public class CropPipelineTests
{
    [Fact]
    public void TrimToContent_RemovesEmptyBorders()
    {
        // 8×8 trasparente con sprite 2×2 al centro (3,3 → 4,4 inclusivo)
        using var img = new Image<Rgba32>(8, 8, new Rgba32(0, 0, 0, 0));
        for (var y = 3; y <= 4; y++)
        for (var x = 3; x <= 4; x++)
            img[x, y] = new Rgba32(255, 0, 0, 255);

        using var result = CropPipeline.Apply(img, new CropPipeline.Options
        {
            Mode = CropPipeline.CropMode.TrimToContent,
            AlphaThreshold = 1,
        });

        Assert.Equal(2, result.FinalW);
        Assert.Equal(2, result.FinalH);
        Assert.False(result.PotApplied);
        // Box: half-open [3, 5)
        Assert.Equal(3, result.CropBox.MinX);
        Assert.Equal(5, result.CropBox.MaxX);
    }

    [Fact]
    public void TrimToContentPadded_AddsPaddingClampedToAtlas()
    {
        // 10×10 trasparente, sprite 1×1 al centro (5,5)
        using var img = new Image<Rgba32>(10, 10, new Rgba32(0, 0, 0, 0));
        img[5, 5] = new Rgba32(255, 0, 0, 255);

        using var result = CropPipeline.Apply(img, new CropPipeline.Options
        {
            Mode = CropPipeline.CropMode.TrimToContentPadded,
            AlphaThreshold = 1,
            PaddingPx = 3,
        });

        // AABB = [5, 6) × [5, 6). +3 padding → [2, 9) × [2, 9). Tutto dentro 10×10 → no clamp.
        Assert.Equal(7, result.FinalW);
        Assert.Equal(7, result.FinalH);
    }

    [Fact]
    public void TrimToContentPadded_PaddingClampsAtAtlasEdges()
    {
        // sprite 1×1 nell'angolo (0,0), padding 5 → dovrebbe clampare al bordo
        using var img = new Image<Rgba32>(10, 10, new Rgba32(0, 0, 0, 0));
        img[0, 0] = new Rgba32(255, 0, 0, 255);

        using var result = CropPipeline.Apply(img, new CropPipeline.Options
        {
            Mode = CropPipeline.CropMode.TrimToContentPadded,
            PaddingPx = 5,
        });

        // AABB [0,1)×[0,1) + 5 = [-5,6)×[-5,6). Clamp a [0,6)×[0,6) → 6×6.
        Assert.Equal(6, result.FinalW);
        Assert.Equal(6, result.FinalH);
        Assert.Equal(0, result.CropBox.MinX);
        Assert.Equal(0, result.CropBox.MinY);
    }

    [Fact]
    public void UserRoi_RespectsRectangleAndClampsToBounds()
    {
        using var img = new Image<Rgba32>(10, 10, new Rgba32(0, 0, 0, 255));
        // ROI esce a destra: [5, 15) × [0, 10)  → clamp a [5, 10) × [0, 10) = 5×10
        var roi = new AxisAlignedBox(5, 0, 15, 10);

        using var result = CropPipeline.Apply(img, new CropPipeline.Options
        {
            Mode = CropPipeline.CropMode.UserRoi,
            UserRoi = roi,
        });

        Assert.Equal(5,  result.FinalW);
        Assert.Equal(10, result.FinalH);
        Assert.Equal(5,  result.CropBox.MinX);
        Assert.Equal(10, result.CropBox.MaxX);
    }

    [Fact]
    public void Pot_PerAxis_192x100_To_256x128()
    {
        // sprite 192×100 esatto (no padding)
        using var img = new Image<Rgba32>(192, 100, new Rgba32(255, 0, 0, 255));

        using var result = CropPipeline.Apply(img, new CropPipeline.Options
        {
            Mode = CropPipeline.CropMode.TrimToContent,
            Pot = CropPipeline.PotPolicy.PerAxis,
        });

        Assert.Equal(256, result.FinalW);
        Assert.Equal(128, result.FinalH);
        Assert.True(result.PotApplied);
    }

    [Fact]
    public void Pot_Square_192x100_To_256x256()
    {
        using var img = new Image<Rgba32>(192, 100, new Rgba32(255, 0, 0, 255));

        using var result = CropPipeline.Apply(img, new CropPipeline.Options
        {
            Mode = CropPipeline.CropMode.TrimToContent,
            Pot = CropPipeline.PotPolicy.Square,
        });

        Assert.Equal(256, result.FinalW);
        Assert.Equal(256, result.FinalH);
    }

    [Fact]
    public void Pot_AlreadyPow2_LeavesUnchanged()
    {
        // 1024×1024 esatto, già POT
        using var img = new Image<Rgba32>(1024, 1024, new Rgba32(255, 0, 0, 255));

        using var result = CropPipeline.Apply(img, new CropPipeline.Options
        {
            Mode = CropPipeline.CropMode.TrimToContent,
            Pot = CropPipeline.PotPolicy.PerAxis,
        });

        Assert.Equal(1024, result.FinalW);
        Assert.Equal(1024, result.FinalH);
        Assert.False(result.PotApplied);
    }

    [Fact]
    public void TrimToContent_OnFullyTransparent_ReturnsEmpty1x1()
    {
        using var img = new Image<Rgba32>(8, 8, new Rgba32(0, 0, 0, 0));

        using var result = CropPipeline.Apply(img, new CropPipeline.Options
        {
            Mode = CropPipeline.CropMode.TrimToContent,
        });

        Assert.Equal(1, result.FinalW);
        Assert.Equal(1, result.FinalH);
        Assert.Contains("no-op", result.Description);
    }

    [Fact]
    public void Pipeline_TrimThenPaddingThenPot_OrderIsRespected()
    {
        // 100×100 atlas, sprite 50×50 al centro. Trim → 50×50. Padding 10 → 70×70.
        // POT PerAxis → 128×128.
        using var img = new Image<Rgba32>(100, 100, new Rgba32(0, 0, 0, 0));
        for (var y = 25; y < 75; y++)
        for (var x = 25; x < 75; x++)
            img[x, y] = new Rgba32(255, 0, 0, 255);

        using var result = CropPipeline.Apply(img, new CropPipeline.Options
        {
            Mode = CropPipeline.CropMode.TrimToContentPadded,
            PaddingPx = 10,
            Pot = CropPipeline.PotPolicy.PerAxis,
        });

        // box trim+pad = [15, 85) → 70×70. POT → 128×128.
        Assert.Equal(70,  result.CropBox.Width);
        Assert.Equal(128, result.FinalW);
        Assert.Equal(128, result.FinalH);
        Assert.True(result.PotApplied);
    }
}

public class FrameStatisticsTests
{
    [Fact]
    public void Compute_BasicStats_ComputeCorrectly()
    {
        var boxes = new List<AxisAlignedBox>
        {
            new(0, 0, 10, 10),
            new(0, 0, 12, 12),
            new(0, 0, 11, 11),
            new(0, 0, 13, 13),
            new(0, 0, 14, 14),
        };

        var stats = FrameStatistics.Compute(boxes);
        Assert.Equal(14, stats.MaxW);
        Assert.Equal(14, stats.MaxH);
        Assert.Equal(12, stats.MedianW);
        Assert.Equal(12, stats.MedianH);
        Assert.Equal(5,  stats.Count);
    }

    [Fact]
    public void Median_RobustToOutlier()
    {
        // 4 frame di altezza ~10, 1 frame outlier di altezza 100
        var boxes = new List<AxisAlignedBox>
        {
            new(0, 0, 10, 10),
            new(0, 0, 10, 11),
            new(0, 0, 10, 9),
            new(0, 0, 10, 10),
            new(0, 0, 10, 100),  // outlier
        };

        var stats = FrameStatistics.Compute(boxes);
        var (_, hMax)    = FrameStatistics.SelectSize(stats, FrameStatistics.NormalizePolicy.Max);
        var (_, hMedian) = FrameStatistics.SelectSize(stats, FrameStatistics.NormalizePolicy.Median);

        Assert.Equal(100, hMax);     // gonfia
        Assert.True(hMedian <= 11);  // robusto
        Assert.True(stats.OutlierCountH >= 1);
        Assert.NotNull(FrameStatistics.FormatOutlierWarning(stats));
    }

    [Fact]
    public void EmptyList_ReturnsZeroStats()
    {
        var stats = FrameStatistics.Compute(new List<AxisAlignedBox>());
        Assert.Equal(0, stats.Count);
        Assert.Equal(0, stats.MaxW);
        Assert.Null(FrameStatistics.FormatOutlierWarning(stats));
    }
}

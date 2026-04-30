using AiPixelScaler.Core.Pipeline.Normalization;
using SixLabors.ImageSharp;
using SixLabors.ImageSharp.PixelFormats;

namespace AiPixelScaler.Core.Tests;

public class GridAlignmentCropperTests
{
    [Fact]
    public void CropToValidGrid_no_spacing_counts_full_tiles()
    {
        using var img = new Image<Rgba32>(100, 80);
        using var outImg = GridAlignmentCropper.CropToValidGrid(img, 0, 0, 32, 16, 0, 0);
        // cols = 100/32 = 3, rows = 80/16 = 5
        Assert.Equal(96, outImg.Width);
        Assert.Equal(80, outImg.Height);
    }

    [Fact]
    public void CropToValidGrid_with_spacing_matches_formula()
    {
        using var img = new Image<Rgba32>(100, 80);
        using var outImg = GridAlignmentCropper.CropToValidGrid(img, 0, 0, 32, 16, 2, 2);
        // period X = 34, cols = (100+2)/34 = 3, finalW = 96 + 4 = 100
        Assert.Equal(100, outImg.Width);
        // period Y = 18, rows = (80+2)/18 = 4, finalH = 64 + 6 = 70
        Assert.Equal(70, outImg.Height);
    }

    [Fact]
    public void CropToValidGrid_with_offset_reduces_available()
    {
        using var img = new Image<Rgba32>(100, 80);
        using var outImg = GridAlignmentCropper.CropToValidGrid(img, 40, 0, 32, 16, 0, 0);
        // available width 60 -> cols = 1 -> width 32
        Assert.Equal(32, outImg.Width);
        Assert.Equal(80, outImg.Height);
    }

    [Fact]
    public void CropToValidGrid_returns_clone_when_no_full_tile()
    {
        using var img = new Image<Rgba32>(30, 30);
        using var outImg = GridAlignmentCropper.CropToValidGrid(img, 0, 0, 32, 32, 0, 0);
        Assert.Equal(img.Width, outImg.Width);
        Assert.Equal(img.Height, outImg.Height);
    }
}

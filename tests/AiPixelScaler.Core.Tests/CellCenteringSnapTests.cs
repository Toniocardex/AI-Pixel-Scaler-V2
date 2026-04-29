using AiPixelScaler.Core.Geometry;
using AiPixelScaler.Core.Pipeline.Normalization;
using AiPixelScaler.Core.Pipeline.Slicing;
using SixLabors.ImageSharp;
using SixLabors.ImageSharp.PixelFormats;

namespace AiPixelScaler.Core.Tests;

public class CellCenteringSnapTests
{
    [Fact]
    public void SnapPasteDest_prefers_aligned_opaque_corner_when_cell_room_allows()
    {
        // Cella atlas X [8,32), opaque in crop [5,15); paste ∈ [3,17], span 14 ≥ 8 → snap attivo
        var d = CellCentering.SnapPasteDestForOpaqueTopLeftModulo(
            idealPasteDest: 13,
            opaqueMinLocal: 5,
            opaqueMaxExclusiveLocal: 15,
            cellMinAtlas: 8,
            cellMaxExclusiveAtlas: 32,
            gridMultiple: 8);
        Assert.Equal(11, d); // 11+5=16; più vicino a 13 di 3 (dist 10)
    }

    [Fact]
    public void SnapPasteDest_skips_when_paste_range_narrower_than_grid()
    {
        var d = CellCentering.SnapPasteDestForOpaqueTopLeftModulo(
            idealPasteDest: 13,
            opaqueMinLocal: 5,
            opaqueMaxExclusiveLocal: 15,
            cellMinAtlas: 8,
            cellMaxExclusiveAtlas: 24,
            gridMultiple: 8);
        Assert.Equal(13, d); // span hi-lo = 6 < 8 → niente snap
    }

    [Fact]
    public void Center_aligned_opaque_top_left_modulo_8()
    {
        using var atlas = new Image<Rgba32>(64, 32, new Rgba32(0, 0, 0, 0));
        var box = new AxisAlignedBox(8, 0, 40, 32);
        var cells = new[] { new SpriteCell("c0", box) };
        for (var y = 4; y < 28; y++)
        for (var x = 11; x < 33; x++)
            atlas[x, y] = new Rgba32(255, 0, 0, 255);

        using var r = CellCentering.Center(atlas, cells, alphaThreshold: 1, opaqueCornerSnapMultiple: 8);
        using var cropAfter = AtlasCropper.Crop(r.Atlas, box);
        var ob = CellCentering.FindOpaqueBox(cropAfter);
        Assert.NotNull(ob);
        var leftAtlas = box.MinX + ob!.Value.MinX;
        Assert.Equal(0, leftAtlas % 8);
        Assert.Equal(0, (box.MinY + ob.Value.MinY) % 8);
    }
}

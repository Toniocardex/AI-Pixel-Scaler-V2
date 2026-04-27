using AiPixelScaler.Core.Geometry;
using AiPixelScaler.Core.Pipeline.Editor;
using AiPixelScaler.Core.Pipeline.Slicing;
using SixLabors.ImageSharp;
using SixLabors.ImageSharp.PixelFormats;

namespace AiPixelScaler.Core.Tests;

public class FrameSheetTests
{
    [Fact]
    public void Extract_TwoCells_ProducesTwoFramesWithCorrectContent()
    {
        // Atlas 8×4: cella sinistra rossa, destra verde
        using var atlas = new Image<Rgba32>(8, 4, new Rgba32(0, 0, 0, 0));
        for (var y = 0; y < 4; y++)
        for (var x = 0; x < 4; x++)
            atlas[x, y] = new Rgba32(255, 0, 0, 255);
        for (var y = 0; y < 4; y++)
        for (var x = 4; x < 8; x++)
            atlas[x, y] = new Rgba32(0, 255, 0, 255);

        var cells = new List<SpriteCell>
        {
            new("0", new AxisAlignedBox(0, 0, 4, 4)),
            new("1", new AxisAlignedBox(4, 0, 8, 4)),
        };

        using var sheet = FrameSheet.ExtractFromAtlas(atlas, cells, padding: 0);
        Assert.Equal(2, sheet.Frames.Count);
        Assert.Equal(255, sheet.Frames[0].Content[1, 1].R);
        Assert.Equal(0,   sheet.Frames[0].Content[1, 1].G);
        Assert.Equal(0,   sheet.Frames[1].Content[1, 1].R);
        Assert.Equal(255, sheet.Frames[1].Content[1, 1].G);
    }

    [Fact]
    public void Compose_NoOffsets_IsRoundtripOfOriginal()
    {
        using var atlas = new Image<Rgba32>(6, 3);
        for (var y = 0; y < 3; y++)
        for (var x = 0; x < 6; x++)
            atlas[x, y] = new Rgba32((byte)(x * 40), (byte)(y * 80), 100, 255);

        var cells = new List<SpriteCell>
        {
            new("0", new AxisAlignedBox(0, 0, 3, 3)),
            new("1", new AxisAlignedBox(3, 0, 6, 3)),
        };

        using var sheet = FrameSheet.ExtractFromAtlas(atlas, cells);
        using var composed = sheet.Compose();

        Assert.Equal(atlas.Width,  composed.Width);
        Assert.Equal(atlas.Height, composed.Height);
        for (var y = 0; y < 3; y++)
        for (var x = 0; x < 6; x++)
        {
            Assert.Equal(atlas[x, y].R, composed[x, y].R);
            Assert.Equal(atlas[x, y].G, composed[x, y].G);
            Assert.Equal(atlas[x, y].A, composed[x, y].A);
        }
    }

    [Fact]
    public void AutoCenter_OffsetMovesContentToCellCenter()
    {
        // 4×4 cella: sprite 1×1 nell'angolo (0,0) → AutoCenter deve spostarlo verso (1,1) o (2,2)
        using var atlas = new Image<Rgba32>(4, 4, new Rgba32(0, 0, 0, 0));
        atlas[0, 0] = new Rgba32(255, 0, 0, 255);

        var cells = new List<SpriteCell> { new("0", new AxisAlignedBox(0, 0, 4, 4)) };
        using var sheet = FrameSheet.ExtractFromAtlas(atlas, cells);

        var f = sheet.Frames[0];
        Assert.True(f.AutoCenter());

        // Cella 4×4: cell.W/2 = 2, content single px at (0,0) → contentCx=0, cy=0
        // Offset = (2 - 0 + 0, 2 - 0 + 0) = (2, 2)
        Assert.Equal(2, f.Offset.X);
        Assert.Equal(2, f.Offset.Y);

        using var composed = sheet.Compose();
        // Il pixel rosso ora è a (0+2, 0+2) = (2, 2) nell'atlas
        Assert.Equal(255, composed[2, 2].R);
        Assert.Equal(0,   composed[0, 0].A); // l'originale (0,0) è ora trasparente
    }

    [Fact]
    public void AlignToBaseline_PutsContentBottomAtCellBottom()
    {
        // Cella 4×4: sprite 1×1 nell'angolo (0,0). Baseline deve metterlo a (cellW/2, cellH-1) = (2, 3)
        using var atlas = new Image<Rgba32>(4, 4, new Rgba32(0, 0, 0, 0));
        atlas[0, 0] = new Rgba32(0, 255, 0, 255);

        var cells = new List<SpriteCell> { new("0", new AxisAlignedBox(0, 0, 4, 4)) };
        using var sheet = FrameSheet.ExtractFromAtlas(atlas, cells);

        var f = sheet.Frames[0];
        Assert.True(f.AlignToBaseline());

        using var composed = sheet.Compose();
        Assert.Equal(255, composed[2, 3].G);
    }

    [Fact]
    public void Reset_RestoresOriginalPosition()
    {
        using var atlas = new Image<Rgba32>(4, 4, new Rgba32(0, 0, 0, 0));
        atlas[0, 0] = new Rgba32(0, 0, 255, 255);

        var cells = new List<SpriteCell> { new("0", new AxisAlignedBox(0, 0, 4, 4)) };
        using var sheet = FrameSheet.ExtractFromAtlas(atlas, cells);
        var f = sheet.Frames[0];
        f.AutoCenter();
        Assert.NotEqual(new Point(0, 0), f.Offset);

        f.Reset();
        Assert.Equal(new Point(0, 0), f.Offset);

        using var composed = sheet.Compose();
        Assert.Equal(255, composed[0, 0].B);
    }

    [Fact]
    public void Extract_WithPadding_CapturesOverflowFromAdjacentCells()
    {
        // Atlas 8×4. Cella di destra è (4..8, 0..4). Pixel "overflow" a (3, 2) — appartiene
        // visivamente alla cella destra ma sta nelle coordinate della sinistra.
        using var atlas = new Image<Rgba32>(8, 4, new Rgba32(0, 0, 0, 0));
        atlas[3, 2] = new Rgba32(255, 0, 255, 255); // magenta overflow

        var cells = new List<SpriteCell> { new("R", new AxisAlignedBox(4, 0, 8, 4)) };
        using var sheet = FrameSheet.ExtractFromAtlas(atlas, cells, padding: 2);

        // Il content è 8×8 (4+2*2). Il pixel (3,2) dell'atlas finisce in
        // contentCoord = (3 - 4 + 2, 2 - 0 + 2) = (1, 4)
        Assert.Equal(255, sheet.Frames[0].Content[1, 4].R);
        Assert.Equal(255, sheet.Frames[0].Content[1, 4].B);
    }
}

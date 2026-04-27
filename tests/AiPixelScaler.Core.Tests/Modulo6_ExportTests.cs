using AiPixelScaler.Core.Pipeline.Export;
using SixLabors.ImageSharp;
using SixLabors.ImageSharp.PixelFormats;

namespace AiPixelScaler.Core.Tests;

public class Modulo6_ExportTests
{
    [Fact]
    public void AtlasPacker_empty_input_returns_transparent_min_atlas()
    {
        var pack = AtlasPacker.PackRow(Array.Empty<(string id, Image<Rgba32> img)>());
        Assert.Equal(1, pack.Atlas.Width);
        Assert.Equal(1, pack.Atlas.Height);
        Assert.Empty(pack.Placements);
        Assert.Equal(0, pack.Atlas[0, 0].A);
    }

    [Fact]
    public void AtlasPacker_row_and_json()
    {
        var a = new Image<Rgba32>(2, 2, new Rgba32(255, 0, 0, 255));
        var b = new Image<Rgba32>(1, 2, new Rgba32(0, 255, 0, 255));
        var pack = AtlasPacker.PackRow(new[] { ("a", a), ("b", b) });
        Assert.Equal(3, pack.Atlas.Width);
        Assert.Equal(2, pack.Atlas.Height);
        var meta = new SpriteSheetMetadata
        {
            Cells = pack.Placements.Select(p => new SpriteCellEntry
            {
                Id = p.id, X = p.x, Y = p.y, Width = p.w, Height = p.h, PivotNdcX = 0.5, PivotNdcY = 0.5
            }).ToList()
        };
        var json = JsonExport.Serialize(meta);
        Assert.Contains("\"Id\": \"a\"", json);
    }
}

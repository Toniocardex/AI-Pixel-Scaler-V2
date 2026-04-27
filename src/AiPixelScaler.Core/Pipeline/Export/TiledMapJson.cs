using System.Text.Json;
using System.Text.Json.Serialization;
using AiPixelScaler.Core.Pipeline.Slicing;

namespace AiPixelScaler.Core.Pipeline.Export;

/// <summary>JSON compatibile Tiled 1.9+ da celle sprite reali o stub 1 tile.</summary>
public static class TiledMapJson
{
    private static readonly JsonSerializerOptions JsonOpts = new()
    {
        WriteIndented = true,
        DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingNull
    };

    /// <summary>
    /// Costruisce una mappa Tiled con tileset derivato dalle celle reali.
    /// Ogni cella diventa un tile; la mappa è larga <paramref name="mapCols"/> × <paramref name="mapRows"/> tile.
    /// </summary>
    public static string BuildFromCells(
        int mapCols, int mapRows,
        int tileW, int tileH,
        IReadOnlyList<SpriteCell> cells,
        string atlasImagePath = "atlas.png")
    {
        var tileCount = cells.Count;
        var atlasW = tileW * tileCount;
        var atlasH = tileH;

        var tileObjects = cells.Select((c, i) => new
        {
            id = i,
            x = c.BoundsInAtlas.MinX,
            y = c.BoundsInAtlas.MinY,
            width = c.BoundsInAtlas.Width,
            height = c.BoundsInAtlas.Height,
        }).ToArray();

        var data = Enumerable.Range(1, mapCols * mapRows).ToArray();

        var root = new
        {
            compressionlevel = -1,
            width = mapCols,
            height = mapRows,
            tilewidth = tileW,
            tileheight = tileH,
            type = "map",
            version = "1.10",
            tiledversion = "1.10.0",
            orientation = "orthogonal",
            layers = new object[]
            {
                new
                {
                    id = 1,
                    name = "Tile Layer 1",
                    type = "tilelayer",
                    width = mapCols,
                    height = mapRows,
                    data
                }
            },
            tilesets = new object[]
            {
                new
                {
                    firstgid = 1,
                    name = "atlas",
                    image = atlasImagePath,
                    imagewidth = atlasW,
                    imageheight = atlasH,
                    tilewidth = tileW,
                    tileheight = tileH,
                    tilecount = tileCount,
                    columns = tileCount,
                    tiles = tileObjects
                }
            }
        };
        return JsonSerializer.Serialize(root, JsonOpts);
    }

}

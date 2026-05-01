using AiPixelScaler.Core.Pipeline.Imaging;
using AiPixelScaler.Core.Pipeline.Tiling;
using SixLabors.ImageSharp;
using SixLabors.ImageSharp.PixelFormats;

namespace AiPixelScaler.Core.Tests;

public class SeamlessEdgeTests
{
    [Fact]
    public void MakeTileable_ProducesContinuousBoundaries()
    {
        // Gradiente su X: il salto originale tra colonna 0 e 15 è massimo.
        // Shift+Bayer attenua in media il salto lungo il boundary ripetuto (non riga per riga sul seam interno).
        using var src = new Image<Rgba32>(16, 16);
        for (var y = 0; y < 16; y++)
        for (var x = 0; x < 16; x++)
            src[x, y] = new Rgba32((byte)(255 - x * 16), 0, (byte)(x * 16), 255);

        var origJump = Math.Abs(src[15, 0].R - src[0, 0].R);
        Assert.True(origJump > 200, $"sanity: origine non tileable, salto {origJump}");

        using var tile = SeamlessEdge.MakeTileable(src, blendWidth: 8);

        var sumJump = 0;
        for (var y = 0; y < 16; y++)
            sumJump += Math.Abs(tile[15, y].R - tile[0, y].R);
        Assert.True(sumJump / 16.0 < origJump * 0.75,
            $"salto orizzontale medio {(sumJump / 16):F1} non migliora vs originale {origJump}");
    }

    [Fact]
    public void MakeTileable_UniformColor_HasZeroBoundaryJump()
    {
        using var src = new Image<Rgba32>(16, 16, new Rgba32(40, 90, 120, 255));
        using var tile = SeamlessEdge.MakeTileable(src, blendWidth: 4);
        for (var y = 0; y < 16; y++)
        {
            Assert.Equal(tile[0, y].R, tile[15, y].R);
            Assert.Equal(tile[0, y].G, tile[15, y].G);
            Assert.Equal(tile[0, y].B, tile[15, y].B);
        }
        for (var x = 0; x < 16; x++)
        {
            Assert.Equal(tile[x, 0].R, tile[x, 15].R);
            Assert.Equal(tile[x, 0].G, tile[x, 15].G);
            Assert.Equal(tile[x, 0].B, tile[x, 15].B);
        }
    }

    [Fact]
    public void MakeTileable_TooSmallImage_ReturnsCloneUnchanged()
    {
        using var src = new Image<Rgba32>(2, 2, new Rgba32(100, 200, 50, 255));
        using var tile = SeamlessEdge.MakeTileable(src);
        Assert.Equal(2, tile.Width);
        Assert.Equal(100, tile[0, 0].R);
    }

    [Fact]
    public void MakeTileable_PreservesPixelArtStyle_NoIntermediateColors()
    {
        // Solo 2 colori: rosso e blu. Dopo MakeTileable, NON devono essere introdotti
        // colori intermedi (es. viola da blending) — il dithering è binario.
        using var src = new Image<Rgba32>(16, 16);
        for (var y = 0; y < 16; y++)
        for (var x = 0; x < 16; x++)
            src[x, y] = x < 8
                ? new Rgba32(255, 0, 0, 255)
                : new Rgba32(0, 0, 255, 255);

        using var tile = SeamlessEdge.MakeTileable(src, blendWidth: 4);

        for (var y = 0; y < 16; y++)
        for (var x = 0; x < 16; x++)
        {
            var p = tile[x, y];
            // Solo (255,0,0,255) o (0,0,255,255) ammessi
            var isRed  = p.R == 255 && p.G == 0 && p.B == 0;
            var isBlue = p.R == 0   && p.G == 0 && p.B == 255;
            Assert.True(isRed || isBlue,
                $"Pixel intermedio a ({x},{y}): ({p.R},{p.G},{p.B}) — atteso solo rosso o blu");
        }
    }
}

public class PalettePresetsTests
{
    [Theory]
    [InlineData(PalettePresets.Preset.GameBoyDMG, 4)]
    [InlineData(PalettePresets.Preset.CGA4,       4)]
    [InlineData(PalettePresets.Preset.Pico8,      16)]
    [InlineData(PalettePresets.Preset.NES16,      16)]
    [InlineData(PalettePresets.Preset.Sweetie16,  16)]
    public void Get_ReturnsCorrectColorCount(PalettePresets.Preset p, int expected)
    {
        var palette = PalettePresets.Get(p);
        Assert.Equal(expected, palette.Count);
    }

    [Fact]
    public void GameBoyDMG_AllGreens()
    {
        // I 4 colori del Game Boy originale sono tutti tonalità di verde-oliva
        foreach (var c in PalettePresets.GameBoyDMG)
        {
            // Per il GB DMG: G dominante o ≥ R, e B basso
            Assert.True(c.G >= c.R - 1, $"colore {c} non ha G dominante");
            Assert.True(c.B <= 0x40, $"colore {c} ha blu troppo alto per GB");
            Assert.Equal((byte)255, c.A);
        }
    }

    [Fact]
    public void CleanAI_ReturnsEmptyToSignalAdaptive()
    {
        // CleanAI = quantizzazione adattiva via Wu (no preset fisso)
        var palette = PalettePresets.Get(PalettePresets.Preset.CleanAI);
        Assert.Empty(palette);
    }

    [Fact]
    public void AllPresets_HaveOpaqueColors()
    {
        foreach (PalettePresets.Preset p in Enum.GetValues<PalettePresets.Preset>())
        {
            foreach (var c in PalettePresets.Get(p))
                Assert.Equal((byte)255, c.A);
        }
    }
}

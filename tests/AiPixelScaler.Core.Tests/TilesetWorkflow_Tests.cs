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
        // 16×16 con gradiente: difference media fra colonne adiacenti = 16 (255/16).
        // Dopo MakeTileable, il "salto" fra ultimo pixel e primo pixel del tile (boundary
        // della ripetizione) deve essere COMPATIBILE con la stessa scala del gradient
        // (≤ 2× la diff media), non un salto da 255 a 0.
        using var src = new Image<Rgba32>(16, 16);
        for (var y = 0; y < 16; y++)
        for (var x = 0; x < 16; x++)
            src[x, y] = new Rgba32((byte)(255 - x * 16), 0, (byte)(x * 16), 255);

        // Salto al boundary nell'originale: |src[15] − src[0]| → quasi 255 (NON tileable)
        var origJump = Math.Abs(src[15, 0].R - src[0, 0].R);
        Assert.True(origJump > 200, $"sanity: origine non tileable, salto {origJump}");

        using var tile = SeamlessEdge.MakeTileable(src, blendWidth: 4);

        // Salto orizzontale post: deve essere ≤ ~32 (paragonabile a transizione regolare ×2)
        for (var y = 0; y < 16; y++)
        {
            var jumpH = Math.Abs(tile[15, y].R - tile[0, y].R);
            Assert.True(jumpH <= 40, $"y={y}: salto orizzontale {jumpH} troppo grande dopo MakeTileable");
        }
        // Verticale: sui pixel FUORI dalla banda di heal orizzontale (x<4 o x≥12) la
        // continuità deve essere perfetta (l'immagine sorgente non ha variazione Y).
        for (var x = 0; x < 4; x++)
        {
            var jumpV = Math.Abs(tile[x, 15].B - tile[x, 0].B);
            Assert.True(jumpV <= 4, $"x={x}: salto verticale {jumpV} fuori dalla banda heal");
        }
        for (var x = 12; x < 16; x++)
        {
            var jumpV = Math.Abs(tile[x, 15].B - tile[x, 0].B);
            Assert.True(jumpV <= 4, $"x={x}: salto verticale {jumpV} fuori dalla banda heal");
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
        // CleanAI = quantizzazione adattiva via K-Means (no preset fisso)
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

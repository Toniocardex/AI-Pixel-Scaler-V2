using AiPixelScaler.Core.Pipeline.Imaging;
using SixLabors.ImageSharp;
using SixLabors.ImageSharp.PixelFormats;

namespace AiPixelScaler.Core.Tests;

public class AiCleanupWizardTests
{
    [Fact]
    public void Apply_RemovesGreenScreenAndBinarizesAlpha()
    {
        // 4×4 immagine: 2×2 sprite rosso al centro su sfondo verde
        using var img = new Image<Rgba32>(4, 4, new Rgba32(0, 255, 0, 255));
        img[1, 1] = new Rgba32(255, 0, 0, 255);
        img[2, 1] = new Rgba32(255, 0, 0, 255);
        img[1, 2] = new Rgba32(255, 0, 0, 255);
        img[2, 2] = new Rgba32(255, 0, 0, 255);

        var report = AiCleanupWizard.Apply(img, new AiCleanupWizard.Options
        {
            BgKey = new Rgba32(0, 255, 0, 255),
            BgTolerance = 5,
            DefringeEdges  = false,
            DenoiseSpike   = false,  // median 3×3 rovinerebbe uno sprite 2×2 in una scena 4×4
            DenoiseIslands = false,
        });

        // Sfondo deve essere rimosso, sprite invariato
        Assert.Equal(0, img[0, 0].A);
        Assert.Equal(255, img[1, 1].A);
        Assert.Equal(255, img[1, 1].R);
        Assert.NotEmpty(report.Steps);
    }

    [Fact]
    public void Apply_AllStepsDisabled_DoesNothing()
    {
        using var img = new Image<Rgba32>(3, 3, new Rgba32(120, 80, 200, 200));

        var report = AiCleanupWizard.Apply(img, new AiCleanupWizard.Options
        {
            RemoveBgColor   = false,
            DefringeEdges   = false,
            BinarizeAlpha   = false,
            DenoiseSpike    = false,
            DenoiseIslands  = false,
            ReducePalette   = false,
        });

        Assert.Empty(report.Steps);
        Assert.Equal(120, img[1, 1].R);
        Assert.Equal(200, img[1, 1].A);
    }

    [Fact]
    public void Apply_AlphaBinarization_OnlyKeepsHighAlphaPixels()
    {
        using var img = new Image<Rgba32>(2, 2);
        img[0, 0] = new Rgba32(255, 0, 0, 200); // sopra soglia
        img[1, 0] = new Rgba32(255, 0, 0, 100); // sotto soglia
        img[0, 1] = new Rgba32(255, 0, 0, 128); // = soglia → trasparente
        img[1, 1] = new Rgba32(255, 0, 0, 255); // pieno

        AiCleanupWizard.Apply(img, new AiCleanupWizard.Options
        {
            RemoveBgColor   = false,
            DefringeEdges   = false,
            BinarizeAlpha   = true,
            AlphaThreshold  = 128,
            DenoiseSpike    = false,
            DenoiseIslands  = false,
        });

        Assert.Equal(255, img[0, 0].A);
        Assert.Equal(0,   img[1, 0].A);
        Assert.Equal(0,   img[0, 1].A);
        Assert.Equal(255, img[1, 1].A);
    }

    [Fact]
    public void Apply_PaletteReduction_LimitsUniqueColors()
    {
        // 8×8 con 16 colori distinti opachi
        using var img = new Image<Rgba32>(8, 8);
        for (var y = 0; y < 8; y++)
        for (var x = 0; x < 8; x++)
            img[x, y] = new Rgba32((byte)(x * 32), (byte)(y * 32), 128, 255);

        AiCleanupWizard.Apply(img, new AiCleanupWizard.Options
        {
            RemoveBgColor   = false,
            DefringeEdges   = false,
            BinarizeAlpha   = false,
            DenoiseSpike    = false,
            DenoiseIslands  = false,
            ReducePalette   = true,
            PaletteColors   = 4,
        });

        var unique = new HashSet<uint>();
        for (var y = 0; y < 8; y++)
        for (var x = 0; x < 8; x++)
        {
            var p = img[x, y];
            unique.Add(((uint)p.R << 16) | ((uint)p.G << 8) | p.B);
        }
        Assert.True(unique.Count <= 4, $"Atteso ≤ 4 colori, trovati {unique.Count}");
    }

    [Fact]
    public void Apply_DenoiseIslands_RemovesSmallBlobs()
    {
        // 5×5 trasparente con un singolo pixel rosso isolato (area=1)
        using var img = new Image<Rgba32>(5, 5, new Rgba32(0, 0, 0, 0));
        img[2, 2] = new Rgba32(255, 0, 0, 255);

        AiCleanupWizard.Apply(img, new AiCleanupWizard.Options
        {
            RemoveBgColor   = false,
            DefringeEdges   = false,
            BinarizeAlpha   = false,
            DenoiseSpike    = false,
            DenoiseIslands  = true,
            IslandMinSize   = 4,
        });

        // Il pixel isolato (area=1 < min=4) deve essere stato rimosso
        Assert.Equal(0, img[2, 2].A);
    }
}

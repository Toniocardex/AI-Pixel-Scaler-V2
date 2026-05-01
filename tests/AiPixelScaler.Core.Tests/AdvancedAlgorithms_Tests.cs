using AiPixelScaler.Core.Color;
using AiPixelScaler.Core.Geometry;
using AiPixelScaler.Core.Pipeline.Imaging;
using SixLabors.ImageSharp;
using SixLabors.ImageSharp.PixelFormats;

namespace AiPixelScaler.Core.Tests;

// ─── Oklab ───────────────────────────────────────────────────────────────────

public class OklabTests
{
    [Fact]
    public void Roundtrip_Srgb_ToOklab_BackToSrgb_PreservesColor()
    {
        // sRGB → Oklab → sRGB. Tolleranza ±5 byte: la cbrt amplifica gli errori
        // float32 sui canali con LMS molto basso (blu scuri).
        // Per la nostra app la roundtrip non è usata direttamente — Oklab serve
        // per confronto di distanze, dove tale precisione è ampiamente sufficiente.
        foreach (var (r, g, b) in new[] {
            (0,0,0), (255,255,255), (128,128,128),
            (255,0,0), (0,255,0), (0,0,255),
            (200,100,50), (33,77,222), (12,34,56) })
        {
            var src  = new Rgba32((byte)r, (byte)g, (byte)b, 255);
            var lab  = Oklab.FromSrgb(src);
            var back = lab.ToSrgb();
            Assert.InRange(Math.Abs(back.R - src.R), 0, 5);
            Assert.InRange(Math.Abs(back.G - src.G), 0, 5);
            Assert.InRange(Math.Abs(back.B - src.B), 0, 5);
        }
    }

    [Fact]
    public void Distance_Between_Black_And_White_IsLargest()
    {
        var black = Oklab.FromSrgb(new Rgba32(0, 0, 0, 255));
        var white = Oklab.FromSrgb(new Rgba32(255, 255, 255, 255));
        var grey  = Oklab.FromSrgb(new Rgba32(128, 128, 128, 255));
        Assert.True(Oklab.Distance(black, white) > Oklab.Distance(black, grey));
        Assert.True(Oklab.Distance(black, white) > Oklab.Distance(grey, white));
    }

    [Fact]
    public void Distance_BetweenIdenticalColors_IsZero()
    {
        var a = Oklab.FromSrgb(new Rgba32(123, 45, 67, 255));
        var b = Oklab.FromSrgb(new Rgba32(123, 45, 67, 255));
        Assert.Equal(0f, Oklab.Distance(a, b), precision: 4);
    }
}

// ─── Defringe ────────────────────────────────────────────────────────────────

public class DefringeTests
{
    [Fact]
    public void RemoveBackgroundBleed_RecoversForegroundColor()
    {
        // Pixel "osservato" = α·fg + (1-α)·bg.  fg=(200,50,50), bg=(0,255,0), α=0.5
        // C_obs = 0.5·(200,50,50) + 0.5·(0,255,0) = (100, 152.5, 25)
        using var img = new Image<Rgba32>(1, 1, new Rgba32(100, 152, 25, 128));
        Defringe.RemoveBackgroundBleed(img, new Rgba32(0, 255, 0, 0));
        var p = img[0, 0];
        Assert.InRange(p.R, 195, 205);
        Assert.InRange(p.G, 45, 55);
        Assert.InRange(p.B, 45, 55);
        Assert.Equal((byte)128, p.A);
    }

    [Fact]
    public void OpaquePixels_AreUntouched()
    {
        using var img = new Image<Rgba32>(1, 1, new Rgba32(33, 44, 55, 255));
        Defringe.RemoveBackgroundBleed(img, new Rgba32(0, 255, 0, 0));
        Assert.Equal(new Rgba32(33, 44, 55, 255), img[0, 0]);
    }

    [Fact]
    public void TransparentPixels_AreUntouched()
    {
        using var img = new Image<Rgba32>(1, 1, new Rgba32(33, 44, 55, 0));
        Defringe.RemoveBackgroundBleed(img, new Rgba32(0, 255, 0, 0));
        Assert.Equal((byte)0, img[0, 0].A);
    }
}

// ─── MedianFilter ────────────────────────────────────────────────────────────

public class MedianFilterTests
{
    [Fact]
    public void RemovesSingleSaltPepperNoise()
    {
        // Sfondo grigio uniforme + 1 pixel "rumoroso" al centro
        using var img = new Image<Rgba32>(5, 5, new Rgba32(100, 100, 100, 255));
        img[2, 2] = new Rgba32(255, 0, 0, 255);  // outlier rosso
        MedianFilter.ApplyInPlace(img);
        // Il rosso deve essere stato sovrascritto dalla mediana dell'intorno (= 100)
        Assert.Equal(100, img[2, 2].R);
        Assert.Equal(100, img[2, 2].G);
    }

    [Fact]
    public void PreservesEdges()
    {
        // Edge verticale netto: metà sx nera, metà dx bianca
        using var img = new Image<Rgba32>(6, 6);
        for (var y = 0; y < 6; y++)
        for (var x = 0; x < 6; x++)
            img[x, y] = x < 3 ? new Rgba32(0, 0, 0, 255) : new Rgba32(255, 255, 255, 255);
        MedianFilter.ApplyInPlace(img);
        // L'edge x=2 deve ancora essere nero, x=3 ancora bianco
        Assert.Equal((byte)0,   img[2, 3].R);
        Assert.Equal((byte)255, img[3, 3].R);
    }
}

// ─── PaletteExtractor + Mapper ───────────────────────────────────────────────

public class PaletteTests
{
    [Fact]
    public void Extract_FromTwoColorImage_Returns2DistinctColors()
    {
        using var img = new Image<Rgba32>(10, 10);
        for (var y = 0; y < 10; y++)
        for (var x = 0; x < 10; x++)
            img[x, y] = x < 5 ? new Rgba32(255, 0, 0, 255) : new Rgba32(0, 0, 255, 255);
        var palette = PaletteExtractorAlgorithms.ExtractWu(img, 2);
        Assert.Equal(2, palette.Count);
        // entrambi i colori devono essere "vicini" a uno dei due originali
        var hasRed  = palette.Any(c => c.R > 200 && c.G < 50  && c.B < 50);
        var hasBlue = palette.Any(c => c.R < 50  && c.G < 50  && c.B > 200);
        Assert.True(hasRed);
        Assert.True(hasBlue);
    }

    [Fact]
    public void Mapper_NoDither_QuantizesToNearestPalette()
    {
        using var img = new Image<Rgba32>(1, 1, new Rgba32(120, 200, 80, 255));
        var palette = new[] { new Rgba32(0, 0, 0, 255), new Rgba32(100, 200, 100, 255) };
        PaletteMapper.ApplyInPlace(img, palette, PaletteMapper.DitherMode.None);
        // Il pixel originale è più vicino al verde
        Assert.Equal((byte)100, img[0, 0].R);
        Assert.Equal((byte)200, img[0, 0].G);
        Assert.Equal((byte)100, img[0, 0].B);
    }
}

// ─── AlphaThreshold Hysteresis ───────────────────────────────────────────────

public class AlphaThresholdHysteresisTests
{
    [Fact]
    public void StrongPixel_BecomesOpaque()
    {
        using var img = new Image<Rgba32>(1, 1, new Rgba32(0, 0, 0, 200));
        AlphaThreshold.ApplyHysteresis(img, tLow: 64, tHigh: 160);
        Assert.Equal((byte)255, img[0, 0].A);
    }

    [Fact]
    public void WeakPixel_BecomesTransparent()
    {
        using var img = new Image<Rgba32>(1, 1, new Rgba32(0, 0, 0, 30));
        AlphaThreshold.ApplyHysteresis(img, tLow: 64, tHigh: 160);
        Assert.Equal((byte)0, img[0, 0].A);
    }

    [Fact]
    public void AmbiguousPixel_ConnectedToStrong_BecomesOpaque()
    {
        using var img = new Image<Rgba32>(2, 1);
        img[0, 0] = new Rgba32(0, 0, 0, 200);  // strong
        img[1, 0] = new Rgba32(0, 0, 0, 100);  // ambiguous, vicino a strong
        AlphaThreshold.ApplyHysteresis(img, tLow: 64, tHigh: 160);
        Assert.Equal((byte)255, img[0, 0].A);
        Assert.Equal((byte)255, img[1, 0].A);
    }

    [Fact]
    public void IsolatedAmbiguous_BecomesTransparent()
    {
        using var img = new Image<Rgba32>(3, 1);
        img[0, 0] = new Rgba32(0, 0, 0, 30);   // weak
        img[1, 0] = new Rgba32(0, 0, 0, 100);  // ambiguous, ma non connesso a strong
        img[2, 0] = new Rgba32(0, 0, 0, 30);   // weak
        AlphaThreshold.ApplyHysteresis(img, tLow: 64, tHigh: 160);
        Assert.Equal((byte)0, img[1, 0].A);
    }
}

// ─── MagicWand ───────────────────────────────────────────────────────────────

// ─── Outline 8-conn ──────────────────────────────────────────────────────────

public class OutlineTests
{
    [Fact]
    public void Outline_FourConn_IgnoresDiagonalNeighbor()
    {
        using var img = new Image<Rgba32>(3, 3);
        img[0, 0] = new Rgba32(255, 0, 0, 255);
        Outline1Px.ApplyOuterInPlace(img, new Rgba32(0, 0, 0, 255), PixelConnectivity.Four);
        // (1,1) è diagonale → NON deve essere outline
        Assert.Equal((byte)0, img[1, 1].A);
    }

    [Fact]
    public void Outline_EightConn_IncludesDiagonalNeighbor()
    {
        using var img = new Image<Rgba32>(3, 3);
        img[0, 0] = new Rgba32(255, 0, 0, 255);
        Outline1Px.ApplyOuterInPlace(img, new Rgba32(0, 0, 0, 255), PixelConnectivity.Eight);
        Assert.Equal((byte)255, img[1, 1].A);
    }
}

// ─── AutoPad ─────────────────────────────────────────────────────────────────

public class AutoPadTests
{
    [Fact]
    public void Apply_AddsTransparentBorder()
    {
        using var src = new Image<Rgba32>(2, 2, new Rgba32(255, 0, 0, 255));
        using var dst = AutoPad.Apply(src, 1);
        Assert.Equal(4, dst.Width);
        Assert.Equal(4, dst.Height);
        Assert.Equal((byte)0,   dst[0, 0].A);  // bordo trasparente
        Assert.Equal((byte)255, dst[1, 1].A);  // contenuto preservato
    }

    [Fact]
    public void PadToMultiple_ExpandsToNextMultiple()
    {
        using var src = new Image<Rgba32>(10, 6);
        using var dst = AutoPad.PadToMultiple(src, 8);
        Assert.Equal(16, dst.Width);   // 10 → 16
        Assert.Equal(8,  dst.Height);  // 6  → 8
    }
}

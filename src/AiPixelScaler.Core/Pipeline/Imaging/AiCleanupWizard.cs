using AiPixelScaler.Core.Geometry;
using SixLabors.ImageSharp;
using SixLabors.ImageSharp.PixelFormats;

namespace AiPixelScaler.Core.Pipeline.Imaging;

/// <summary>
/// Orchestratore one-click per la pulizia di sprite generati da IA.
/// Esegue in ordine deterministico:
///
///   1. <see cref="EdgeBackgroundFill"/>      — flood-fill dai bordi del bg color (BFS, ΔRGB Euclidea)
///   2. <see cref="Defringe.FromOpaqueNeighbors"/> — color decontamination dai vicini opachi
///   3. <see cref="AlphaThreshold"/>          — binarizza alpha (rimuove anti-aliasing IA)
///   4. <see cref="MedianFilter"/>            — denoise 3×3 mediano (preserva i bordi)
///   5. <see cref="IslandDenoise"/>           — drop blob isolati (CCL 8-connessa)
///   6. <see cref="PaletteExtractor"/> + <see cref="PaletteMapper"/> — riduzione palette in OKLab (opzionale)
///
/// Quest'ordine è critico: il defringe DEVE precedere la binarizzazione (lavora sui pixel
/// semi-trasparenti); il denoise spike DEVE precedere il drop blob (i pixel "salt&amp;pepper"
/// fanno aumentare la dimensione apparente delle isole).
/// </summary>
public static class AiCleanupWizard
{
    public record Options
    {
        // ── 1. Background color removal ──────────────────────────────────
        public bool   RemoveBgColor { get; init; } = true;
        public Rgba32 BgKey         { get; init; } = new(0, 255, 0, 255);
        public int    BgTolerance   { get; init; } = 40;

        // ── 2. Defringe ──────────────────────────────────────────────────
        public bool DefringeEdges  { get; init; } = true;
        public byte DefringeOpaque { get; init; } = 250;

        // ── 3. Alpha binarization ─────────────────────────────────────────
        public bool BinarizeAlpha  { get; init; } = true;
        public byte AlphaThreshold { get; init; } = 128;

        // ── 4. Median 3×3 ─────────────────────────────────────────────────
        public bool DenoiseSpike   { get; init; } = true;

        // ── 5. Island denoise ─────────────────────────────────────────────
        public bool DenoiseIslands { get; init; } = true;
        public int  IslandMinSize  { get; init; } = 4;

        // ── 6. Palette reduction ──────────────────────────────────────────
        public bool ReducePalette  { get; init; } = false;
        public int  PaletteColors  { get; init; } = 16;
        public bool PaletteDither  { get; init; } = false;
    }

    public sealed class Report
    {
        public List<string> Steps { get; } = new();
        public override string ToString() => string.Join(" → ", Steps);
    }

    public static Report Apply(Image<Rgba32> image, Options o)
    {
        var report = new Report();

        if (o.RemoveBgColor)
        {
            EdgeBackgroundFill.ApplyInPlace(image, o.BgKey, o.BgTolerance);
            report.Steps.Add($"sfondo (tol {o.BgTolerance})");
        }

        if (o.DefringeEdges)
        {
            Defringe.FromOpaqueNeighbors(image, o.DefringeOpaque);
            report.Steps.Add($"defringe (opaque ≥ {o.DefringeOpaque})");
        }

        if (o.BinarizeAlpha)
        {
            PipelineSharedStages.ApplyAlphaThreshold(image, o.AlphaThreshold);
            report.Steps.Add($"alpha→{o.AlphaThreshold}");
        }

        if (o.DenoiseSpike)
        {
            MedianFilter.ApplyInPlace(image);
            report.Steps.Add("median 3×3");
        }

        if (o.DenoiseIslands)
        {
            PipelineSharedStages.ApplyIslandDenoise(image, o.IslandMinSize, PixelConnectivity.Eight);
            report.Steps.Add($"isole < {o.IslandMinSize}");
        }

        if (o.ReducePalette)
        {
            var n = Math.Clamp(o.PaletteColors, 2, 256);
            var palette = PipelineSharedStages.ApplyPaletteReduction(
                image,
                n,
                o.PaletteDither ? PaletteMapper.DitherMode.FloydSteinberg : PaletteMapper.DitherMode.None,
                PixelArtProcessor.QuantizerKind.KMeansOklab);
            if (palette.Count > 0)
                report.Steps.Add($"palette {palette.Count}{(o.PaletteDither ? " +dither" : "")}");
        }

        return report;
    }
}

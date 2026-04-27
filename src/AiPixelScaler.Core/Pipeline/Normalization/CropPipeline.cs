using AiPixelScaler.Core.Geometry;
using AiPixelScaler.Core.Pipeline.Imaging;
using SixLabors.ImageSharp;
using SixLabors.ImageSharp.PixelFormats;

namespace AiPixelScaler.Core.Pipeline.Normalization;

/// <summary>
/// Pipeline di crop / normalizzazione asset con tre modalità ESPLICITE
/// (mai mescolate in silenzio):
///
///   • <see cref="CropMode.TrimToContent"/>        — AABB su soglia α (no padding)
///   • <see cref="CropMode.UserRoi"/>              — rettangolo utente (solo clamp ai bordi)
///   • <see cref="CropMode.TrimToContentPadded"/>  — AABB + padding pixel uniforme
///
/// Ordine fisso (rispettato sempre):
///   1. (esterno) denoise / island filter — opzionale, NON eseguito qui
///   2. determinazione box di lavoro (AABB±soglia±padding oppure ROI)
///   3. clamp ai bordi atlas
///   4. <see cref="AtlasCropper.Crop"/>
///   5. POT (se abilitata) DOPO il padding — non in mezzo
///
/// POT policies:
///   • <see cref="PotPolicy.None"/>     — dimensioni naturali
///   • <see cref="PotPolicy.PerAxis"/>  — W' = NextPow2(W), H' = NextPow2(H)
///   • <see cref="PotPolicy.Square"/>   — side = NextPow2(max(W,H)), output quadrato
///
/// Esempio: trim porta a 192×100 → POT PerAxis → 256×128 ; POT Square → 256×256.
/// 1024×768 → PerAxis → 1024×1024 (1024 invariato, 768 → 1024).
/// </summary>
public static class CropPipeline
{
    public enum CropMode
    {
        TrimToContent,
        UserRoi,
        TrimToContentPadded,
    }

    public enum PotPolicy
    {
        None,
        PerAxis,
        Square,
    }

    public sealed record Options
    {
        public CropMode         Mode             { get; init; } = CropMode.TrimToContent;
        public byte             AlphaThreshold   { get; init; } = 1;        // pixel "visibile" se α ≥ soglia
        public int              PaddingPx        { get; init; } = 0;
        public PotPolicy        Pot              { get; init; } = PotPolicy.None;
        public AxisAlignedBox?  UserRoi          { get; init; } = null;     // richiesto se Mode == UserRoi
    }

    public sealed class Result : IDisposable
    {
        public required Image<Rgba32>    Image       { get; init; }
        /// <summary>Box effettivo nel sorgente prima dell'eventuale POT-pad.</summary>
        public required AxisAlignedBox   CropBox     { get; init; }
        public required int              FinalW      { get; init; }
        public required int              FinalH      { get; init; }
        public bool                      PotApplied  { get; init; }
        public string                    Description { get; init; } = "";
        public void Dispose() => Image.Dispose();
    }

    public static Result Apply(Image<Rgba32> source, Options opts)
    {
        ArgumentNullException.ThrowIfNull(source);
        ArgumentNullException.ThrowIfNull(opts);
        if (source.Width == 0 || source.Height == 0)
            return MakeEmptyResult(opts, "sorgente vuota");

        // ── 1. Determina box di lavoro ────────────────────────────────────────
        AxisAlignedBox box;
        switch (opts.Mode)
        {
            case CropMode.UserRoi:
                if (opts.UserRoi is null || opts.UserRoi.Value.IsEmpty)
                    return MakeEmptyResult(opts, "ROI mancante o vuota");
                box = ClampBox(opts.UserRoi.Value, source.Width, source.Height);
                break;

            case CropMode.TrimToContent:
            case CropMode.TrimToContentPadded:
                var aabb = AlphaBoundingBox.Compute(source, opts.AlphaThreshold);
                if (aabb.IsEmpty)
                    return MakeEmptyResult(opts, $"nessun pixel α ≥ {opts.AlphaThreshold}");
                if (opts.Mode == CropMode.TrimToContentPadded && opts.PaddingPx > 0)
                    aabb = ExpandBox(aabb, opts.PaddingPx);
                box = ClampBox(aabb, source.Width, source.Height);
                break;

            default:
                throw new ArgumentOutOfRangeException(nameof(opts), $"Mode sconosciuta: {opts.Mode}");
        }

        if (box.IsEmpty) return MakeEmptyResult(opts, "box di lavoro vuoto dopo clamp");

        // ── 2. Crop ───────────────────────────────────────────────────────────
        var cropped = AtlasCropper.Crop(source, in box);

        // ── 3. POT (DOPO padding) ─────────────────────────────────────────────
        var (potW, potH) = ComputePotSize(cropped.Width, cropped.Height, opts.Pot);
        var potApplied = potW != cropped.Width || potH != cropped.Height;
        if (potApplied)
        {
            // Pad alla dim POT con trasparenza (anchor top-left).
            // Si potrebbe anche centrare; per atlas è più comune top-left.
            var padded = AutoPad.Apply(cropped, 0, 0, potW - cropped.Width, potH - cropped.Height);
            cropped.Dispose();
            cropped = padded;
        }

        return new Result
        {
            Image       = cropped,
            CropBox     = box,
            FinalW      = cropped.Width,
            FinalH      = cropped.Height,
            PotApplied  = potApplied,
            Description = BuildDescription(opts, box, cropped.Width, cropped.Height, potApplied),
        };
    }

    // ─── Helpers ─────────────────────────────────────────────────────────────

    private static (int w, int h) ComputePotSize(int w, int h, PotPolicy pot) => pot switch
    {
        PotPolicy.None    => (w, h),
        PotPolicy.PerAxis => (SizingMath.NextPow2(w), SizingMath.NextPow2(h)),
        PotPolicy.Square  => SquareSize(w, h),
        _                 => (w, h),
    };

    private static (int w, int h) SquareSize(int w, int h)
    {
        var side = SizingMath.NextPow2(Math.Max(w, h));
        return (side, side);
    }

    private static AxisAlignedBox ExpandBox(AxisAlignedBox box, int padding) =>
        new(box.MinX - padding, box.MinY - padding,
            box.MaxX + padding, box.MaxY + padding);

    private static AxisAlignedBox ClampBox(AxisAlignedBox box, int imgW, int imgH)
    {
        var minX = Math.Max(0,    box.MinX);
        var minY = Math.Max(0,    box.MinY);
        var maxX = Math.Min(imgW, box.MaxX);
        var maxY = Math.Min(imgH, box.MaxY);
        return new AxisAlignedBox(minX, minY, maxX, maxY);
    }

    private static Result MakeEmptyResult(Options opts, string reason)
    {
        var img = new Image<Rgba32>(1, 1, new Rgba32(0, 0, 0, 0));
        return new Result
        {
            Image       = img,
            CropBox     = new AxisAlignedBox(0, 0, 1, 1),
            FinalW      = 1, FinalH = 1,
            PotApplied  = false,
            Description = $"crop no-op ({reason}) → 1×1 trasparente",
        };
    }

    private static string BuildDescription(Options opts, AxisAlignedBox box, int w, int h, bool pot)
    {
        var parts = new List<string>(4);
        parts.Add(opts.Mode switch
        {
            CropMode.TrimToContent       => $"trim α≥{opts.AlphaThreshold}",
            CropMode.UserRoi             => "ROI utente",
            CropMode.TrimToContentPadded => $"trim α≥{opts.AlphaThreshold} +pad {opts.PaddingPx}px",
            _                            => "?",
        });
        parts.Add($"box {box.Width}×{box.Height}");
        if (pot) parts.Add(opts.Pot == PotPolicy.Square ? $"POT square→{w}×{h}" : $"POT axis→{w}×{h}");
        return string.Join(" · ", parts);
    }
}

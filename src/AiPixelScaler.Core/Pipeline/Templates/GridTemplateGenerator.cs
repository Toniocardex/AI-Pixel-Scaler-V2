using SixLabors.ImageSharp;
using SixLabors.ImageSharp.PixelFormats;

namespace AiPixelScaler.Core.Pipeline.Templates;

/// <summary>
/// Genera un'immagine PNG trasparente con griglia di riferimento per guidare
/// la generazione AI (ControlNet / IP-Adapter / inpainting).
///
/// Strati configurabili:
///   • Bordi celle  — rettangolo perimetrale colorato
///   • Pivot (croce + dot) — marcatore NDC posizionabile
///   • Baseline line — linea orizzontale alla Y del pivot
///   • Indici cella — numero di frame in alto a sinistra (pixel-font 3×5)
///   • Tinta alternata — fill semitrasparente per distinguere celle
/// </summary>
public static class GridTemplateGenerator
{
    // ─── 9 pivot presets (riga × col, 0..2) ──────────────────────────────────
    public enum PivotPreset
    {
        TopLeft,    TopCenter,    TopRight,
        MidLeft,    Center,       MidRight,
        BottomLeft, BottomCenter, BottomRight,
        Custom
    }

    public static (double ndcX, double ndcY) PivotNdc(PivotPreset p) => p switch
    {
        PivotPreset.TopLeft      => (0.0, 0.0),
        PivotPreset.TopCenter    => (0.5, 0.0),
        PivotPreset.TopRight     => (1.0, 0.0),
        PivotPreset.MidLeft      => (0.0, 0.5),
        PivotPreset.Center       => (0.5, 0.5),
        PivotPreset.MidRight     => (1.0, 0.5),
        PivotPreset.BottomLeft   => (0.0, 1.0),
        PivotPreset.BottomCenter => (0.5, 1.0),
        PivotPreset.BottomRight  => (1.0, 1.0),
        _                        => (0.5, 0.5),
    };

    // ─── Options ──────────────────────────────────────────────────────────────

    public record Options
    {
        public int Rows      { get; init; } = 4;
        public int Cols      { get; init; } = 4;
        public int CellWidth { get; init; } = 64;
        public int CellHeight{ get; init; } = 64;

        // Bordo
        public bool   ShowBorder      { get; init; } = true;
        public Rgba32 BorderColor     { get; init; } = new(60, 220, 255, 200);
        public int    BorderThickness { get; init; } = 1;

        // Pivot
        public bool        ShowPivotMarker   { get; init; } = true;
        public PivotPreset Pivot             { get; init; } = PivotPreset.Center;
        /// <summary>Usato solo se Pivot == Custom.</summary>
        public double      PivotNdcX        { get; init; } = 0.5;
        public double      PivotNdcY        { get; init; } = 0.5;
        public Rgba32      PivotColor       { get; init; } = new(255, 210, 0, 230);
        public int         PivotCrosshairArm{ get; init; } = 6;
        public int         PivotDotRadius   { get; init; } = 2;

        // Baseline
        public bool   ShowBaselineLine { get; init; } = true;
        public Rgba32 BaselineColor    { get; init; } = new(255, 210, 0, 80);

        // Indici cella (pixel-font 3×5)
        public bool   ShowCellIndex  { get; init; } = true;
        public Rgba32 IndexColor     { get; init; } = new(255, 210, 0, 200);
        public int    IndexScale     { get; init; } = 1;   // 1 = 3×5 px, 2 = 6×10 px …

        // Tinta alternata
        public bool   ShowCellTint { get; init; } = false;
        public Rgba32 TintEven     { get; init; } = new(255, 255, 255, 10);
        public Rgba32 TintOdd      { get; init; } = new(100, 80, 255, 10);
    }

    // ─── Entry point ─────────────────────────────────────────────────────────

    public static Image<Rgba32> Generate(Options opts)
    {
        if (opts.Rows < 1)       throw new ArgumentOutOfRangeException(nameof(opts), "Rows < 1");
        if (opts.Cols < 1)       throw new ArgumentOutOfRangeException(nameof(opts), "Cols < 1");
        if (opts.CellWidth  < 4) throw new ArgumentOutOfRangeException(nameof(opts), "CellWidth < 4");
        if (opts.CellHeight < 4) throw new ArgumentOutOfRangeException(nameof(opts), "CellHeight < 4");

        var totalW = opts.Cols * opts.CellWidth;
        var totalH = opts.Rows * opts.CellHeight;
        var img    = new Image<Rgba32>(totalW, totalH, new Rgba32(0, 0, 0, 0));

        // Risolvi NDC pivot
        var (ndcX, ndcY) = opts.Pivot == PivotPreset.Custom
            ? (opts.PivotNdcX, opts.PivotNdcY)
            : PivotNdc(opts.Pivot);

        var thick = Math.Max(1, opts.BorderThickness);

        for (var row = 0; row < opts.Rows; row++)
        for (var col = 0; col < opts.Cols; col++)
        {
            var ox = col * opts.CellWidth;
            var oy = row * opts.CellHeight;
            var cw = opts.CellWidth;
            var ch = opts.CellHeight;

            // ── tinta ────────────────────────────────────────────────────────
            if (opts.ShowCellTint)
            {
                var tint = (row + col) % 2 == 0 ? opts.TintEven : opts.TintOdd;
                FillRect(img, ox, oy, cw, ch, tint);
            }

            // ── bordo ─────────────────────────────────────────────────────────
            if (opts.ShowBorder)
            {
                FillRect(img, ox,             oy,             cw,     thick, opts.BorderColor);
                FillRect(img, ox,             oy + ch - thick, cw,    thick, opts.BorderColor);
                FillRect(img, ox,             oy,             thick,  ch,    opts.BorderColor);
                FillRect(img, ox + cw - thick,oy,             thick,  ch,    opts.BorderColor);
            }

            // ── baseline ─────────────────────────────────────────────────────
            if (opts.ShowBaselineLine)
            {
                var by = oy + (int)Math.Round(ndcY * (ch - 1));
                by = Math.Clamp(by, oy, oy + ch - 1);
                for (var x = ox; x < ox + cw; x++)
                    BlendPixel(img, x, by, opts.BaselineColor);
            }

            // ── pivot marker ─────────────────────────────────────────────────
            if (opts.ShowPivotMarker)
            {
                var px = ox + (int)Math.Round(ndcX * (cw - 1));
                var py = oy + (int)Math.Round(ndcY * (ch - 1));
                px = Math.Clamp(px, ox, ox + cw - 1);
                py = Math.Clamp(py, oy, oy + ch - 1);

                var arm = opts.PivotCrosshairArm;
                for (var x = px - arm; x <= px + arm; x++)
                    if (x >= ox && x < ox + cw) BlendPixel(img, x, py, opts.PivotColor);
                for (var y = py - arm; y <= py + arm; y++)
                    if (y >= oy && y < oy + ch) BlendPixel(img, px, y, opts.PivotColor);

                var r = opts.PivotDotRadius;
                for (var dy = -r; dy <= r; dy++)
                for (var dx = -r; dx <= r; dx++)
                {
                    if (dx * dx + dy * dy > r * r) continue;
                    var nx = px + dx; var ny = py + dy;
                    if (nx >= ox && nx < ox + cw && ny >= oy && ny < oy + ch)
                        BlendPixel(img, nx, ny, opts.PivotColor);
                }
            }

            // ── indice cella ─────────────────────────────────────────────────
            if (opts.ShowCellIndex)
            {
                var idx = row * opts.Cols + col;
                var s   = Math.Max(1, opts.IndexScale);
                // posizione: 2px dal bordo in alto a sinistra
                DrawPixelNumber(img, idx, ox + 2, oy + 2, opts.IndexColor, s,
                                ox, oy, cw, ch);
            }
        }

        return img;
    }

    // ─── helpers ─────────────────────────────────────────────────────────────

    private static void FillRect(Image<Rgba32> img, int x, int y, int w, int h, Rgba32 color)
    {
        var x2 = Math.Min(img.Width,  x + w);
        var y2 = Math.Min(img.Height, y + h);
        var x1 = Math.Max(0, x);
        var y1 = Math.Max(0, y);
        for (var py = y1; py < y2; py++)
        for (var px = x1; px < x2; px++)
            BlendPixel(img, px, py, color);
    }

    /// <summary>Alpha-compositing "source over".</summary>
    private static void BlendPixel(Image<Rgba32> img, int x, int y, Rgba32 src)
    {
        if (src.A == 0) return;
        var dst = img[x, y];
        if (src.A == 255 || dst.A == 0) { img[x, y] = src; return; }
        var sa   = src.A / 255f;
        var da   = dst.A / 255f;
        var outA = sa + da * (1 - sa);
        if (outA < 1e-6f) { img[x, y] = new Rgba32(0, 0, 0, 0); return; }
        img[x, y] = new Rgba32(
            (byte)Math.Round((src.R * sa + dst.R * da * (1 - sa)) / outA),
            (byte)Math.Round((src.G * sa + dst.G * da * (1 - sa)) / outA),
            (byte)Math.Round((src.B * sa + dst.B * da * (1 - sa)) / outA),
            (byte)Math.Round(outA * 255));
    }

    // ─── Pixel-font 3×5 per cifre 0-9 ───────────────────────────────────────
    // Ogni cifra è encodata come 5 byte, ogni byte = 3 bit (bit2=col0, bit1=col1, bit0=col2)

    private static readonly byte[][] Glyphs = [
        [0b111, 0b101, 0b101, 0b101, 0b111], // 0
        [0b010, 0b110, 0b010, 0b010, 0b111], // 1
        [0b111, 0b001, 0b111, 0b100, 0b111], // 2
        [0b111, 0b001, 0b011, 0b001, 0b111], // 3
        [0b101, 0b101, 0b111, 0b001, 0b001], // 4
        [0b111, 0b100, 0b111, 0b001, 0b111], // 5
        [0b111, 0b100, 0b111, 0b101, 0b111], // 6
        [0b111, 0b001, 0b001, 0b001, 0b001], // 7
        [0b111, 0b101, 0b111, 0b101, 0b111], // 8
        [0b111, 0b101, 0b111, 0b001, 0b111], // 9
    ];

    private const int GlyphW = 3;
    private const int GlyphH = 5;
    private const int GlyphGap = 1; // gap fra cifre

    private static void DrawPixelNumber(
        Image<Rgba32> img, int number,
        int startX, int startY, Rgba32 color, int scale,
        int clipX, int clipY, int clipW, int clipH)
    {
        var digits = number == 0 ? [0] : ToDigits(number);
        var cx = startX;
        foreach (var d in digits)
        {
            DrawGlyph(img, Glyphs[d], cx, startY, color, scale, clipX, clipY, clipW, clipH);
            cx += (GlyphW + GlyphGap) * scale;
        }
    }

    private static int[] ToDigits(int n)
    {
        if (n == 0) return [0];
        var tmp = new List<int>();
        while (n > 0) { tmp.Add(n % 10); n /= 10; }
        tmp.Reverse();
        return [.. tmp];
    }

    private static void DrawGlyph(
        Image<Rgba32> img, byte[] glyph,
        int ox, int oy, Rgba32 color, int scale,
        int clipX, int clipY, int clipW, int clipH)
    {
        for (var row = 0; row < GlyphH; row++)
        for (var col = 0; col < GlyphW; col++)
        {
            if ((glyph[row] & (1 << (GlyphW - 1 - col))) == 0) continue;
            for (var sy = 0; sy < scale; sy++)
            for (var sx = 0; sx < scale; sx++)
            {
                var px = ox + col * scale + sx;
                var py = oy + row * scale + sy;
                if (px < clipX || py < clipY || px >= clipX + clipW || py >= clipY + clipH) continue;
                if (px < 0 || py < 0 || px >= img.Width || py >= img.Height) continue;
                BlendPixel(img, px, py, color);
            }
        }
    }
}

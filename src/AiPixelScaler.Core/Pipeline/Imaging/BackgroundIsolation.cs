using AiPixelScaler.Core.Color;
using SixLabors.ImageSharp;
using SixLabors.ImageSharp.PixelFormats;
using System.Collections.Generic;

namespace AiPixelScaler.Core.Pipeline.Imaging;

/// <summary>
/// Isola lo sfondo esterno senza rimuovere dettagli interni dello sprite:
/// flood fill non ricorsivo dai bordi immagine, vincolato da similarità colore
/// (Oklab percettivo) e opzionalmente bloccato da bordi Sobel.
///
/// Pipeline raccomandata:
///   1. <see cref="SampleRegionColor"/> — campiona colore dominante nell'area cliccata
///   2. <see cref="SnapKeyToBorderColor"/> — corregge il colore se Quantize lo ha rimappato
///   3. <see cref="ApplyInPlace"/> — flood fill dal bordo con protezione Sobel
///   4. <see cref="AlphaThreshold.ApplyInPlace"/> — binarizza pixel semi-trasparenti residui (opzionale)
///   5. <see cref="Defringe.FromOpaqueNeighbors"/> — decontamina fringe colorati (opzionale)
/// </summary>
public static class BackgroundIsolation
{
    public sealed record Options(
        Rgba32 BackgroundColor,
        double ColorTolerance = 8,
        double EdgeThreshold = 100,       // soglia Sobel: 100 ignora artefatti JPEG (<80), blocca bordi sprite (200-1440)
        bool UseOklab = true,
        bool ProtectStrongEdges = true,
        bool Use8Connectivity = true);    // include vicini diagonali → elimina angoli isolati

    // ─────────────────────────────────────────────────────────────────────────

    public static int ApplyInPlace(Image<Rgba32> image, Options options)
    {
        var w = image.Width;
        var h = image.Height;
        if (w < 1 || h < 1) return 0;

        var pixels = ImageUtils.ToFlatArray(image);
        var edge = options.ProtectStrongEdges
            ? BuildSobelEdgeMap(pixels, w, h, Math.Clamp(options.EdgeThreshold, 0, 4096))
            : new bool[w * h];
        var removed = new bool[w * h];
        var q = new Queue<int>();

        var rgbTol = Math.Max(0, options.ColorTolerance);
        var oklabKey = Oklab.FromSrgb(options.BackgroundColor);

        // ── Calibrazione Oklab percettiva ──────────────────────────────────────
        // Campiona 8 direzioni nello spazio RGB alla distanza tolInt:
        // 6 assi allineati (+/-R, +/-G, +/-B) + 2 diagonali principali.
        // Questo copre i casi limite per colori saturi dove uno shift su un canale
        // clipa a 0/255 e produce una distanza Oklab molto diversa dagli altri assi.
        var tolInt = (int)Math.Round(Math.Max(0, rgbTol));
        var oklabTolSq = 0.0;
        if (tolInt > 0)
        {
            var (cr, cg, cb) = (
                (int)options.BackgroundColor.R,
                (int)options.BackgroundColor.G,
                (int)options.BackgroundColor.B);

            // 6 assi
            oklabTolSq = MaxOklabDist(oklabKey, oklabTolSq, cr + tolInt, cg, cb);
            oklabTolSq = MaxOklabDist(oklabKey, oklabTolSq, cr - tolInt, cg, cb);
            oklabTolSq = MaxOklabDist(oklabKey, oklabTolSq, cr, cg + tolInt, cb);
            oklabTolSq = MaxOklabDist(oklabKey, oklabTolSq, cr, cg - tolInt, cb);
            oklabTolSq = MaxOklabDist(oklabKey, oklabTolSq, cr, cg, cb + tolInt);
            oklabTolSq = MaxOklabDist(oklabKey, oklabTolSq, cr, cg, cb - tolInt);
            // 2 diagonali (cattura colori dove un asse clipa e un altro domina)
            oklabTolSq = MaxOklabDist(oklabKey, oklabTolSq, cr + tolInt, cg + tolInt, cb + tolInt);
            oklabTolSq = MaxOklabDist(oklabKey, oklabTolSq, cr - tolInt, cg - tolInt, cb - tolInt);
        }
        // Fallback minimo per tol = 0 o tolInt = 0
        if (oklabTolSq <= 0.0)
            oklabTolSq = (rgbTol / 255.0) * (rgbTol / 255.0);

        // ── Helpers locali ────────────────────────────────────────────────────
        int I(int x, int y) => y * w + x;

        bool MatchesBackground(int idx)
        {
            var p = pixels[idx];
            if (p.A == 0) return true;
            if (options.UseOklab)
                return (double)Oklab.DistanceSquared(oklabKey, Oklab.FromSrgb(p)) <= oklabTolSq;

            var dr = p.R - options.BackgroundColor.R;
            var dg = p.G - options.BackgroundColor.G;
            var db = p.B - options.BackgroundColor.B;
            return dr * dr + dg * dg + db * db <= rgbTol * rgbTol;
        }

        // Un pixel è attraversabile se:
        //   a) già trasparente (run precedenti) → sempre passabile come corridoio
        //   b) corrisponde al colore sfondo E non è protetto da bordo forte Sobel
        bool CanFlood(int idx)
        {
            if (pixels[idx].A == 0) return true;         // trasparente → passabile
            return !edge[idx] && MatchesBackground(idx); // corrispondenza colore + no bordo forte
        }

        void Seed(int idx)
        {
            if (removed[idx] || !CanFlood(idx)) return;
            removed[idx] = true;
            q.Enqueue(idx);
        }

        // ── Seeding perimetro ─────────────────────────────────────────────────
        for (var x = 0; x < w; x++)
        {
            Seed(I(x, 0));
            if (h > 1) Seed(I(x, h - 1));
        }
        for (var y = 1; y < h - 1; y++)
        {
            Seed(I(0, y));
            if (w > 1) Seed(I(w - 1, y));
        }

        // ── Propagazione ─────────────────────────────────────────────────────
        void TryAdd(int x, int y)
        {
            if ((uint)x >= (uint)w || (uint)y >= (uint)h) return;
            var idx = I(x, y);
            if (removed[idx] || !CanFlood(idx)) return;
            removed[idx] = true;
            q.Enqueue(idx);
        }

        while (q.Count > 0)
        {
            var idx = q.Dequeue();
            var px = idx % w;
            var py = idx / w;

            TryAdd(px - 1, py);
            TryAdd(px + 1, py);
            TryAdd(px, py - 1);
            TryAdd(px, py + 1);

            if (options.Use8Connectivity)
            {
                TryAdd(px - 1, py - 1);
                TryAdd(px + 1, py - 1);
                TryAdd(px - 1, py + 1);
                TryAdd(px + 1, py + 1);
            }
        }

        // ── Azzeramento pixel opachi rimossi ─────────────────────────────────
        // Conta solo pixel originalmente opachi; quelli già trasparenti da run
        // precedenti erano passabili come corridoi ma non vanno conteggiati.
        var removedCount = 0;
        image.ProcessPixelRows(accessor =>
        {
            for (var y = 0; y < accessor.Height; y++)
            {
                var row = accessor.GetRowSpan(y);
                for (var x = 0; x < row.Length; x++)
                {
                    var idx = I(x, y);
                    if (!removed[idx] || pixels[idx].A == 0) continue;
                    row[x] = new Rgba32(row[x].R, row[x].G, row[x].B, 0);
                    removedCount++;
                }
            }
        });
        return removedCount;
    }

    // ── Helper calibrazione ────────────────────────────────────────────────────
    private static double MaxOklabDist(in Oklab key, double current, int r, int g, int b)
    {
        var sample = Oklab.FromSrgb(new Rgba32(
            (byte)Math.Clamp(r, 0, 255),
            (byte)Math.Clamp(g, 0, 255),
            (byte)Math.Clamp(b, 0, 255), 255));
        var d = (double)Oklab.DistanceSquared(key, sample);
        return d > current ? d : current;
    }

    // ─────────────────────────────────────────────────────────────────────────

    /// <summary>
    /// Campiona il colore dominante in un'area quadrata di lato <c>2*radius+1</c>
    /// centrata in (cx, cy).
    /// Restituisce il colore più frequente tra i pixel OPACHI (alpha ≥ 128)
    /// nell'area, oppure il pixel centrale se tutti i pixel sono trasparenti.
    ///
    /// Usare questo metodo nella pipetta invece di leggere il singolo pixel:
    /// elimina il problema di campionare accidentalmente pixel anti-aliased,
    /// compressi o con colore leggermente deviante dall'area circostante.
    /// </summary>
    public static Rgba32 SampleRegionColor(Image<Rgba32> image, int cx, int cy, int radius = 2)
    {
        var w = image.Width;
        var h = image.Height;
        if (w < 1 || h < 1)
            return default;

        var x0 = Math.Max(0, cx - radius);
        var x1 = Math.Min(w - 1, cx + radius);
        var y0 = Math.Max(0, cy - radius);
        var y1 = Math.Min(h - 1, cy + radius);

        // Dictionary packed RGB → count
        var freq = new Dictionary<uint, int>((x1 - x0 + 1) * (y1 - y0 + 1));

        for (var y = y0; y <= y1; y++)
        for (var x = x0; x <= x1; x++)
        {
            var p = image[x, y];
            if (p.A < 128) continue; // salta pixel trasparenti / semi-trasparenti
            var packed = (uint)(p.R << 16 | p.G << 8 | p.B);
            freq.TryGetValue(packed, out var cnt);
            freq[packed] = cnt + 1;
        }

        if (freq.Count == 0)
        {
            // Tutta l'area è trasparente: ritorna il pixel centrale (anche se A=0)
            return image[Math.Clamp(cx, 0, w - 1), Math.Clamp(cy, 0, h - 1)];
        }

        // Trova il colore più frequente
        uint bestPacked = 0;
        var bestCount = 0;
        foreach (var (packed, cnt) in freq)
        {
            if (cnt <= bestCount) continue;
            bestCount = cnt;
            bestPacked = packed;
        }

        return new Rgba32(
            (byte)(bestPacked >> 16),
            (byte)((bestPacked >> 8) & 0xFF),
            (byte)(bestPacked & 0xFF),
            255);
    }

    // ─────────────────────────────────────────────────────────────────────────

    /// <summary>
    /// Corregge il colore chiave sfondo cercando il pixel opaco di bordo più vicino (distanza RGB²).
    /// Utile dopo operazioni di quantizzazione palette: il colore campionato con la pipetta prima
    /// del quantize potrebbe non esistere più nell'immagine rimappata.
    ///
    /// Scansiona <paramref name="borderDepth"/> pixel di profondità dal perimetro
    /// (default 3) per coprire i casi in cui il bordo esterno è già stato azzerato
    /// da un run precedente di background removal.
    ///
    /// Se nessun pixel opaco è entro <paramref name="maxRgbDistancePerChannel"/> unità per canale,
    /// restituisce <paramref name="key"/> invariato.
    /// </summary>
    public static Rgba32 SnapKeyToBorderColor(
        Image<Rgba32> image,
        Rgba32 key,
        double maxRgbDistancePerChannel = 32,
        int borderDepth = 3)
    {
        var w = image.Width;
        var h = image.Height;
        if (w < 1 || h < 1) return key;

        var pixels = ImageUtils.ToFlatArray(image);
        var maxDistSq = maxRgbDistancePerChannel * maxRgbDistancePerChannel * 3.0;
        var bestDistSq = double.MaxValue;
        var best = key;

        void Check(int x, int y)
        {
            if ((uint)x >= (uint)w || (uint)y >= (uint)h) return;
            var p = pixels[y * w + x];
            if (p.A == 0) return; // già trasparente: salta
            var dr = (double)(p.R - key.R);
            var dg = (double)(p.G - key.G);
            var db = (double)(p.B - key.B);
            var d = dr * dr + dg * dg + db * db;
            if (d < bestDistSq) { bestDistSq = d; best = p; }
        }

        var depth = Math.Max(1, Math.Min(borderDepth, Math.Min(w / 2, h / 2)));
        for (var d = 0; d < depth; d++)
        {
            for (var x = d; x < w - d; x++) { Check(x, d); Check(x, h - 1 - d); }
            for (var y = d + 1; y < h - 1 - d; y++) { Check(d, y); Check(w - 1 - d, y); }
        }

        return bestDistSq <= maxDistSq
            ? new Rgba32(best.R, best.G, best.B, 255)
            : key;
    }

    // ─────────────────────────────────────────────────────────────────────────

    /// <summary>
    /// Uniforma tutti i pixel opachi il cui colore è entro <paramref name="tolerance"/>
    /// unità RGB dal colore chiave al colore chiave stesso (distanza Euclidea sui 3 canali).
    /// Utile per appiattire variazioni minime prima del flood fill.
    /// </summary>
    public static void SnapBackgroundRgbInPlace(Image<Rgba32> image, Rgba32 key, double tolerance)
    {
        var tol = Math.Max(0, tolerance);
        var tolSq = tol * tol;
        image.ProcessPixelRows(accessor =>
        {
            for (var y = 0; y < accessor.Height; y++)
            {
                var row = accessor.GetRowSpan(y);
                for (var x = 0; x < row.Length; x++)
                {
                    var p = row[x];
                    if (p.A == 0) continue;
                    var dr = p.R - key.R;
                    var dg = p.G - key.G;
                    var db = p.B - key.B;
                    if (dr * dr + dg * dg + db * db <= tolSq)
                        row[x] = new Rgba32(key.R, key.G, key.B, p.A);
                }
            }
        });
    }

    // ─────────────────────────────────────────────────────────────────────────

    /// <summary>
    /// Costruisce la mappa bordi Sobel (luma-based).
    /// Pixel già trasparenti sono esclusi dalla mappa: impedisce che la frontiera
    /// trasparente↔sprite generi false edges che bloccano il flood nelle esecuzioni
    /// successive (quando parte del bordo immagine è già stata azzerata).
    /// </summary>
    private static bool[] BuildSobelEdgeMap(Rgba32[] pixels, int w, int h, double threshold)
    {
        var edge = new bool[w * h];
        if (threshold <= 0 || w < 3 || h < 3)
            return edge;

        var luma = new double[w * h];
        for (var i = 0; i < pixels.Length; i++)
        {
            var p = pixels[i];
            // Pixel trasparenti contribuiscono con luma=0; ma non sono marcati come edge.
            luma[i] = p.A == 0 ? 0.0 : 0.299 * p.R + 0.587 * p.G + 0.114 * p.B;
        }

        int I(int x, int y) => y * w + x;

        for (var y = 1; y < h - 1; y++)
        for (var x = 1; x < w - 1; x++)
        {
            // I pixel già trasparenti non sono bordi sprite.
            if (pixels[I(x, y)].A == 0) continue;

            var gx =
                -luma[I(x - 1, y - 1)] + luma[I(x + 1, y - 1)]
                - 2 * luma[I(x - 1, y)] + 2 * luma[I(x + 1, y)]
                - luma[I(x - 1, y + 1)] + luma[I(x + 1, y + 1)];

            var gy =
                luma[I(x - 1, y - 1)] + 2 * luma[I(x, y - 1)] + luma[I(x + 1, y - 1)]
                - luma[I(x - 1, y + 1)] - 2 * luma[I(x, y + 1)] - luma[I(x + 1, y + 1)];

            edge[I(x, y)] = Math.Sqrt(gx * gx + gy * gy) >= threshold;
        }

        return edge;
    }
}

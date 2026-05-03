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
/// Pipeline raccomandata (dal codice chiamante):
///   1. <see cref="SampleRegionColor"/>      — colore dominante nell'area cliccata (5×5 default)
///   2. <see cref="SnapKeyToBorderColor"/>   — corregge se Quantize ha rimappato i colori
///   3. <see cref="ApplyInPlace"/>           — flood fill dal bordo con protezione Sobel
///   4. <see cref="AlphaThreshold.ApplyInPlace"/> — binarizza pixel semi-trasparenti residui (opzionale)
///   5. <see cref="Defringe.FromOpaqueNeighbors"/> — decontamina fringe colorati (opzionale)
///
/// Limitazioni by design (non bug):
///   - Sfondo non connesso al perimetro non viene rimosso (protezione interni sprite).
///   - Chiave colore singola: sfondi a gradiente o texture richiedono un algoritmo diverso.
///   - Sobel protegge anche variazioni interne al background: trade-off per pixel art.
/// </summary>
public static class BackgroundIsolation
{
    public sealed record Options(
        Rgba32 BackgroundColor,
        double ColorTolerance = 8,
        double EdgeThreshold = 100,                              // Sobel: 100 supera artefatti JPEG (<80), blocca bordi sprite (200-1440)
        bool UseOklab = true,
        bool ProtectStrongEdges = true,
        bool Use8Connectivity = true,                           // vicini diagonali → elimina angoli isolati
        IReadOnlyList<(int X, int Y)>? AdditionalSeeds = null); // seed extra (es. punto cliccato con pipetta)

    // ─────────────────────────────────────────────────────────────────────────

    public static int ApplyInPlace(Image<Rgba32> image, Options options)
    {
        var w = image.Width;
        var h = image.Height;
        if (w < 1 || h < 1) return 0;

        var pixels = ImageUtils.ToFlatArray(image);
        var edge   = options.ProtectStrongEdges
            ? BuildSobelEdgeMap(pixels, w, h, Math.Clamp(options.EdgeThreshold, 0, 4096))
            : new bool[w * h];
        var removed = new bool[w * h];
        var q       = new Queue<int>();

        // ── Calibrazione tolleranza Oklab ─────────────────────────────────────
        // Delegata a CalibrateOklabToleranceSq (condivisa con GlobalChromaKey).
        var rgbTol     = Math.Max(0, options.ColorTolerance);
        var oklabKey   = Oklab.FromSrgb(options.BackgroundColor);
        var oklabTolSq = CalibrateOklabToleranceSq(options.BackgroundColor, rgbTol);

        var rgbTolSq = rgbTol * rgbTol;                     // usato solo in fallback non-Oklab
        var bgR      = options.BackgroundColor.R;
        var bgG      = options.BackgroundColor.G;
        var bgB      = options.BackgroundColor.B;

        // ── Helpers locali ────────────────────────────────────────────────────

        // Nota: NON controlla p.A == 0 perché CanFlood lo verifica prima.
        bool MatchesBackground(int idx)
        {
            var p = pixels[idx];
            if (options.UseOklab)
                return (double)Oklab.DistanceSquared(oklabKey, Oklab.FromSrgb(p)) <= oklabTolSq;

            var dr = p.R - bgR;
            var dg = p.G - bgG;
            var db = p.B - bgB;
            return dr * dr + dg * dg + db * db <= rgbTolSq;
        }

        // Pixel attraversabile se:
        //   a) già trasparente (run precedenti) → corridoio passabile
        //   b) corrisponde al colore sfondo E non è su bordo forte Sobel
        bool CanFlood(int idx)
        {
            if (pixels[idx].A == 0) return true;
            return !edge[idx] && MatchesBackground(idx);
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
            Seed(x);                    // riga 0
            if (h > 1) Seed((h-1)*w+x); // riga h-1
        }
        for (var y = 1; y < h - 1; y++)
        {
            Seed(y*w);                  // colonna 0
            if (w > 1) Seed(y*w+w-1);  // colonna w-1
        }

        // ── Seeding aggiuntivo (punti espliciti, es. ultima pick contagocce) ─
        // Permette di raggiungere pixel residui interni non connessi al bordo.
        // Un seed su un pixel già trasparente è no-op (CanFlood → true ma rimossa=skip).
        if (options.AdditionalSeeds is { Count: > 0 })
        {
            foreach (var (sx, sy) in options.AdditionalSeeds)
            {
                // Bound check con cast a uint: elimina due confronti (x<0 implicito)
                if ((uint)sx < (uint)w && (uint)sy < (uint)h)
                    Seed(sy * w + sx);
            }
        }

        // ── Propagazione ─────────────────────────────────────────────────────
        void TryAdd(int nidx, bool inBounds)
        {
            if (!inBounds || removed[nidx] || !CanFlood(nidx)) return;
            removed[nidx] = true;
            q.Enqueue(nidx);
        }

        while (q.Count > 0)
        {
            var idx = q.Dequeue();
            var py  = idx / w;
            var px  = idx - py * w;     // idx % w senza secondo div

            // 4 vicini cardinali
            TryAdd(idx - 1,   px > 0);
            TryAdd(idx + 1,   px < w - 1);
            TryAdd(idx - w,   py > 0);
            TryAdd(idx + w,   py < h - 1);

            if (options.Use8Connectivity)
            {
                var notLeft  = px > 0;
                var notRight = px < w - 1;
                var notTop   = py > 0;
                var notBot   = py < h - 1;
                TryAdd(idx - w - 1, notTop  && notLeft);
                TryAdd(idx - w + 1, notTop  && notRight);
                TryAdd(idx + w - 1, notBot  && notLeft);
                TryAdd(idx + w + 1, notBot  && notRight);
            }
        }

        // ── Azzeramento ───────────────────────────────────────────────────────
        // RGBA(0,0,0,0) — non solo A=0.
        // I game engine (Unity, Godot) campionano i pixel vicini con bilinear filtering:
        // se il pixel rimosso mantiene i canali RGB colorati, appare un alone (alpha bleeding)
        // attorno allo sprite. Azzerare tutto previene questo artefatto.
        var removedCount = 0;
        image.ProcessPixelRows(accessor =>
        {
            for (var y = 0; y < accessor.Height; y++)
            {
                var row = accessor.GetRowSpan(y);
                for (var x = 0; x < row.Length; x++)
                {
                    var idx = y * w + x;
                    if (!removed[idx] || pixels[idx].A == 0) continue;
                    row[x] = new Rgba32(0, 0, 0, 0); // RGB azzerati: nessun alpha bleeding
                    removedCount++;
                }
            }
        });
        return removedCount;
    }

    // ─────────────────────────────────────────────────────────────────────────

    /// <summary>
    /// Calibra la soglia di tolleranza Oklab² a partire da una tolleranza RGB lineare.
    ///
    /// Campiona 8 direzioni nello spazio RGB alla distanza <paramref name="rgbTolerance"/>:
    ///   • 6 assi allineati (+/-R, +/-G, +/-B)
    ///   • 2 diagonali principali (+tol su tutti i canali, -tol su tutti)
    /// Il massimo delle distanze Oklab² ottenute diventa la soglia effettiva.
    ///
    /// Necessario perché per colori saturi uno shift su un canale può clipparsi a 0/255
    /// producendo distanze Oklab molto diverse dagli altri assi.
    ///
    /// Condiviso con <see cref="GlobalChromaKey"/> per garantire coerenza tra le due modalità.
    /// </summary>
    public static double CalibrateOklabToleranceSq(Rgba32 key, double rgbTolerance)
    {
        var oklabKey = Oklab.FromSrgb(key);
        var tolInt   = (int)Math.Round(Math.Max(0, rgbTolerance));
        if (tolInt == 0)
            return (rgbTolerance / 255.0) * (rgbTolerance / 255.0);

        var (cr, cg, cb) = ((int)key.R, (int)key.G, (int)key.B);
        var sq = 0.0;
        sq = MaxOklabDistSq(oklabKey, sq, cr + tolInt, cg,          cb         );
        sq = MaxOklabDistSq(oklabKey, sq, cr - tolInt, cg,          cb         );
        sq = MaxOklabDistSq(oklabKey, sq, cr,          cg + tolInt, cb         );
        sq = MaxOklabDistSq(oklabKey, sq, cr,          cg - tolInt, cb         );
        sq = MaxOklabDistSq(oklabKey, sq, cr,          cg,          cb + tolInt);
        sq = MaxOklabDistSq(oklabKey, sq, cr,          cg,          cb - tolInt);
        sq = MaxOklabDistSq(oklabKey, sq, cr + tolInt, cg + tolInt, cb + tolInt);
        sq = MaxOklabDistSq(oklabKey, sq, cr - tolInt, cg - tolInt, cb - tolInt);
        return sq;
    }

    private static double MaxOklabDistSq(in Oklab key, double current, int r, int g, int b)
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
    /// Campiona il colore dominante (mode) in un'area quadrata di lato <c>2*radius+1</c>
    /// centrata in (cx, cy). Ignora pixel con alpha &lt; 128.
    ///
    /// Per immagini JPEG: tutti i 25 campioni potrebbero avere colori leggermente diversi
    /// (no chiaro "modo"). In quel caso ritorna il campione più frequente — più robusto
    /// di un singolo pixel ma non perfetto. Per pixel art lossless è ideale.
    ///
    /// Restituisce il pixel centrale se tutta l'area è trasparente.
    /// </summary>
    public static Rgba32 SampleRegionColor(Image<Rgba32> image, int cx, int cy, int radius = 2)
    {
        var w = image.Width;
        var h = image.Height;
        if (w < 1 || h < 1) return default;

        var x0 = Math.Max(0, cx - radius);
        var x1 = Math.Min(w - 1, cx + radius);
        var y0 = Math.Max(0, cy - radius);
        var y1 = Math.Min(h - 1, cy + radius);

        var freq = new Dictionary<uint, int>((x1 - x0 + 1) * (y1 - y0 + 1));

        for (var y = y0; y <= y1; y++)
        for (var x = x0; x <= x1; x++)
        {
            var p = image[x, y];
            if (p.A < 128) continue;
            var packed = (uint)(p.R << 16 | p.G << 8 | p.B);
            freq.TryGetValue(packed, out var cnt);
            freq[packed] = cnt + 1;
        }

        if (freq.Count == 0)
            return image[Math.Clamp(cx, 0, w - 1), Math.Clamp(cy, 0, h - 1)];

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
    /// Corregge il colore chiave sfondo cercando il pixel opaco di bordo più vicino
    /// (distanza RGB²). Utile dopo Quantize: il colore campionato con la pipetta potrebbe
    /// non esistere più nell'immagine rimappata.
    ///
    /// Scansiona <paramref name="borderDepth"/> pixel di profondità dal perimetro (default 3)
    /// per coprire i casi in cui il bordo esterno sia già stato azzerato da un run precedente.
    ///
    /// Accede direttamente ai pixel di bordo con <c>image[x,y]</c> senza copiare l'intera
    /// immagine — O(w+h) anziché O(w×h).
    ///
    /// Se nessun pixel opaco è entro <paramref name="maxRgbDistancePerChannel"/> unità per
    /// canale, restituisce <paramref name="key"/> invariato.
    /// </summary>
    public static Rgba32 SnapKeyToBorderColor(
        Image<Rgba32> image,
        Rgba32 key,
        double maxRgbDistancePerChannel = 32,
        int borderDepth = 3)
    {
        var w = image.Width;
        var h = image.Height;
        if (w < 1 || h < 1 || maxRgbDistancePerChannel < 0) return key;

        // Accesso diretto image[x,y] — nessuna copia ToFlatArray, O(w+h) anziché O(w×h)
        var maxDistSq = maxRgbDistancePerChannel * maxRgbDistancePerChannel * 3.0;
        var bestDistSq = double.MaxValue;
        var best = key;

        void Check(int x, int y)
        {
            var p = image[x, y];
            if (p.A == 0) return;
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
    /// unità RGB (Euclidea 3-canali) al colore chiave stesso.
    ///
    /// Usato da <see cref="PixelArtPipeline"/> e <see cref="SharedPaletteBuilder"/>
    /// prima del flood fill per appiattire variazioni minime di compressione.
    /// Non è dead code — non rimuovere.
    /// </summary>
    public static void SnapBackgroundRgbInPlace(Image<Rgba32> image, Rgba32 key, double tolerance)
    {
        var tol   = Math.Max(0, tolerance);
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
    ///
    /// Ottimizzazioni rispetto all'implementazione naïve:
    ///   • Luma in <c>float[]</c> invece di <c>double[]</c>: metà memoria (16 MB vs 32 MB
    ///     su immagini 2048×2048) senza perdita di precisione per il confronto soglia.
    ///   • Confronto <c>gx²+gy² ≥ threshold²</c> senza <c>Math.Sqrt</c> nell'hot loop.
    ///   • Pixel trasparenti esclusi dalla mappa: la frontiera trasparente↔opaco non
    ///     genera false edges che bloccherebbero il flood nelle esecuzioni successive.
    /// </summary>
    private static bool[] BuildSobelEdgeMap(Rgba32[] pixels, int w, int h, double threshold)
    {
        var edge = new bool[w * h];
        if (threshold <= 0 || w < 3 || h < 3)
            return edge;

        // float[] dimezza la memoria rispetto a double[] con stesso risultato pratico
        var luma = new float[w * h];
        for (var i = 0; i < pixels.Length; i++)
        {
            var p = pixels[i];
            luma[i] = p.A == 0
                ? 0f
                : (float)(0.299 * p.R + 0.587 * p.G + 0.114 * p.B);
        }

        // Quadrato della soglia: elimina Math.Sqrt nell'hot loop
        var threshSq = (float)(threshold * threshold);

        for (var y = 1; y < h - 1; y++)
        {
            var row = y * w;
            for (var x = 1; x < w - 1; x++)
            {
                var idx = row + x;
                if (pixels[idx].A == 0) continue; // pixel già trasparente: non è un bordo sprite

                // Indici dei vicini (precalcolati per evitare moltiplicazioni nel kernel)
                var tl = idx - w - 1; var tc = idx - w; var tr = idx - w + 1;
                var ml = idx     - 1;                   var mr = idx     + 1;
                var bl = idx + w - 1; var bc = idx + w; var br = idx + w + 1;

                var gx = -luma[tl] + luma[tr]
                         - 2f * luma[ml] + 2f * luma[mr]
                         - luma[bl] + luma[br];

                var gy = luma[tl]  + 2f * luma[tc]  + luma[tr]
                        - luma[bl] - 2f * luma[bc]  - luma[br];

                edge[idx] = gx * gx + gy * gy >= threshSq; // nessuna sqrt
            }
        }

        return edge;
    }
}

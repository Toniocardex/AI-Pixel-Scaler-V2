using AiPixelScaler.Core.Color;
using SixLabors.ImageSharp;
using SixLabors.ImageSharp.PixelFormats;

namespace AiPixelScaler.Core.Pipeline.Imaging;

/// <summary>
/// Isola lo sfondo esterno senza rimuovere dettagli interni dello sprite:
/// flood fill non ricorsivo dai bordi immagine, vincolato da similarita' colore
/// e opzionalmente bloccato da bordi Sobel.
/// </summary>
public static class BackgroundIsolation
{
    public sealed record Options(
        Rgba32 BackgroundColor,
        double ColorTolerance = 8,
        double EdgeThreshold = 48,
        bool UseOklab = true,
        bool ProtectStrongEdges = true);

    public static int ApplyInPlace(Image<Rgba32> image, Options options)
    {
        var w = image.Width;
        var h = image.Height;
        if (w < 1 || h < 1) return 0;

        var pixels = ImageUtils.ToFlatArray(image);
        var edge = options.ProtectStrongEdges
            ? BuildSobelEdgeMap(pixels, w, h, Math.Clamp(options.EdgeThreshold, 0, 1024))
            : new bool[w * h];
        var removed = new bool[w * h];
        var q = new Queue<(int x, int y)>();

        var rgbTol = Math.Max(0, options.ColorTolerance);
        var oklabKey = Oklab.FromSrgb(options.BackgroundColor);

        // Calibra oklabTolSq in base al colore chiave effettivo.
        // rgbTol è in unità [0,255]; calcoliamo la distanza Oklab reale per uno
        // spostamento uniforme di +rgbTol e -rgbTol su tutti i canali (caso peggiore per i grigi).
        var calR = (int)options.BackgroundColor.R;
        var calG = (int)options.BackgroundColor.G;
        var calB = (int)options.BackgroundColor.B;
        var tolInt = (int)Math.Round(rgbTol);
        var oklabPlus  = Oklab.FromSrgb(new Rgba32(
            (byte)Math.Clamp(calR + tolInt, 0, 255),
            (byte)Math.Clamp(calG + tolInt, 0, 255),
            (byte)Math.Clamp(calB + tolInt, 0, 255), 255));
        var oklabMinus = Oklab.FromSrgb(new Rgba32(
            (byte)Math.Clamp(calR - tolInt, 0, 255),
            (byte)Math.Clamp(calG - tolInt, 0, 255),
            (byte)Math.Clamp(calB - tolInt, 0, 255), 255));
        var oklabTolSq = Math.Max(
            Oklab.DistanceSquared(oklabKey, oklabPlus),
            Oklab.DistanceSquared(oklabKey, oklabMinus));
        // Fallback minimo per rgbTol molto piccoli
        if (oklabTolSq <= 0f)
            oklabTolSq = (float)((rgbTol / 255.0) * (rgbTol / 255.0));

        int I(int x, int y) => y * w + x;

        bool MatchesBackground(int x, int y)
        {
            var p = pixels[I(x, y)];
            if (p.A == 0) return true;
            if (options.UseOklab)
                return Oklab.DistanceSquared(oklabKey, Oklab.FromSrgb(p)) <= oklabTolSq;

            var dr = p.R - options.BackgroundColor.R;
            var dg = p.G - options.BackgroundColor.G;
            var db = p.B - options.BackgroundColor.B;
            return dr * dr + dg * dg + db * db <= rgbTol * rgbTol;
        }

        // Un pixel è "attraversabile" se è già trasparente (run precedenti) OPPURE
        // corrisponde al colore sfondo corrente (non è un bordo forte).
        // Questo consente il flood anche dopo passaggi multipli di rimozione:
        // i pixel già azzerati fungono da corridoio per raggiungere nuovi pixel sfondo.
        bool CanFlood(int x, int y)
        {
            var i = I(x, y);
            if (pixels[i].A == 0) return true; // già trasparente: passabile
            return !edge[i] && MatchesBackground(x, y);
        }

        void Seed(int x, int y)
        {
            var i = I(x, y);
            if (removed[i] || !CanFlood(x, y)) return;
            removed[i] = true;
            q.Enqueue((x, y));
        }

        for (var x = 0; x < w; x++)
        {
            Seed(x, 0);
            if (h > 1) Seed(x, h - 1);
        }
        for (var y = 0; y < h; y++)
        {
            Seed(0, y);
            if (w > 1) Seed(w - 1, y);
        }

        void TryAdd(int x, int y)
        {
            if (x < 0 || y < 0 || x >= w || y >= h) return;
            var i = I(x, y);
            if (removed[i] || !CanFlood(x, y)) return;
            removed[i] = true;
            q.Enqueue((x, y));
        }

        while (q.Count > 0)
        {
            var (x, y) = q.Dequeue();
            TryAdd(x - 1, y);
            TryAdd(x + 1, y);
            TryAdd(x, y - 1);
            TryAdd(x, y + 1);
        }

        // Conta e azzera solo pixel che erano OPACHI (non già trasparenti da run precedenti).
        var removedCount = 0;
        image.ProcessPixelRows(accessor =>
        {
            for (var y = 0; y < accessor.Height; y++)
            {
                var row = accessor.GetRowSpan(y);
                for (var x = 0; x < row.Length; x++)
                {
                    var idx = I(x, y);
                    if (!removed[idx] || pixels[idx].A == 0) continue; // già trasparente: salta
                    row[x] = new Rgba32(row[x].R, row[x].G, row[x].B, 0);
                    removedCount++;
                }
            }
        });
        return removedCount;
    }

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

    private static bool[] BuildSobelEdgeMap(Rgba32[] pixels, int w, int h, double threshold)
    {
        var luma = new double[w * h];
        var edge = new bool[w * h];

        for (var i = 0; i < pixels.Length; i++)
        {
            var p = pixels[i];
            luma[i] = p.A == 0 ? 0 : 0.299 * p.R + 0.587 * p.G + 0.114 * p.B;
        }

        int I(int x, int y) => y * w + x;
        for (var y = 1; y < h - 1; y++)
        {
            for (var x = 1; x < w - 1; x++)
            {
                // I pixel già trasparenti non sono bordi sprite: escluderli evita che la
                // mappa Sobel blocchi il flood nelle esecuzioni successive (quando parte del
                // bordo immagine è già stata azzerata da un run precedente).
                if (pixels[I(x, y)].A == 0) continue; // edge[i] resta false

                var gx =
                    -luma[I(x - 1, y - 1)] + luma[I(x + 1, y - 1)] +
                    -2 * luma[I(x - 1, y)] + 2 * luma[I(x + 1, y)] +
                    -luma[I(x - 1, y + 1)] + luma[I(x + 1, y + 1)];
                var gy =
                    luma[I(x - 1, y - 1)] + 2 * luma[I(x, y - 1)] + luma[I(x + 1, y - 1)] -
                    luma[I(x - 1, y + 1)] - 2 * luma[I(x, y + 1)] - luma[I(x + 1, y + 1)];

                edge[I(x, y)] = Math.Sqrt(gx * gx + gy * gy) >= threshold;
            }
        }

        return edge;
    }
}

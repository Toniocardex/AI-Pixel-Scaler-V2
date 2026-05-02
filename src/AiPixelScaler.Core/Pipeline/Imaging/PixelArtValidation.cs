using SixLabors.ImageSharp;
using SixLabors.ImageSharp.PixelFormats;

namespace AiPixelScaler.Core.Pipeline.Imaging;

/// <summary>
/// Statistiche e controlli leggeri per asset pixel-art (colori unici, vincoli opzionali).
/// </summary>
public static class PixelArtValidation
{
    public enum Severity { Info, Warning, Error }

    public sealed record Issue(Severity Severity, string Code, string Message, string Suggestion);

    public sealed record Options(
        int? MaxUniqueColors = null,
        bool RequirePowerOfTwoDimensions = false,
        bool RequireBinaryAlpha = false,
        bool IgnoreFullyTransparentInCount = true,
        Rgba32? ExpectedBackgroundColor = null,
        double? MaxBackgroundMismatchRatio = null,
        double BackgroundTolerance = 0);

    public sealed record Result(
        int UniqueColors,
        bool IsValid,
        IReadOnlyList<string> Issues,
        IReadOnlyList<Issue> StructuredIssues);

    /// <summary>
    /// Conta i colori <see cref="Rgba32"/> distinti (per default ignora α=0).
    /// </summary>
    public static int CountUniqueColors(Image<Rgba32> image, bool ignoreFullyTransparent = true)
    {
        var set = new HashSet<Rgba32>();
        image.ProcessPixelRows(accessor =>
        {
            for (var y = 0; y < accessor.Height; y++)
            {
                var row = accessor.GetRowSpan(y);
                foreach (ref readonly var p in row)
                {
                    if (ignoreFullyTransparent && p.A == 0) continue;
                    set.Add(p);
                }
            }
        });
        return set.Count;
    }

    public static Result Validate(Image<Rgba32> image, Options options)
    {
        var issues = new List<string>();
        var structured = new List<Issue>();
        var unique = CountUniqueColors(image, options.IgnoreFullyTransparentInCount);

        if (options.MaxUniqueColors is { } maxU && unique > maxU)
        {
            var message = $"Troppi colori unici ({unique} > {maxU}).";
            issues.Add(message);
            structured.Add(new Issue(
                Severity.Warning,
                "MAX_UNIQUE_COLORS",
                message,
                $"Riduci la palette a {maxU} colori o meno."));
        }

        if (options.RequirePowerOfTwoDimensions)
        {
            if (!IsPowerOfTwo(image.Width))
            {
                var message = $"Larghezza {image.Width} non è potenza di 2.";
                issues.Add(message);
                structured.Add(new Issue(
                    Severity.Warning,
                    "WIDTH_NOT_POW2",
                    message,
                    "Ridimensiona o fai pad della larghezza al successivo valore 2^n."));
            }

            if (!IsPowerOfTwo(image.Height))
            {
                var message = $"Altezza {image.Height} non è potenza di 2.";
                issues.Add(message);
                structured.Add(new Issue(
                    Severity.Warning,
                    "HEIGHT_NOT_POW2",
                    message,
                    "Ridimensiona o fai pad dell'altezza al successivo valore 2^n."));
            }
        }

        if (options.RequireBinaryAlpha)
        {
            var bad = false;
            image.ProcessPixelRows(accessor =>
            {
                for (var y = 0; y < accessor.Height && !bad; y++)
                {
                    var row = accessor.GetRowSpan(y);
                    foreach (ref readonly var p in row)
                    {
                        if (p.A is not (0 or 255)) { bad = true; return; }
                    }
                }
            });
            if (bad)
            {
                const string message = "Alfa non binaria (valori diversi da 0 e 255).";
                issues.Add(message);
                structured.Add(new Issue(
                    Severity.Error,
                    "ALPHA_NOT_BINARY",
                    message,
                    "Applica soglia alpha per ottenere solo 0/255."));
            }
        }

        if (options.ExpectedBackgroundColor is { } background && options.MaxBackgroundMismatchRatio is { } maxMismatch)
        {
            var mismatch = ComputeBackgroundMismatchRatio(image, background, Math.Max(0, options.BackgroundTolerance));
            if (mismatch > maxMismatch)
            {
                var message = $"Sfondo non uniforme rispetto al colore atteso ({mismatch:P1} > {maxMismatch:P1}).";
                issues.Add(message);
                structured.Add(new Issue(
                    Severity.Warning,
                    "BACKGROUND_MISMATCH",
                    message,
                    "Esegui prima lo snap sfondo con tolleranza adeguata."));
            }
        }

        return new Result(unique, issues.Count == 0, issues, structured);
    }

    private static bool IsPowerOfTwo(int n) => n > 0 && (n & (n - 1)) == 0;

    private static double ComputeBackgroundMismatchRatio(Image<Rgba32> image, Rgba32 key, double tolerance)
    {
        var tolSq = tolerance * tolerance;
        var totalOpaque = 0;
        var mismatch = 0;
        image.ProcessPixelRows(accessor =>
        {
            for (var y = 0; y < accessor.Height; y++)
            {
                var row = accessor.GetRowSpan(y);
                for (var x = 0; x < row.Length; x++)
                {
                    var p = row[x];
                    if (p.A == 0) continue;
                    totalOpaque++;
                    var dr = p.R - key.R;
                    var dg = p.G - key.G;
                    var db = p.B - key.B;
                    if (dr * dr + dg * dg + db * db > tolSq)
                        mismatch++;
                }
            }
        });
        return totalOpaque == 0 ? 0 : (double)mismatch / totalOpaque;
    }
}

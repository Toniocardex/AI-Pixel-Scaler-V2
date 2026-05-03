using AiPixelScaler.Core.Color;
using SixLabors.ImageSharp;
using SixLabors.ImageSharp.PixelFormats;

namespace AiPixelScaler.Core.Pipeline.Imaging;

/// <summary>
/// Rimozione sfondo globale (Global Chroma-Key).
///
/// A differenza di <see cref="BackgroundIsolation"/> (flood fill border-seeded),
/// questa modalità scansiona ogni pixel dell'immagine indipendentemente dalla
/// contiguità spaziale: qualunque pixel il cui colore Oklab² sia entro la soglia
/// viene rimosso, anche se circondato completamente da pixel sprite (isole interne).
///
/// Caso d'uso tipico: sprite sheets AI-generated con pixel residui isolati interni
/// che il flood fill non riesce a raggiungere.
///
/// Limitazione by design: rimuove TUTTO ciò che corrisponde al colore chiave,
/// compresi eventuali dettagli dello sprite dello stesso colore.
/// Raccomandazione: applicare dopo Quantize con tolleranza bassa (5-10).
///
/// Riferimento: "Specifiche Tecniche: Ottimizzazione Pulizia Sfondo Pixel Art" §2A.
/// </summary>
public static class GlobalChromaKey
{
    /// <summary>
    /// Rimuove ogni pixel opaco il cui colore Oklab è entro <paramref name="rgbTolerance"/>
    /// unità RGB (calibrate in Oklab²) dalla chiave <paramref name="key"/>.
    ///
    /// La calibrazione della tolleranza è identica a <see cref="BackgroundIsolation.CalibrateOklabToleranceSq"/>:
    /// le due modalità producono la stessa soglia per la stessa coppia colore+tolleranza.
    ///
    /// I pixel rimossi vengono impostati a RGBA(0,0,0,0) — non solo A=0 — per prevenire
    /// l'alpha bleeding nel bilinear filtering dei game engine (Unity, Godot).
    /// </summary>
    /// <param name="image">Immagine da modificare in-place.</param>
    /// <param name="key">Colore chiave sfondo.</param>
    /// <param name="rgbTolerance">Tolleranza RGB [0-255]. 0 = solo colore esatto.</param>
    /// <returns>Numero di pixel opachi rimossi.</returns>
    public static int ApplyInPlace(Image<Rgba32> image, Rgba32 key, double rgbTolerance)
    {
        var oklabKey   = Oklab.FromSrgb(key);
        var oklabTolSq = BackgroundIsolation.CalibrateOklabToleranceSq(key, rgbTolerance);

        var removedCount = 0;

        image.ProcessPixelRows(accessor =>
        {
            for (var y = 0; y < accessor.Height; y++)
            {
                var row = accessor.GetRowSpan(y);
                for (var x = 0; x < row.Length; x++)
                {
                    var p = row[x];
                    if (p.A == 0) continue; // già trasparente

                    var distSq = (double)Oklab.DistanceSquared(oklabKey, Oklab.FromSrgb(p));
                    if (distSq > oklabTolSq) continue;

                    row[x] = new Rgba32(0, 0, 0, 0); // RGBA azzerati: no alpha bleeding
                    removedCount++;
                }
            }
        });

        return removedCount;
    }
}

using System;
using System.Runtime.InteropServices;
using Avalonia;
using Avalonia.Media.Imaging;
using Avalonia.Platform;
using SixLabors.ImageSharp;
using SixLabors.ImageSharp.PixelFormats;

namespace AiPixelScaler.Desktop.Imaging;

public static class Rgba32BitmapBridge
{
    /// <summary>
    /// Converte un Image&lt;Rgba32&gt; in un WriteableBitmap Avalonia tramite copia diretta
    /// dei pixel in memoria — nessuna codifica PNG, nessuna allocazione intermedia.
    /// </summary>
    public static Bitmap? ToBitmap(Image<Rgba32>? source)
    {
        if (source is null || source.Width == 0 || source.Height == 0)
            return null;

        var wb = new WriteableBitmap(
            new PixelSize(source.Width, source.Height),
            new Vector(96, 96),
            PixelFormat.Rgba8888,
            AlphaFormat.Unpremul);

        using var fb = wb.Lock();
        source.ProcessPixelRows(accessor =>
        {
            for (var y = 0; y < accessor.Height; y++)
            {
                var src = MemoryMarshal.AsBytes(accessor.GetRowSpan(y));
                unsafe
                {
                    fixed (byte* p = src)
                        Buffer.MemoryCopy(p, (void*)(fb.Address + y * fb.RowBytes),
                                          fb.RowBytes, src.Length);
                }
            }
        });

        return wb;
    }

    /// <summary>
    /// Aggiorna solo le righe [yMin, yMax) di un WriteableBitmap già esistente.
    /// Usato dalla gomma per evitare di ricreare l'intero bitmap ad ogni pennellata.
    /// </summary>
    public static void UpdateRows(WriteableBitmap wb, Image<Rgba32> source, int yMin, int yMax)
    {
        yMin = Math.Max(0, yMin);
        yMax = Math.Min(source.Height, yMax);
        if (yMin >= yMax) return;

        using var fb = wb.Lock();
        source.ProcessPixelRows(accessor =>
        {
            for (var y = yMin; y < yMax; y++)
            {
                var src = MemoryMarshal.AsBytes(accessor.GetRowSpan(y));
                unsafe
                {
                    fixed (byte* p = src)
                        Buffer.MemoryCopy(p, (void*)(fb.Address + y * fb.RowBytes),
                                          fb.RowBytes, src.Length);
                }
            }
        });
    }
}

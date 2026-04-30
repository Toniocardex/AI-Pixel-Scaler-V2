using System;
using System.IO;
using System.Runtime.InteropServices;
using System.Threading.Tasks;
using Avalonia.Input.Platform;
using Avalonia.Media.Imaging;
using Avalonia.Platform;
using SixLabors.ImageSharp;
using SixLabors.ImageSharp.PixelFormats;

namespace AiPixelScaler.Desktop.Services;

/// <summary>
/// Legge un’immagine dagli appunti (estensione <c>TryGetBitmapAsync</c> di Avalonia 12).
/// </summary>
internal static class ClipboardImageReader
{
    internal static async Task<Image<Rgba32>?> TryReadImageAsync(IClipboard clipboard)
    {
        try
        {
            var fromBmp = await clipboard.TryGetBitmapAsync();
            if (fromBmp is null)
                return null;
            try
            {
                return TryFromAvaloniaBitmap(fromBmp);
            }
            finally
            {
                fromBmp.Dispose();
            }
        }
        catch
        {
            return null;
        }
    }

    private static Image<Rgba32>? TryFromAvaloniaBitmap(Bitmap bmp)
    {
        try
        {
            if (bmp.PixelSize.Width < 1 || bmp.PixelSize.Height < 1)
                return null;

            if (bmp is WriteableBitmap wb && wb.Format == PixelFormat.Rgba8888)
            {
                using var fb = wb.Lock();
                var w = wb.PixelSize.Width;
                var h = wb.PixelSize.Height;
                var img = new Image<Rgba32>(w, h);
                img.ProcessPixelRows(accessor =>
                {
                    for (var y = 0; y < h; y++)
                    {
                        var dst = MemoryMarshal.AsBytes(accessor.GetRowSpan(y));
                        unsafe
                        {
                            fixed (byte* d = dst)
                            {
                                var srcRow = (byte*)(fb.Address + y * fb.RowBytes);
                                Buffer.MemoryCopy(srcRow, d, dst.Length, dst.Length);
                            }
                        }
                    }
                });
                return img;
            }

            using var ms = new MemoryStream();
            bmp.Save(ms);
            if (ms.Length < 8) return null;
            ms.Position = 0;
            return Image.Load<Rgba32>(ms);
        }
        catch
        {
            return null;
        }
    }
}

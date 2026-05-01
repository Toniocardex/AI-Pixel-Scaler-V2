using System;
using System.IO;
using System.Threading.Tasks;
using AiPixelScaler.Core.Geometry;
using AiPixelScaler.Core.Pipeline.Normalization;
using Avalonia.Input.Platform;
using Avalonia.Media.Imaging;
using Avalonia.Platform.Storage;
using SixLabors.ImageSharp;
using SixLabors.ImageSharp.Formats.Png;
using SixLabors.ImageSharp.PixelFormats;

namespace AiPixelScaler.Desktop.Controllers;

internal static class SelectionController
{
    public static AxisAlignedBox? SelectAll(Image<Rgba32>? document, Action<string> setStatus)
    {
        if (document is null)
        {
            setStatus("Nessuna immagine aperta.");
            return null;
        }

        var box = new AxisAlignedBox(0, 0, document.Width, document.Height);
        setStatus($"Selezione intera immagine: {document.Width}×{document.Height} px.");
        return box;
    }

    public static async Task ExportSelectionAsync(
        Image<Rgba32>? document,
        AxisAlignedBox? activeSelectionBox,
        IStorageProvider storageProvider,
        Action<string> setStatus)
    {
        if (document is null) { setStatus("Nessuna immagine aperta."); return; }
        if (activeSelectionBox is null) { setStatus("Nessuna selezione attiva. Trascina sull'immagine."); return; }

        try
        {
            var box = activeSelectionBox.Value;
            var suggested = $"selezione_{box.Width}x{box.Height}.png";
            var file = await storageProvider.SaveFilePickerAsync(new FilePickerSaveOptions
            {
                Title = "Salva area selezionata come PNG",
                DefaultExtension = "png",
                FileTypeChoices = [new FilePickerFileType("PNG") { Patterns = ["*.png"] }],
                SuggestedFileName = suggested,
            });
            if (file is null) return;

            using var crop = AtlasCropper.Crop(document, box);
            await using var stream = await file.OpenWriteAsync();
            crop.Save(stream, new PngEncoder());
            setStatus($"Area esportata: {box.Width}×{box.Height} px — ({box.MinX},{box.MinY}).");
        }
        catch (Exception ex)
        {
            setStatus($"Errore esportazione selezione: {ex.Message}");
        }
    }

    public static void CropToSelection(
        ref Image<Rgba32>? document,
        AxisAlignedBox? activeSelectionBox,
        Action pushUndo,
        Action clearCellState,
        Action refreshView,
        Action<string> setStatus)
    {
        if (document is null) { setStatus("Nessuna immagine aperta."); return; }
        if (activeSelectionBox is null) { setStatus("Nessuna selezione attiva."); return; }

        try
        {
            pushUndo();
            var box = activeSelectionBox.Value;
            var cropped = AtlasCropper.Crop(document, box);
            document.Dispose();
            document = cropped;
            clearCellState();
            refreshView();
            setStatus($"Immagine ritagliata alla selezione: {document.Width}×{document.Height} px. (Ctrl+Z per annullare)");
        }
        catch (Exception ex)
        {
            setStatus($"Errore ritaglio selezione: {ex.Message}");
        }
    }

    public static void RemoveSelectedArea(
        Image<Rgba32>? document,
        AxisAlignedBox? activeSelectionBox,
        Action pushUndo,
        Action refreshView,
        Action<string> setStatus)
    {
        if (document is null) { setStatus("Nessuna immagine aperta."); return; }
        if (activeSelectionBox is null) { setStatus("Nessuna selezione attiva da rimuovere."); return; }

        try
        {
            var box = activeSelectionBox.Value;
            var minX = Math.Clamp(box.MinX, 0, document.Width);
            var maxX = Math.Clamp(box.MaxX, 0, document.Width);
            var minY = Math.Clamp(box.MinY, 0, document.Height);
            var maxY = Math.Clamp(box.MaxY, 0, document.Height);
            if (minX >= maxX || minY >= maxY)
            {
                setStatus("Area selezionata non valida.");
                return;
            }

            pushUndo();
            document.ProcessPixelRows(accessor =>
            {
                for (var y = minY; y < maxY; y++)
                {
                    var row = accessor.GetRowSpan(y);
                    for (var x = minX; x < maxX; x++)
                        row[x] = new Rgba32(0, 0, 0, 0);
                }
            });

            refreshView();
            setStatus($"Area selezionata rimossa: {maxX - minX}×{maxY - minY} px.");
        }
        catch (Exception ex)
        {
            setStatus($"Errore rimozione area selezionata: {ex.Message}");
        }
    }

    public static async Task CopyImageToClipboardAsync(
        Image<Rgba32>? document,
        AxisAlignedBox? activeSelectionBox,
        IClipboard? clipboard,
        Func<Image<Rgba32>, Bitmap?> toBitmap,
        Func<IClipboard, Bitmap, Task> setBitmapAndFlushAsync,
        Action<string> setStatus)
    {
        if (clipboard is null)
        {
            setStatus("Appunti non disponibili.");
            return;
        }

        if (document is null)
        {
            setStatus("Apri prima un'immagine.");
            return;
        }

        Image<Rgba32>? cropped = null;
        Bitmap? bmp = null;
        try
        {
            Image<Rgba32> pixelsSource = document;
            string detail;

            if (activeSelectionBox is not null)
            {
                var box = activeSelectionBox.Value;
                var minX = Math.Clamp(box.MinX, 0, document.Width);
                var maxX = Math.Clamp(box.MaxX, 0, document.Width);
                var minY = Math.Clamp(box.MinY, 0, document.Height);
                var maxY = Math.Clamp(box.MaxY, 0, document.Height);
                if (minX >= maxX || minY >= maxY)
                {
                    setStatus("Selezione non valida per la copia.");
                    return;
                }

                var clamped = new AxisAlignedBox(minX, minY, maxX, maxY);
                cropped = AtlasCropper.Crop(document, in clamped);
                pixelsSource = cropped;
                detail = $"{cropped.Width}×{cropped.Height} px (selezione ROI)";
            }
            else
            {
                detail = $"{document.Width}×{document.Height} px";
            }

            bmp = toBitmap(pixelsSource);
            if (bmp is null)
            {
                setStatus("Impossibile preparare l'immagine per gli appunti.");
                return;
            }

            await setBitmapAndFlushAsync(clipboard, bmp).ConfigureAwait(true);
            setStatus($"Copiato negli appunti: {detail}.");
        }
        catch (Exception ex)
        {
            setStatus($"Copia negli appunti: {ex.Message}");
        }
        finally
        {
            cropped?.Dispose();
            bmp?.Dispose();
        }
    }
}

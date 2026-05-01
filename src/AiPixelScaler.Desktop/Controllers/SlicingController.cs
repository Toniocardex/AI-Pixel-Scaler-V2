using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using AiPixelScaler.Core.Geometry;
using AiPixelScaler.Core.Pipeline.Normalization;
using AiPixelScaler.Core.Pipeline.Slicing;
using AiPixelScaler.Desktop.Utilities;
using Avalonia.Platform.Storage;
using SixLabors.ImageSharp;
using SixLabors.ImageSharp.Formats.Png;
using SixLabors.ImageSharp.PixelFormats;

namespace AiPixelScaler.Desktop.Controllers;

internal static class SlicingController
{
    public static List<SpriteCell>? RunGridSlice(
        Image<Rgba32>? document,
        string? rowsText,
        string? colsText,
        Action pushUndo,
        Action<int, int> setEditorGrid,
        Action<IReadOnlyList<SpriteCell>> setEditorCells,
        Action<string> setStatus)
    {
        if (document is null) return null;

        try
        {
            var rows = Math.Max(1, InputParsing.ParseInt(rowsText, 2));
            var cols = Math.Max(1, InputParsing.ParseInt(colsText, 2));
            pushUndo();
            var cells = GridSlicer.Slice(document.Width, document.Height, rows, cols).ToList();
            setEditorGrid(rows, cols);
            setEditorCells([]);
            setStatus($"Diviso in griglia {rows}×{cols}: trovati {cells.Count} sprite.");
            return cells;
        }
        catch (Exception ex)
        {
            setStatus($"Errore grid slice: {ex.Message}");
            return null;
        }
    }

    public static List<SpriteCell>? RunCcl(
        Image<Rgba32>? document,
        Action pushUndo,
        Action clearSliceGrid,
        Action<IReadOnlyList<SpriteCell>> setEditorCells,
        Action<string> setStatus)
    {
        if (document is null) return null;

        try
        {
            pushUndo();
            var cells = CclAutoSlicer.Slice(document).ToList();
            clearSliceGrid();
            setEditorCells(cells);
            setStatus($"Rilevamento automatico completato: trovati {cells.Count} sprite.");
            return cells;
        }
        catch (Exception ex)
        {
            setStatus($"Errore CCL: {ex.Message}");
            return null;
        }
    }

    public static async Task SaveSelectedFrameAsync(
        Image<Rgba32>? document,
        IReadOnlyList<SpriteCell> cells,
        int selectedIndex,
        IStorageProvider storageProvider,
        Action<string> setStatus)
    {
        if (document is null) { setStatus("Nessuna immagine aperta."); return; }
        if (selectedIndex < 0 || selectedIndex >= cells.Count)
        {
            setStatus("Seleziona prima un frame dalla lista.");
            return;
        }

        var cell = cells[selectedIndex];
        try
        {
            var suggested = $"{cell.Id}_{cell.BoundsInAtlas.Width}x{cell.BoundsInAtlas.Height}.png";
            var file = await storageProvider.SaveFilePickerAsync(new FilePickerSaveOptions
            {
                Title = $"Salva frame {cell.Id}",
                DefaultExtension = "png",
                FileTypeChoices = [new FilePickerFileType("PNG") { Patterns = ["*.png"] }],
                SuggestedFileName = suggested,
            });
            if (file is null) return;

            using var crop = AtlasCropper.Crop(document, cell.BoundsInAtlas);
            await using var stream = await file.OpenWriteAsync();
            crop.Save(stream, new PngEncoder());

            setStatus($"Frame {cell.Id} salvato — {cell.BoundsInAtlas.Width}×{cell.BoundsInAtlas.Height} px.");
        }
        catch (Exception ex)
        {
            setStatus($"Errore salvataggio frame: {ex.Message}");
        }
    }
}

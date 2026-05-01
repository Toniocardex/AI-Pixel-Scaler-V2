using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Threading.Tasks;
using AiPixelScaler.Core.Geometry;
using AiPixelScaler.Core.Pipeline.Export;
using AiPixelScaler.Core.Pipeline.Normalization;
using AiPixelScaler.Core.Pipeline.Slicing;
using AiPixelScaler.Desktop.Services;
using AiPixelScaler.Desktop.Utilities;
using Avalonia.Platform.Storage;
using SixLabors.ImageSharp;
using SixLabors.ImageSharp.Formats.Png;
using SixLabors.ImageSharp.PixelFormats;

namespace AiPixelScaler.Desktop.Controllers;

internal static class ExportController
{
    public static async Task ExportPngAsync(
        Image<Rgba32>? document,
        IReadOnlyList<SpriteCell> cells,
        double pivotX,
        double pivotY,
        bool useCustomCell,
        bool keepCellSize,
        bool indexedPng,
        string? customCellWText,
        string? customCellHText,
        IStorageProvider storageProvider,
        Action<string> setStatus)
    {
        if (document is null) return;

        try
        {
            var keepUniformCell = keepCellSize || useCustomCell;
            int? customW = null;
            int? customH = null;
            if (useCustomCell)
            {
                customW = Math.Max(1, InputParsing.ParseInt(customCellWText, 208));
                customH = Math.Max(1, InputParsing.ParseInt(customCellHText, 256));
            }

            using var layout = ExportLayoutBuilder.Build(document, cells, pivotX, pivotY, keepUniformCell, customW, customH);
            if (layout is null)
            {
                setStatus("Niente da esportare.");
                return;
            }

            var file = await storageProvider.SaveFilePickerAsync(new FilePickerSaveOptions
            {
                Title = "Salva atlas PNG",
                DefaultExtension = "png",
                FileTypeChoices = [new FilePickerFileType("PNG") { Patterns = ["*.png"] }],
                SuggestedFileName = "atlas.png"
            });
            if (file is null) return;

            await using (var s = await file.OpenWriteAsync())
            {
                if (indexedPng)
                    IndexedPngExporter.SaveWithWuQuantize(layout.Pack.Atlas, s);
                else
                    layout.Pack.Atlas.Save(s, new PngEncoder());
            }

            var mode = indexedPng ? " (palette 8-bit)" : "";
            var layoutMode = useCustomCell
                ? $" · uniforme custom {customW}×{customH}"
                : keepUniformCell ? " · celle uniformi (auto)" : " · compatto";
            setStatus($"Atlas PNG salvato{mode}{layoutMode} ({layout.Pack.Placements.Count} sprite).");
        }
        catch (Exception ex)
        {
            setStatus($"Errore export PNG: {ex.Message}");
        }
    }

    public static async Task ExportJsonAsync(
        Image<Rgba32>? document,
        IReadOnlyList<SpriteCell> cells,
        double pivotX,
        double pivotY,
        bool useCustomCell,
        bool keepCellSize,
        string? customCellWText,
        string? customCellHText,
        string? paletteIdText,
        IStorageProvider storageProvider,
        Action<string> setStatus)
    {
        if (document is null) return;

        try
        {
            var keepUniformCell = keepCellSize || useCustomCell;
            int? customW = null;
            int? customH = null;
            if (useCustomCell)
            {
                customW = Math.Max(1, InputParsing.ParseInt(customCellWText, 208));
                customH = Math.Max(1, InputParsing.ParseInt(customCellHText, 256));
            }

            using var layout = ExportLayoutBuilder.Build(document, cells, pivotX, pivotY, keepUniformCell, customW, customH);
            if (layout is null)
            {
                setStatus("Niente da esportare.");
                return;
            }

            var meta = new SpriteSheetMetadata
            {
                PaletteId = string.IsNullOrWhiteSpace(paletteIdText) ? null : paletteIdText.Trim(),
                Cells = layout.Entries.ToList()
            };
            var json = JsonExport.Serialize(meta);

            var file = await storageProvider.SaveFilePickerAsync(new FilePickerSaveOptions
            {
                Title = "Salva metadati JSON",
                DefaultExtension = "json",
                FileTypeChoices = [new FilePickerFileType("JSON") { Patterns = ["*.json"] }],
                SuggestedFileName = "spritesheet.json"
            });
            if (file is null) return;

            await using var s = await file.OpenWriteAsync();
            using var w = new StreamWriter(s);
            await w.WriteAsync(json);
            setStatus($"JSON salvato ({layout.Entries.Count} celle).");
        }
        catch (Exception ex)
        {
            setStatus($"Errore export JSON: {ex.Message}");
        }
    }

    public static async Task ExportTiledMapJsonAsync(
        Image<Rgba32>? document,
        IReadOnlyList<SpriteCell> cells,
        IStorageProvider storageProvider,
        Action<string> setStatus)
    {
        if (document is null) return;

        try
        {
            var exportCells = cells.Count > 0
                ? cells
                : new List<SpriteCell> { new("full", new AxisAlignedBox(0, 0, document.Width, document.Height)) };
            var tileW = exportCells[0].BoundsInAtlas.Width;
            var tileH = exportCells[0].BoundsInAtlas.Height;
            var mapCols = document.Width / Math.Max(1, tileW);
            var mapRows = document.Height / Math.Max(1, tileH);
            var json = TiledMapJson.BuildFromCells(mapCols, mapRows, tileW, tileH, exportCells, "atlas.png");
            var file = await storageProvider.SaveFilePickerAsync(new FilePickerSaveOptions
            {
                Title = "Salva mappa Tiled",
                DefaultExtension = "json",
                FileTypeChoices = [new FilePickerFileType("Tiled / JSON") { Patterns = ["*.json", "*.tmj"] }],
                SuggestedFileName = "map.json"
            });
            if (file is null) return;

            await using (var s = await file.OpenWriteAsync())
            {
                using var w = new StreamWriter(s);
                await w.WriteAsync(json);
            }
            setStatus($"Mappa Tiled salvata ({exportCells.Count} tile, {mapCols}×{mapRows}). Posiziona 'atlas.png' nella stessa cartella.");
        }
        catch (Exception ex)
        {
            setStatus($"Errore export Tiled: {ex.Message}");
        }
    }

    public static async Task ExportFramesZipAsync(
        Image<Rgba32>? document,
        IReadOnlyList<SpriteCell> cells,
        IStorageProvider storageProvider,
        Action<string> setStatus)
    {
        if (document is null) return;
        var list = new List<(string name, Image<Rgba32> img)>();
        try
        {
            if (cells.Count == 0)
            {
                var copy = document.Clone();
                list.Add(("frame0.png", copy));
            }
            else
            {
                for (var i = 0; i < cells.Count; i++)
                {
                    var c = cells[i];
                    var id = string.IsNullOrEmpty(c.Id) ? "cell" : SanitizeFileSegment(c.Id);
                    var name = $"{i:000}_{id}.png";
                    var crop = AtlasCropper.Crop(document, c.BoundsInAtlas);
                    if (crop.Width == 0) continue;
                    list.Add((name, crop));
                }
            }

            if (list.Count == 0)
            {
                setStatus("Nessun frame da esportare nello ZIP.");
                return;
            }

            var file = await storageProvider.SaveFilePickerAsync(new FilePickerSaveOptions
            {
                Title = "Salva archivio frame PNG",
                DefaultExtension = "zip",
                FileTypeChoices = [new FilePickerFileType("ZIP") { Patterns = ["*.zip"] }],
                SuggestedFileName = "frames.zip"
            });
            if (file is null)
            {
                foreach (var t in list) t.img.Dispose();
                return;
            }

            await using (var s = await file.OpenWriteAsync())
            {
                PngFrameZipWriter.Write(list, s);
            }
            setStatus($"ZIP creato: {list.Count} frame.");
        }
        catch (Exception ex)
        {
            setStatus($"Errore export ZIP: {ex.Message}");
        }
        finally
        {
            foreach (var t in list) t.img.Dispose();
        }
    }

    private static string SanitizeFileSegment(string id)
    {
        var invalid = Path.GetInvalidFileNameChars();
        var chars = id.ToCharArray();
        for (var i = 0; i < chars.Length; i++)
        {
            if (Array.IndexOf(invalid, chars[i]) >= 0)
                chars[i] = '_';
        }
        return new string(chars);
    }
}

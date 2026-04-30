using System;
using System.Collections.Generic;
using System.Linq;
using AiPixelScaler.Core.Geometry;
using AiPixelScaler.Core.Pipeline.Export;
using AiPixelScaler.Core.Pipeline.Normalization;
using AiPixelScaler.Core.Pipeline.Slicing;
using SixLabors.ImageSharp;
using SixLabors.ImageSharp.PixelFormats;

namespace AiPixelScaler.Desktop.Services;

internal static class ExportLayoutBuilder
{
    internal sealed class BuildResult : IDisposable
    {
        private readonly IReadOnlyList<Image<Rgba32>> _frames;
        public AtlasPacker.PackedLayout Pack { get; }
        public IReadOnlyList<SpriteCellEntry> Entries { get; }

        public BuildResult(
            AtlasPacker.PackedLayout pack,
            IReadOnlyList<SpriteCellEntry> entries,
            IReadOnlyList<Image<Rgba32>> frames)
        {
            Pack = pack;
            Entries = entries;
            _frames = frames;
        }

        public void Dispose()
        {
            Pack.Atlas.Dispose();
            foreach (var f in _frames) f.Dispose();
        }
    }

    public static BuildResult? Build(
        Image<Rgba32> document,
        IReadOnlyList<SpriteCell> cells,
        double pivotX,
        double pivotY,
        bool keepUniformCellSize = false,
        int? customCellWidth = null,
        int? customCellHeight = null)
    {
        var effectiveCells = cells.Count > 0
            ? cells
            : [new SpriteCell("full", new AxisAlignedBox(0, 0, document.Width, document.Height))];

        var items = new List<(string id, Image<Rgba32> img)>();
        foreach (var cell in effectiveCells)
        {
            var crop = AtlasCropper.Crop(document, cell.BoundsInAtlas);
            if (crop.Width == 0 || crop.Height == 0)
            {
                crop.Dispose();
                continue;
            }
            items.Add((cell.Id, crop));
        }

        if (items.Count == 0)
            return null;

        AtlasPacker.PackedLayout pack;
        if (customCellWidth.HasValue || customCellHeight.HasValue)
        {
            if (!customCellWidth.HasValue || !customCellHeight.HasValue)
                throw new InvalidOperationException("Export uniforme personalizzato richiede sia larghezza che altezza.");

            var requestedW = Math.Max(1, customCellWidth.Value);
            var requestedH = Math.Max(1, customCellHeight.Value);
            var maxFrameW = items.Max(i => i.img.Width);
            var maxFrameH = items.Max(i => i.img.Height);
            if (requestedW < maxFrameW || requestedH < maxFrameH)
                throw new InvalidOperationException(
                    $"Cella custom troppo piccola: richiesto {requestedW}×{requestedH}, ma serve almeno {maxFrameW}×{maxFrameH}.");

            pack = AtlasPacker.PackRowUniform(items, requestedW, requestedH);
        }
        else if (keepUniformCellSize)
        {
            var cellW = effectiveCells.Max(c => Math.Max(1, c.BoundsInAtlas.Width));
            var cellH = effectiveCells.Max(c => Math.Max(1, c.BoundsInAtlas.Height));
            pack = AtlasPacker.PackRowUniform(items, cellW, cellH);
        }
        else
        {
            pack = AtlasPacker.PackRow(items);
        }
        var entries = pack.Placements.Select(p => new SpriteCellEntry
        {
            Id = p.id,
            X = p.x,
            Y = p.y,
            Width = p.w,
            Height = p.h,
            PivotNdcX = pivotX,
            PivotNdcY = pivotY
        }).ToList();

        return new BuildResult(pack, entries, items.Select(x => x.img).ToList());
    }
}

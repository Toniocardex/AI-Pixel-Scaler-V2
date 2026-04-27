using AiPixelScaler.Core.Geometry;
using AiPixelScaler.Core.Pipeline.Normalization;
using AiPixelScaler.Core.Pipeline.Slicing;
using SixLabors.ImageSharp;
using SixLabors.ImageSharp.PixelFormats;
using SixLabors.ImageSharp.Processing;

namespace AiPixelScaler.Core.Pipeline.Editor;

/// <summary>
/// Workbench non-distruttivo per allineare i singoli frame di uno sprite-sheet.
///
/// Ogni cella diventa un <see cref="Frame"/> con:
///   • <c>Content</c>  — copia indipendente del contenuto della cella (± padding di overflow)
///   • <c>Offset</c>   — spostamento corrente rispetto alla posizione originale
///
/// Operazioni di allineamento:
///   • <see cref="Frame.AutoCenter"/>      — centra l'AABB opaca nel rettangolo cella
///   • <see cref="Frame.AlignToBaseline"/> — ai piedi (centra X, sticky bottom Y)
///   • <see cref="Frame.Reset"/>           — torna alla posizione originale
///
/// La composizione finale (<see cref="Compose"/>) crea un nuovo atlas applicando
/// gli offset correnti di ogni frame. L'atlas originale resta intatto fino al commit.
/// </summary>
public sealed class FrameSheet : IDisposable
{
    public sealed class Frame : IDisposable
    {
        public int Index { get; init; }
        public AxisAlignedBox Cell { get; init; }
        public int Padding { get; init; }
        public Image<Rgba32> Content { get; private set; } = null!;
        public Point Offset;       // (0,0) = posizione originale

        internal void SetContent(Image<Rgba32> content) => Content = content;
        public void Dispose() { Content?.Dispose(); Content = null!; }

        /// <summary>AABB dei pixel opachi nel content (coordinate Content, half-open).</summary>
        public AxisAlignedBox? OpaqueBoundsInContent(byte alphaThreshold = 1) =>
            CellCentering.FindOpaqueBox(Content, alphaThreshold);

        /// <summary>
        /// Centra l'AABB opaca nel rettangolo cella di destinazione.
        /// Composition: <c>atlasPos = Cell.Min + Offset + (contentPos − Padding)</c>.
        /// </summary>
        public bool AutoCenter(byte alphaThreshold = 1)
        {
            var aabb = OpaqueBoundsInContent(alphaThreshold);
            if (aabb is null) return false;
            var bb = aabb.Value;

            // centro AABB opaca, in coords Content
            var contentCx = bb.MinX + bb.Width  / 2;
            var contentCy = bb.MinY + bb.Height / 2;

            // Offset = cellHalf + Padding − contentCenter
            Offset = new Point(
                Cell.Width  / 2 - contentCx + Padding,
                Cell.Height / 2 - contentCy + Padding);
            return true;
        }

        /// <summary>
        /// Allinea ai piedi: centra X, attacca il bottom dell'AABB opaca al bottom della cella.
        /// </summary>
        public bool AlignToBaseline(byte alphaThreshold = 1)
        {
            var aabb = OpaqueBoundsInContent(alphaThreshold);
            if (aabb is null) return false;
            var bb = aabb.Value;

            var contentCx = bb.MinX + bb.Width / 2;
            var contentBottomIncl = bb.MaxY - 1;       // inclusive bottom

            // X: come AutoCenter
            // Y: contentBottomIncl − Padding + Offset.Y = Cell.H − 1
            //    → Offset.Y = Cell.H − 1 − contentBottomIncl + Padding
            Offset = new Point(
                Cell.Width      / 2 - contentCx + Padding,
                Cell.Height - 1 - contentBottomIncl + Padding);
            return true;
        }

        public void Reset() => Offset = new Point(0, 0);
    }

    private readonly List<Frame> _frames = new();
    public IReadOnlyList<Frame> Frames => _frames;
    public int AtlasWidth  { get; private init; }
    public int AtlasHeight { get; private init; }

    public static FrameSheet ExtractFromAtlas(
        Image<Rgba32> atlas, IReadOnlyList<SpriteCell> cells, int padding = 0)
    {
        padding = Math.Max(0, padding);
        var sheet = new FrameSheet
        {
            AtlasWidth  = atlas.Width,
            AtlasHeight = atlas.Height,
        };
        for (var i = 0; i < cells.Count; i++)
        {
            var cell = cells[i].BoundsInAtlas;
            var contentW = cell.Width  + 2 * padding;
            var contentH = cell.Height + 2 * padding;
            var content  = new Image<Rgba32>(contentW, contentH, new Rgba32(0, 0, 0, 0));

            for (var ly = 0; ly < contentH; ly++)
            for (var lx = 0; lx < contentW; lx++)
            {
                var sx = cell.MinX - padding + lx;
                var sy = cell.MinY - padding + ly;
                if (sx < 0 || sy < 0 || sx >= atlas.Width || sy >= atlas.Height) continue;
                content[lx, ly] = atlas[sx, sy];
            }

            var f = new Frame
            {
                Index   = i,
                Cell    = cell,
                Padding = padding,
            };
            f.SetContent(content);
            sheet._frames.Add(f);
        }
        return sheet;
    }

    /// <summary>Compone un nuovo atlas applicando gli offset correnti.</summary>
    public Image<Rgba32> Compose()
    {
        var dst = new Image<Rgba32>(AtlasWidth, AtlasHeight, new Rgba32(0, 0, 0, 0));
        foreach (var f in _frames)
        {
            var px = f.Cell.MinX + f.Offset.X - f.Padding;
            var py = f.Cell.MinY + f.Offset.Y - f.Padding;
            dst.Mutate(ctx => ctx.DrawImage(f.Content, new Point(px, py), 1f));
        }
        return dst;
    }

    public void AutoCenterAll(byte alphaThreshold = 1)
    {
        foreach (var f in _frames) f.AutoCenter(alphaThreshold);
    }

    public void AlignAllToBaseline(byte alphaThreshold = 1)
    {
        foreach (var f in _frames) f.AlignToBaseline(alphaThreshold);
    }

    public void ResetAll()
    {
        foreach (var f in _frames) f.Reset();
    }

    public void Dispose()
    {
        foreach (var f in _frames) f.Dispose();
        _frames.Clear();
    }
}

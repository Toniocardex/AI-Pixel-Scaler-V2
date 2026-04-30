using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using AiPixelScaler.Core.Editor;
using AiPixelScaler.Core.Geometry;
using AiPixelScaler.Core.Pipeline.Slicing;
using AiPixelScaler.Desktop.Imaging;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Input;
using Avalonia.Media;
using Avalonia.Media.Imaging;
using SixLabors.ImageSharp;
using SixLabors.ImageSharp.PixelFormats;

namespace AiPixelScaler.Desktop.Controls;

/// <summary>
/// Canvas interattivo: ordine di disegno — sfondo (checker), immagine atlas, overlay (griglia leggera).
/// </summary>
public class EditorSurface : Control
{
    // ─── Styled properties ────────────────────────────────────────────────────

    public static readonly StyledProperty<Bitmap?> BitmapProperty =
        AvaloniaProperty.Register<EditorSurface, Bitmap?>(nameof(Bitmap));

    public static readonly StyledProperty<double> ZoomProperty =
        AvaloniaProperty.Register<EditorSurface, double>(nameof(Zoom), 1.0);

    public static readonly StyledProperty<double> PanXProperty =
        AvaloniaProperty.Register<EditorSurface, double>(nameof(PanX));

    public static readonly StyledProperty<double> PanYProperty =
        AvaloniaProperty.Register<EditorSurface, double>(nameof(PanY));

    public static readonly StyledProperty<bool> ShowGridProperty =
        AvaloniaProperty.Register<EditorSurface, bool>(nameof(ShowGrid), true);

    public static readonly StyledProperty<int> WorldGridSizeProperty =
        AvaloniaProperty.Register<EditorSurface, int>(nameof(WorldGridSize), 16);

    public static readonly StyledProperty<int> SliceGridRowsProperty =
        AvaloniaProperty.Register<EditorSurface, int>(nameof(SliceGridRows));

    public static readonly StyledProperty<int> SliceGridColsProperty =
        AvaloniaProperty.Register<EditorSurface, int>(nameof(SliceGridCols));

    public static readonly StyledProperty<bool> SnapToGridProperty =
        AvaloniaProperty.Register<EditorSurface, bool>(nameof(SnapToGrid), false);

    public static readonly StyledProperty<int> SnapGridSizeProperty =
        AvaloniaProperty.Register<EditorSurface, int>(nameof(SnapGridSize), 16);

    /// <summary>
    /// Distanza massima in screen-pixel entro cui scatta lo snap.
    /// Per la griglia regolare (no celle) lo snap è sempre esatto (round).
    /// Per lo snap a bordi-cella, sotto questa soglia si aggancia, sopra si usa la griglia normale.
    /// </summary>
    public static readonly StyledProperty<int> SnapThresholdProperty =
        AvaloniaProperty.Register<EditorSurface, int>(nameof(SnapThreshold), 18);

    /// <summary>Overlay griglia allineamento (tile + gutter) per ritaglio atlas.</summary>
    public static readonly StyledProperty<bool> ShowAlignGridProperty =
        AvaloniaProperty.Register<EditorSurface, bool>(nameof(ShowAlignGrid));

    public static readonly StyledProperty<int> AlignGridOffsetXProperty =
        AvaloniaProperty.Register<EditorSurface, int>(nameof(AlignGridOffsetX));

    public static readonly StyledProperty<int> AlignGridOffsetYProperty =
        AvaloniaProperty.Register<EditorSurface, int>(nameof(AlignGridOffsetY));

    public static readonly StyledProperty<int> AlignGridCellWidthProperty =
        AvaloniaProperty.Register<EditorSurface, int>(nameof(AlignGridCellWidth), 32);

    public static readonly StyledProperty<int> AlignGridCellHeightProperty =
        AvaloniaProperty.Register<EditorSurface, int>(nameof(AlignGridCellHeight), 32);

    public static readonly StyledProperty<int> AlignGridSpacingXProperty =
        AvaloniaProperty.Register<EditorSurface, int>(nameof(AlignGridSpacingX));

    public static readonly StyledProperty<int> AlignGridSpacingYProperty =
        AvaloniaProperty.Register<EditorSurface, int>(nameof(AlignGridSpacingY));

    public static readonly StyledProperty<bool> IsEraserModeProperty =
        AvaloniaProperty.Register<EditorSurface, bool>(nameof(IsEraserMode), false);

    public static readonly StyledProperty<int> EraserRadiusProperty =
        AvaloniaProperty.Register<EditorSurface, int>(nameof(EraserRadius), 4);

    // ─── Frame edit (workbench) mode ─────────────────────────────────────────
    public static readonly StyledProperty<bool> IsTilePreviewModeProperty =
        AvaloniaProperty.Register<EditorSurface, bool>(nameof(IsTilePreviewMode), false);

    public static readonly StyledProperty<bool> IsFrameEditModeProperty =
        AvaloniaProperty.Register<EditorSurface, bool>(nameof(IsFrameEditMode), false);

    public static readonly StyledProperty<int> FrameSnapRadiusProperty =
        AvaloniaProperty.Register<EditorSurface, int>(nameof(FrameSnapRadius), 6);

    public static readonly StyledProperty<bool> FrameSnapEnabledProperty =
        AvaloniaProperty.Register<EditorSurface, bool>(nameof(FrameSnapEnabled), true);

    /// <summary>Quando attivo, click su una cella di <see cref="SpriteCells"/> emette <see cref="CellClicked"/>.</summary>
    public static readonly StyledProperty<bool> IsCellClickModeProperty =
        AvaloniaProperty.Register<EditorSurface, bool>(nameof(IsCellClickMode), false);

    // ─── Private state ────────────────────────────────────────────────────────

    private readonly Viewport2D _viewport = new();
    private Avalonia.Point _lastPointer;
    private bool _panning;
    private bool _midPanning;

    // Pipette
    private bool _pipetteArmed;
    private bool _pipetteDragCancel;
    private Avalonia.Point _pipettePress;
    private bool _isPipetteMode;

    private enum SelectionAdjustGrip
    {
        None,
        Move,
        N, Ne, E, Se, S, Sw, W, Nw
    }

    // Selection
    private bool _isSelectionMode;
    private bool _selDragging;
    private Avalonia.Point _selStartScreen;
    private Avalonia.Point _selCurScreen;
    private AxisAlignedBox? _committedSelection;   // selezione persistente (canvas mode)

    /// <summary>Ridimensionamento o spostamento della ROI già confermata (maniglie / interno).</summary>
    private bool _selAdjustDragging;
    private SelectionAdjustGrip _selAdjustGrip;
    private AxisAlignedBox _selAdjustBoxStart;
    private Avalonia.Point _selAdjustPointerWorldStart;

    // Eraser
    private bool _eraserDragging;
    private Avalonia.Point _eraserCurScreen;
    private int _lastEraserX = int.MinValue;
    private int _lastEraserY = int.MinValue;

    // Frame edit mode (workbench)
    private List<AxisAlignedBox> _frameCells = [];
    private int _selectedFrameIndex = -1;
    private bool _frameDragging;
    private Avalonia.Point _frameDragStartScreen;
    private List<WorkbenchFrameRender> _workbenchRenderFrames = [];

    // Floating paste overlay (owner: MainWindow — non fare Dispose qui)
    private Bitmap? _floatingOverlayBitmap;
    private int _floatingX, _floatingY, _floatingW, _floatingH;
    private bool _floatingDragging;
    private double _floatingGrabWx, _floatingGrabWy;

    // ─── Static brushes ───────────────────────────────────────────────────────

    private static readonly SolidColorBrush CheckerBrushA = new(Avalonia.Media.Color.FromArgb(0xff, 0x2a, 0x2a, 0x2e));
    private static readonly SolidColorBrush CheckerBrushB = new(Avalonia.Media.Color.FromArgb(0xff, 0x1e, 0x1e, 0x22));
    private static readonly SolidColorBrush InfoBgBrush   = new(Avalonia.Media.Color.FromArgb(190, 8, 8, 18));
    private static readonly SolidColorBrush InfoFgBrush   = new(Avalonia.Media.Color.FromArgb(255, 255, 255, 255));
    private static readonly SolidColorBrush EraserBrush   = new(Avalonia.Media.Color.FromArgb(50, 255, 80, 80));

    private static readonly Avalonia.Media.Color[] CellPalette =
    [
        Avalonia.Media.Color.FromArgb(180, 255, 80,  80),
        Avalonia.Media.Color.FromArgb(180, 80,  200, 255),
        Avalonia.Media.Color.FromArgb(180, 80,  255, 130),
        Avalonia.Media.Color.FromArgb(180, 255, 200, 60),
        Avalonia.Media.Color.FromArgb(180, 200, 80,  255),
        Avalonia.Media.Color.FromArgb(180, 255, 130, 40),
    ];

    private List<SpriteCell> _spriteCells = [];

    // ─── Public API ───────────────────────────────────────────────────────────

    public EditorSurface()
    {
        ClipToBounds = true;
        Focusable = true;
    }

    public List<SpriteCell> SpriteCells
    {
        get => _spriteCells;
        set { _spriteCells = value ?? []; InvalidateVisual(); }
    }

    public Bitmap? Bitmap
    {
        get => GetValue(BitmapProperty);
        set => SetValue(BitmapProperty, value);
    }

    public double Zoom
    {
        get => GetValue(ZoomProperty);
        set => SetValue(ZoomProperty, value);
    }

    public double PanX
    {
        get => GetValue(PanXProperty);
        set => SetValue(PanXProperty, value);
    }

    public double PanY
    {
        get => GetValue(PanYProperty);
        set => SetValue(PanYProperty, value);
    }

    public bool ShowGrid
    {
        get => GetValue(ShowGridProperty);
        set => SetValue(ShowGridProperty, value);
    }

    public int WorldGridSize
    {
        get => GetValue(WorldGridSizeProperty);
        set => SetValue(WorldGridSizeProperty, Math.Max(1, value));
    }

    public int SliceGridRows
    {
        get => GetValue(SliceGridRowsProperty);
        set => SetValue(SliceGridRowsProperty, value);
    }

    public int SliceGridCols
    {
        get => GetValue(SliceGridColsProperty);
        set => SetValue(SliceGridColsProperty, value);
    }

    public bool SnapToGrid
    {
        get => GetValue(SnapToGridProperty);
        set => SetValue(SnapToGridProperty, value);
    }

    public int SnapGridSize
    {
        get => GetValue(SnapGridSizeProperty);
        set => SetValue(SnapGridSizeProperty, Math.Max(1, value));
    }

    /// <summary>
    /// Soglia di aggancio in screen-pixel. Default 18 px — abbastanza grande da essere usabile
    /// anche a zoom 1×, abbastanza piccola da non disturbare selezioni libere fra le celle.
    /// </summary>
    public int SnapThreshold
    {
        get => GetValue(SnapThresholdProperty);
        set => SetValue(SnapThresholdProperty, Math.Max(1, value));
    }

    public bool ShowAlignGrid
    {
        get => GetValue(ShowAlignGridProperty);
        set => SetValue(ShowAlignGridProperty, value);
    }

    public int AlignGridOffsetX
    {
        get => GetValue(AlignGridOffsetXProperty);
        set => SetValue(AlignGridOffsetXProperty, Math.Max(0, value));
    }

    public int AlignGridOffsetY
    {
        get => GetValue(AlignGridOffsetYProperty);
        set => SetValue(AlignGridOffsetYProperty, Math.Max(0, value));
    }

    public int AlignGridCellWidth
    {
        get => GetValue(AlignGridCellWidthProperty);
        set => SetValue(AlignGridCellWidthProperty, Math.Max(1, value));
    }

    public int AlignGridCellHeight
    {
        get => GetValue(AlignGridCellHeightProperty);
        set => SetValue(AlignGridCellHeightProperty, Math.Max(1, value));
    }

    public int AlignGridSpacingX
    {
        get => GetValue(AlignGridSpacingXProperty);
        set => SetValue(AlignGridSpacingXProperty, Math.Max(0, value));
    }

    public int AlignGridSpacingY
    {
        get => GetValue(AlignGridSpacingYProperty);
        set => SetValue(AlignGridSpacingYProperty, Math.Max(0, value));
    }

    /// <summary>Modalità gomma: trascina per cancellare pixel (A→0) nell'immagine.</summary>
    public bool IsEraserMode
    {
        get => GetValue(IsEraserModeProperty);
        set
        {
            SetValue(IsEraserModeProperty, value);
            Cursor = value ? new Cursor(StandardCursorType.Cross) : null;
            InvalidateVisual();
        }
    }

    /// <summary>Raggio del pennello gomma in pixel-immagine.</summary>
    public int EraserRadius
    {
        get => GetValue(EraserRadiusProperty);
        set => SetValue(EraserRadiusProperty, Math.Max(1, value));
    }

    /// <summary>Renderizza la bitmap come pattern 3×3 per visualizzare seam tileable in tempo reale.</summary>
    public bool IsTilePreviewMode
    {
        get => GetValue(IsTilePreviewModeProperty);
        set => SetValue(IsTilePreviewModeProperty, value);
    }

    /// <summary>Modalità workbench: click seleziona un frame, drag lo sposta nella sua cella.</summary>
    public bool IsFrameEditMode
    {
        get => GetValue(IsFrameEditModeProperty);
        set
        {
            SetValue(IsFrameEditModeProperty, value);
            if (!value) { _selectedFrameIndex = -1; _frameDragging = false; }
            InvalidateVisual();
        }
    }

    public int FrameSnapRadius
    {
        get => GetValue(FrameSnapRadiusProperty);
        set => SetValue(FrameSnapRadiusProperty, Math.Max(0, value));
    }

    public bool FrameSnapEnabled
    {
        get => GetValue(FrameSnapEnabledProperty);
        set => SetValue(FrameSnapEnabledProperty, value);
    }

    public bool IsCellClickMode
    {
        get => GetValue(IsCellClickModeProperty);
        set => SetValue(IsCellClickModeProperty, value);
    }

    /// <summary>
    /// Imposta i frame del workbench (Avalonia bitmap + offset). Sostituisce e disposa i precedenti.
    /// Render diretto: niente <see cref="SetSourceImage"/>, niente recompose dell'atlas durante il drag.
    /// </summary>
    public void SetWorkbenchFrames(List<WorkbenchFrameRender> frames, int selectedIndex = -1)
    {
        foreach (var old in _workbenchRenderFrames) old.Dispose();
        _workbenchRenderFrames = frames;
        _frameCells = frames.Select(f => f.Cell).ToList();
        _selectedFrameIndex = selectedIndex < 0 || selectedIndex >= frames.Count ? -1 : selectedIndex;
        InvalidateVisual();
    }

    /// <summary>Aggiorna l'offset di un singolo frame (chiamato durante il drag, no recompose).</summary>
    public void UpdateFrameOffset(int idx, int x, int y)
    {
        if (idx < 0 || idx >= _workbenchRenderFrames.Count) return;
        _workbenchRenderFrames[idx].Offset = new Avalonia.Point(x, y);
        InvalidateVisual();
    }

    public void ClearWorkbenchFrames()
    {
        foreach (var f in _workbenchRenderFrames) f.Dispose();
        _workbenchRenderFrames = [];
        _frameCells = [];
        _selectedFrameIndex = -1;
        InvalidateVisual();
    }

    public event EventHandler<int>? CellClicked;

    /// <summary>Imposta le celle visibili in workbench mode + indice selezionato.</summary>
    public void SetFrameCells(IReadOnlyList<AxisAlignedBox> cells, int selectedIndex = -1)
    {
        _frameCells = cells.ToList();
        _selectedFrameIndex = selectedIndex < 0 || selectedIndex >= _frameCells.Count ? -1 : selectedIndex;
        InvalidateVisual();
    }

    public int SelectedFrameIndex => _selectedFrameIndex;

    public event EventHandler<int>? FrameSelected;
    public event EventHandler<FrameDragEventArgs>? FrameDragged;

    public bool IsPipetteMode
    {
        get => _isPipetteMode;
        set
        {
            if (_isPipetteMode == value) return;
            _isPipetteMode = value;
            _pipetteArmed = false;
            Cursor = value ? new Cursor(StandardCursorType.Cross) : null;
        }
    }

    public bool IsSelectionMode
    {
        get => _isSelectionMode;
        set
        {
            if (_isSelectionMode == value) return;
            _isSelectionMode = value;
            _selDragging = false;
            InvalidateVisual();
        }
    }

    // ─── Events ───────────────────────────────────────────────────────────────

    public event EventHandler<ImagePixelPickedEventArgs>?       ImagePixelPicked;
    public event EventHandler<ImageSelectionCompletedEventArgs>? ImageSelectionCompleted;

    /// <summary>
    /// ROI committed aggiornata (ridimensionamento o spostamento con maniglie). Non sostituisce <see cref="ImageSelectionCompleted"/> al primo disegno.
    /// </summary>
    public event EventHandler<ImageSelectionCompletedEventArgs>? CommittedSelectionEdited;

    /// <summary>Nuova posizione (angolo alto-sinistra in pixel mondo) durante il drag dell’overlay fluttuante.</summary>
    public event EventHandler<FloatingOverlayMoveEventArgs>? FloatingOverlayMoved;

    /// <summary>Fired continuamente durante il drag della gomma (coordinate in pixel-immagine).</summary>
    public event EventHandler<EraserStrokeEventArgs>? EraserStroke;

    /// <summary>Fired al rilascio del mouse dopo una passata di gomma.</summary>
    public event EventHandler? EraserStrokeEnded;

    // ─── Image helpers ────────────────────────────────────────────────────────

    public void SetSourceImage(Image<Rgba32>? image) =>
        Bitmap = Rgba32BitmapBridge.ToBitmap(image);

    /// <summary>
    /// Centra l'immagine nella viewport mantenendo lo zoom corrente.
    /// Ritorna false se non c'è una bitmap valida o il controllo non ha dimensioni.
    /// </summary>
    public bool CenterImageInViewport()
    {
        var bmp = Bitmap;
        if (bmp is null || bmp.PixelSize.Width <= 0 || bmp.PixelSize.Height <= 0) return false;
        if (Bounds.Width <= 0 || Bounds.Height <= 0) return false;

        var z = Math.Max(Zoom, 0.0001);
        PanX = (Bounds.Width - bmp.PixelSize.Width * z) * 0.5;
        PanY = (Bounds.Height - bmp.PixelSize.Height * z) * 0.5;
        InvalidateVisual();
        return true;
    }

    /// <summary>Imposta lo zoom verso un punto schermo (come la rotellina), clamp 0.05–64.</summary>
    public void SetZoomTowardScreenPoint(double newZoom, double screenX, double screenY)
    {
        var clamped = Math.Clamp(newZoom, 0.05, 64.0);
        _viewport.Zoom = Zoom;
        _viewport.PanX = PanX;
        _viewport.PanY = PanY;
        _viewport.ZoomAtScreenPoint(clamped, screenX, screenY);
        Zoom = _viewport.Zoom;
        PanX = _viewport.PanX;
        PanY = _viewport.PanY;
    }

    /// <summary>
    /// Larghezza/altezza mondo in pixel (coerenti col rendering) per limiti di pan.
    /// Workbench: massimo delle estensioni delle celle frame; tile preview: atlas ripetuto 3×3.
    /// </summary>
    public bool TryGetPanWorldSize(out int worldW, out int worldH)
    {
        worldW = worldH = 0;
        var inWorkbench = IsFrameEditMode && _workbenchRenderFrames.Count > 0;
        if (inWorkbench)
        {
            foreach (var f in _workbenchRenderFrames)
            {
                if (f.Cell.MaxX > worldW) worldW = f.Cell.MaxX;
                if (f.Cell.MaxY > worldH) worldH = f.Cell.MaxY;
            }
            return worldW > 0 && worldH > 0;
        }

        var bmp = Bitmap;
        if (bmp is null || bmp.PixelSize.Width <= 0 || bmp.PixelSize.Height <= 0) return false;

        worldW = bmp.PixelSize.Width;
        worldH = bmp.PixelSize.Height;
        if (IsTilePreviewMode)
        {
            worldW *= 3;
            worldH *= 3;
        }

        return true;
    }

    /// <summary>
    /// Imposta/rimuove la selezione committed (area evidenziata persistente dopo il drag).
    /// Usata dalla modalità canvas-selezione per mostrare l'area selezionata anche dopo il rilascio.
    /// </summary>
    public void SetCommittedSelection(AxisAlignedBox? box)
    {
        _committedSelection = box;
        InvalidateVisual();
    }

    /// <summary>Overlay incolla fluttuante (bitmap già scalata per anteprima). Non dispone <paramref name="overlay"/>.</summary>
    public void SetFloatingOverlay(Bitmap? overlay, int destX, int destY, int destW, int destH)
    {
        _floatingOverlayBitmap = overlay;
        _floatingX = destX;
        _floatingY = destY;
        _floatingW = Math.Max(1, destW);
        _floatingH = Math.Max(1, destH);
        _floatingDragging = false;
        InvalidateVisual();
    }

    public void UpdateFloatingOverlayPosition(int x, int y)
    {
        _floatingX = x;
        _floatingY = y;
        InvalidateVisual();
    }

    public void ClearFloatingOverlay()
    {
        _floatingOverlayBitmap = null;
        _floatingDragging = false;
        _floatingW = _floatingH = 0;
        InvalidateVisual();
    }

    public bool IsFloatingOverlayActive => _floatingOverlayBitmap is not null;

    /// <summary>
    /// Aggiorna solo le righe [yMin, yMax) del bitmap esistente senza ricrearlo.
    /// Ideale per la gomma: copia solo i pixel modificati, poi invalida il visual.
    /// </summary>
    public void UpdateBitmapRegion(Image<Rgba32> image, int yMin, int yMax)
    {
        if (Bitmap is WriteableBitmap wb)
        {
            Rgba32BitmapBridge.UpdateRows(wb, image, yMin, yMax);
            InvalidateVisual();
        }
        else
        {
            // Fallback: ricrea il bitmap intero (non dovrebbe accadere in uso normale)
            SetSourceImage(image);
        }
    }

    public static (double wx, double wy) ScreenToImageFloat(Avalonia.Point screen, double panX, double panY, double zoom)
    {
        var z = Math.Max(zoom, 0.0001);
        return ((screen.X - panX) / z, (screen.Y - panY) / z);
    }

    public bool TryScreenToImagePixel(Avalonia.Point screenPoint, out int px, out int py)
    {
        px = py = 0;
        var bmp = Bitmap;
        if (bmp is null || bmp.PixelSize.Width == 0) return false;
        var z = Math.Max(Zoom, 0.0001);
        var wx = (screenPoint.X - PanX) / z;
        var wy = (screenPoint.Y - PanY) / z;
        px = (int)Math.Floor(wx);
        py = (int)Math.Floor(wy);
        return px >= 0 && py >= 0 && px < bmp.PixelSize.Width && py < bmp.PixelSize.Height;
    }

    // ─── Property invalidation ────────────────────────────────────────────────

    protected override void OnPropertyChanged(AvaloniaPropertyChangedEventArgs change)
    {
        base.OnPropertyChanged(change);
        if (change.Property == ZoomProperty         || change.Property == PanXProperty
         || change.Property == PanYProperty          || change.Property == BitmapProperty
         || change.Property == ShowGridProperty      || change.Property == WorldGridSizeProperty
         || change.Property == SliceGridRowsProperty
         || change.Property == SliceGridColsProperty || change.Property == SnapToGridProperty
         || change.Property == SnapGridSizeProperty  || change.Property == ShowAlignGridProperty
         || change.Property == AlignGridOffsetXProperty || change.Property == AlignGridOffsetYProperty
         || change.Property == AlignGridCellWidthProperty || change.Property == AlignGridCellHeightProperty
         || change.Property == AlignGridSpacingXProperty || change.Property == AlignGridSpacingYProperty
         || change.Property == IsEraserModeProperty
         || change.Property == EraserRadiusProperty  || change.Property == IsFrameEditModeProperty
         || change.Property == FrameSnapRadiusProperty || change.Property == FrameSnapEnabledProperty
         || change.Property == IsTilePreviewModeProperty
         || change.Property == IsCellClickModeProperty)
            InvalidateVisual();
    }

    // ─── Render ───────────────────────────────────────────────────────────────

    public override void Render(DrawingContext context)
    {
        base.Render(context);
        var w = Bounds.Width;
        var h = Bounds.Height;
        if (w <= 0 || h <= 0) return;

        _viewport.Zoom = Zoom;
        _viewport.PanX = PanX;
        _viewport.PanY = PanY;

        DrawCheckerboard(context, w, h);

        var bmp = Bitmap;
        var inWorkbench = IsFrameEditMode && _workbenchRenderFrames.Count > 0;

        // Determina le dimensioni del world: dal Bitmap, oppure (in workbench) dai cell bounds
        int worldW, worldH;
        if (inWorkbench)
        {
            worldW = 0; worldH = 0;
            foreach (var f in _workbenchRenderFrames)
            {
                if (f.Cell.MaxX > worldW) worldW = f.Cell.MaxX;
                if (f.Cell.MaxY > worldH) worldH = f.Cell.MaxY;
            }
            if (worldW == 0 || worldH == 0) return;
        }
        else
        {
            if (bmp is null || bmp.PixelSize.Width == 0) return;
            worldW = bmp.PixelSize.Width;
            worldH = bmp.PixelSize.Height;
        }

        var m = Matrix.Identity;
        m *= Matrix.CreateScale(_viewport.Zoom, _viewport.Zoom);
        m *= Matrix.CreateTranslation(_viewport.PanX, _viewport.PanY);

        using (context.PushTransform(m))
        {
            if (inWorkbench)
            {
                // RENDER DIRETTO dei frame con i loro offset correnti.
                // Niente atlas compose, niente Avalonia bitmap conversion: 60 FPS fluidi sul drag.
                foreach (var f in _workbenchRenderFrames)
                {
                    var dstX = f.Cell.MinX + f.Offset.X - f.Padding;
                    var dstY = f.Cell.MinY + f.Offset.Y - f.Padding;
                    context.DrawImage(f.Content,
                        new Rect(0, 0, f.ContentW, f.ContentH),
                        new Rect(dstX, dstY, f.ContentW, f.ContentH));
                }
            }
            else if (IsTilePreviewMode && bmp is not null)
            {
                // Pattern 3×3 con il tile centrale a (0,0) e 8 cloni attorno
                for (var ty = -1; ty <= 1; ty++)
                for (var tx = -1; tx <= 1; tx++)
                {
                    context.DrawImage(bmp,
                        new Rect(0, 0, worldW, worldH),
                        new Rect(tx * worldW, ty * worldH, worldW, worldH));
                }
                var hPen = new Pen(new SolidColorBrush(Avalonia.Media.Color.FromArgb(180, 255, 200, 0)),
                                   1.5 / Math.Max(_viewport.Zoom, 0.0001));
                context.DrawRectangle(hPen, new Rect(0, 0, worldW, worldH));
            }
            else if (bmp is not null)
            {
                context.DrawImage(bmp, new Rect(0, 0, worldW, worldH), new Rect(0, 0, worldW, worldH));
            }

            if (!inWorkbench && _floatingOverlayBitmap is not null && _floatingW > 0 && _floatingH > 0)
            {
                using (context.PushRenderOptions(new RenderOptions
                       { BitmapInterpolationMode = BitmapInterpolationMode.None }))
                {
                    var srcW = _floatingOverlayBitmap.PixelSize.Width;
                    var srcH = _floatingOverlayBitmap.PixelSize.Height;
                    context.DrawImage(
                        _floatingOverlayBitmap,
                        new Rect(0, 0, srcW, srcH),
                        new Rect(_floatingX, _floatingY, _floatingW, _floatingH));
                }

                var fp = new Pen(new SolidColorBrush(Avalonia.Media.Color.FromArgb(255, 255, 220, 80)), 1.2 / Math.Max(_viewport.Zoom, 0.0001))
                {
                    DashStyle = new DashStyle([3.0, 3.0], 0)
                };
                context.DrawRectangle(fp, new Rect(_floatingX, _floatingY, _floatingW, _floatingH));
            }
        }

        if (ShowGrid)
        {
            using (context.PushTransform(m))
                DrawLightGrid(context, worldW, worldH, WorldGridSize, _viewport.Zoom);
        }

        if (SnapToGrid && SnapGridSize > 0)
        {
            using (context.PushTransform(m))
                DrawSnapGrid(context, worldW, worldH, SnapGridSize, _viewport.Zoom);
        }

        if (SliceGridRows > 0 && SliceGridCols > 0)
        {
            using (context.PushTransform(m))
                DrawSliceGrid(context, worldW, worldH, SliceGridRows, SliceGridCols, _viewport.Zoom);
        }

        if (ShowAlignGrid && AlignGridCellWidth >= 1 && AlignGridCellHeight >= 1)
        {
            using (context.PushTransform(m))
                DrawAlignGrid(context, worldW, worldH,
                    AlignGridOffsetX, AlignGridOffsetY,
                    AlignGridCellWidth, AlignGridCellHeight,
                    AlignGridSpacingX, AlignGridSpacingY,
                    _viewport.Zoom);
        }

        if (_spriteCells.Count > 0 && SliceGridRows == 0 && SliceGridCols == 0)
        {
            using (context.PushTransform(m))
                DrawCellOverlay(context, _spriteCells, _viewport.Zoom);
        }

        // ── Selezione (drag) ───────────────────────────────────────────────────
        if (IsSelectionMode && _selDragging)
        {
            var box = BuildSelectionBoxInWorld(_selStartScreen, _selCurScreen, worldW, worldH);
            if (box is { IsEmpty: false })
            {
                var t = 1.5 / Math.Max(_viewport.Zoom, 0.0001);
                var selPen = new Pen(new SolidColorBrush(Avalonia.Media.Color.FromArgb(255, 80, 220, 255)), t)
                {
                    LineCap = PenLineCap.Square,
                    DashStyle = new DashStyle([4.0, 3.0], 0)
                };
                using (context.PushTransform(m))
                {
                    context.DrawRectangle(selPen, new Rect(box.MinX, box.MinY, box.Width, box.Height));

                    if (SnapToGrid && SnapGridSize > 0)
                    {
                        var (cwx, cwy) = ScreenToImageFloat(_selCurScreen, PanX, PanY, Zoom);
                        var worldThresh = SnapThreshold / Math.Max(_viewport.Zoom, 0.0001);
                        var snappedX = SnapCoordToLines(cwx, BuildXSnapLines(), SnapGridSize, worldThresh);
                        var snappedY = SnapCoordToLines(cwy, BuildYSnapLines(), SnapGridSize, worldThresh);
                        var dotR = 4.0 / Math.Max(_viewport.Zoom, 0.0001);
                        context.FillRectangle(
                            new SolidColorBrush(Avalonia.Media.Color.FromArgb(220, 255, 165, 0)),
                            new Rect(snappedX - dotR, snappedY - dotR, dotR * 2, dotR * 2));

                        // Guide snap ai bordi delle celle (solo se celle definite)
                        if (_spriteCells.Count > 0)
                        {
                            var snpPen = new Pen(
                                new SolidColorBrush(Avalonia.Media.Color.FromArgb(110, 255, 165, 0)),
                                0.8 / Math.Max(_viewport.Zoom, 0.0001));
                            context.DrawLine(snpPen, new Avalonia.Point(box.MinX, 0), new Avalonia.Point(box.MinX, worldH));
                            context.DrawLine(snpPen, new Avalonia.Point(box.MaxX, 0), new Avalonia.Point(box.MaxX, worldH));
                            context.DrawLine(snpPen, new Avalonia.Point(0, box.MinY), new Avalonia.Point(worldW, box.MinY));
                            context.DrawLine(snpPen, new Avalonia.Point(0, box.MaxY), new Avalonia.Point(worldW, box.MaxY));
                        }
                    }
                }

                // Info label dimensioni — in screen space (fuori dal transform)
                DrawSelectionInfo(context, box.Width, box.Height, _selCurScreen, w, h);
            }
        }

        // ── Selezione committed (canvas-mode, persiste dopo il drag) ──────────
        if (IsSelectionMode && !_selDragging && _committedSelection is { IsEmpty: false } cs)
        {
            var t = 1.5 / Math.Max(_viewport.Zoom, 0.0001);
            var selPen = new Pen(new SolidColorBrush(Avalonia.Media.Color.FromArgb(210, 80, 220, 255)), t)
            {
                LineCap = PenLineCap.Square,
                DashStyle = new DashStyle([4.0, 3.0], 0)
            };
            var fillBrush = new SolidColorBrush(Avalonia.Media.Color.FromArgb(22, 80, 200, 255));
            using (context.PushTransform(m))
            {
                context.FillRectangle(fillBrush, new Rect(cs.MinX, cs.MinY, cs.Width, cs.Height));
                context.DrawRectangle(selPen, new Rect(cs.MinX, cs.MinY, cs.Width, cs.Height));

                // Maniglie agli angoli e ai lati (hit-test allineato agli stessi punti)
                var hr = 3.5 / Math.Max(_viewport.Zoom, 0.0001);
                var hb = new SolidColorBrush(Avalonia.Media.Color.FromArgb(255, 80, 220, 255));
                foreach (var (hx, hy) in CommittedSelectionGripPoints(cs))
                    context.FillRectangle(hb, new Rect(hx - hr, hy - hr, hr * 2, hr * 2));
            }
            // Info label (in screen space)
            var infoScrPt = new Avalonia.Point(
                cs.MaxX * _viewport.Zoom + _viewport.PanX,
                cs.MaxY * _viewport.Zoom + _viewport.PanY + 4);
            DrawSelectionInfo(context, cs.Width, cs.Height, infoScrPt, w, h);
        }

        // ── Workbench overlay (modalità frame edit) ───────────────────────────
        if (IsFrameEditMode && _frameCells.Count > 0)
        {
            using (context.PushTransform(m))
                DrawFrameWorkbench(context, _frameCells, _selectedFrameIndex, _viewport.Zoom);
        }

        // ── Cursore gomma ──────────────────────────────────────────────────────
        if (IsEraserMode && (IsPointerOver || _eraserDragging))
        {
            var screenR = EraserRadius * Math.Max(_viewport.Zoom, 0.0001);
            var eraserOutlinePen = new Pen(new SolidColorBrush(Avalonia.Media.Color.FromArgb(200, 255, 80, 80)), 1.5);
            context.DrawEllipse(EraserBrush, eraserOutlinePen,
                _eraserCurScreen,
                screenR, screenR);
        }
    }

    // ─── Selection info label ─────────────────────────────────────────────────

    private static void DrawSelectionInfo(DrawingContext ctx, int selW, int selH,
                                          Avalonia.Point cursorScreen, double canvasW, double canvasH)
    {
        var text = $"  {selW} × {selH} px  ";
        var ft = new FormattedText(
            text,
            CultureInfo.InvariantCulture,
            FlowDirection.LeftToRight,
            new Typeface("default"),
            11,
            InfoFgBrush);

        const double offsetX = 14;
        const double offsetY = 14;
        const double padV = 3;
        var bw = ft.Width;
        var bh = ft.Height + padV * 2;

        var lx = cursorScreen.X + offsetX;
        var ly = cursorScreen.Y + offsetY;

        // mantieni dentro i bordi del canvas
        if (lx + bw > canvasW - 4) lx = cursorScreen.X - bw - offsetX;
        if (ly + bh > canvasH - 4) ly = cursorScreen.Y - bh - offsetY;

        var bgRect = new Rect(lx, ly, bw, bh);
        ctx.FillRectangle(InfoBgBrush, bgRect, 4);
        ctx.DrawText(ft, new Avalonia.Point(lx, ly + padV));
    }

    // ─── Input: pointer ───────────────────────────────────────────────────────

    protected override void OnPointerWheelChanged(PointerWheelEventArgs e)
    {
        base.OnPointerWheelChanged(e);
        var p = e.GetPosition(this);
        var delta = e.Delta.Y;
        if (delta == 0) return;
        var factor  = delta > 0 ? 1.1 : 1 / 1.1;
        var newZoom = Zoom * factor;
        SetZoomTowardScreenPoint(newZoom, p.X, p.Y);
        e.Handled = true;
    }

    protected override void OnPointerPressed(PointerPressedEventArgs e)
    {
        base.OnPointerPressed(e);
        var cur = e.GetCurrentPoint(this);
        var pos = e.GetPosition(this);

        if (cur.Properties.IsMiddleButtonPressed)
        {
            _midPanning = true;
            _lastPointer = pos;
            e.Pointer.Capture(this);
            e.Handled = true;
            return;
        }
        if (!cur.Properties.IsLeftButtonPressed) return;

        if (_floatingOverlayBitmap is not null
            && !IsEraserMode
            && !IsPipetteMode
            && !IsSelectionMode
            && !IsCellClickMode
            && !(IsFrameEditMode && _frameCells.Count > 0)
            && !IsTilePreviewMode)
        {
            var (fwx, fwy) = ScreenToImageFloat(pos, PanX, PanY, Zoom);
            if (fwx >= _floatingX && fwy >= _floatingY
                && fwx < _floatingX + _floatingW && fwy < _floatingY + _floatingH)
            {
                _floatingDragging = true;
                _floatingGrabWx = fwx - _floatingX;
                _floatingGrabWy = fwy - _floatingY;
                e.Pointer.Capture(this);
                e.Handled = true;
                return;
            }
        }

        if (IsEraserMode)
        {
            _eraserDragging = true;
            _eraserCurScreen = pos;
            e.Pointer.Capture(this);
            FireEraserStroke(pos);
            e.Handled = true;
            return;
        }
        if (IsFrameEditMode && _frameCells.Count > 0)
        {
            var (wx, wy) = ScreenToImageFloat(pos, PanX, PanY, Zoom);
            var hit = -1;
            for (var i = 0; i < _frameCells.Count; i++)
            {
                var c = _frameCells[i];
                if (wx >= c.MinX && wy >= c.MinY && wx < c.MaxX && wy < c.MaxY) { hit = i; break; }
            }
            if (hit >= 0)
            {
                _selectedFrameIndex = hit;
                FrameSelected?.Invoke(this, hit);
                _frameDragging = true;
                _frameDragStartScreen = pos;
                e.Pointer.Capture(this);
                InvalidateVisual();
                e.Handled = true;
                return;
            }
        }
        if (IsCellClickMode && _spriteCells.Count > 0)
        {
            var (wx, wy) = ScreenToImageFloat(pos, PanX, PanY, Zoom);
            for (var i = 0; i < _spriteCells.Count; i++)
            {
                var c = _spriteCells[i].BoundsInAtlas;
                if (wx >= c.MinX && wy >= c.MinY && wx < c.MaxX && wy < c.MaxY)
                {
                    CellClicked?.Invoke(this, i);
                    e.Handled = true;
                    return;
                }
            }
        }
        if (IsSelectionMode)
        {
            var bmpSel = Bitmap;
            if (bmpSel is { PixelSize.Width: > 0, PixelSize.Height: > 0 }
                && !_selDragging
                && _committedSelection is { IsEmpty: false } csHit)
            {
                var grip = HitTestCommittedSelectionGrip(pos, csHit);
                if (grip != SelectionAdjustGrip.None)
                {
                    _selAdjustDragging = true;
                    _selAdjustGrip = grip;
                    _selAdjustBoxStart = csHit;
                    var (wpx, wpy) = ScreenToImageFloat(pos, PanX, PanY, Zoom);
                    var (spx, spy) = SnapWorldPoint(wpx, wpy);
                    _selAdjustPointerWorldStart = new Avalonia.Point(spx, spy);
                    e.Pointer.Capture(this);
                    InvalidateVisual();
                    e.Handled = true;
                    return;
                }
            }

            _selDragging    = true;
            _selStartScreen = pos;
            _selCurScreen   = pos;
            e.Pointer.Capture(this);
            InvalidateVisual();
            e.Handled = true;
            return;
        }
        if (IsPipetteMode)
        {
            _pipetteArmed    = true;
            _pipetteDragCancel = false;
            _pipettePress    = pos;
            e.Pointer.Capture(this);
            return;
        }
        _panning = true;
        _lastPointer = pos;
        e.Pointer.Capture(this);
    }

    protected override void OnPointerReleased(PointerReleasedEventArgs e)
    {
        base.OnPointerReleased(e);
        var pos = e.GetPosition(this);

        if (_midPanning && e.InitialPressMouseButton == MouseButton.Middle)
        {
            _midPanning = false;
            e.Pointer.Capture(null);
            return;
        }
        if (_eraserDragging)
        {
            _eraserDragging  = false;
            _eraserCurScreen = pos;
            _lastEraserX = int.MinValue;
            _lastEraserY = int.MinValue;
            e.Pointer.Capture(null);
            EraserStrokeEnded?.Invoke(this, EventArgs.Empty);
            InvalidateVisual();
            return;
        }
        if (_frameDragging)
        {
            _frameDragging = false;
            e.Pointer.Capture(null);
            var z = Math.Max(Zoom, 0.0001);
            var dxImage = (pos.X - _frameDragStartScreen.X) / z;
            var dyImage = (pos.Y - _frameDragStartScreen.Y) / z;
            FrameDragged?.Invoke(this, new FrameDragEventArgs(
                _selectedFrameIndex,
                (int)Math.Round(dxImage),
                (int)Math.Round(dyImage),
                isCommit: true));
            InvalidateVisual();
            return;
        }
        if (_floatingDragging)
        {
            _floatingDragging = false;
            e.Pointer.Capture(null);
            InvalidateVisual();
            return;
        }
        if (_selAdjustDragging)
        {
            _selAdjustDragging = false;
            _selAdjustGrip = SelectionAdjustGrip.None;
            e.Pointer.Capture(null);
            InvalidateVisual();
            if (_committedSelection is { IsEmpty: false } fin)
                CommittedSelectionEdited?.Invoke(this, new ImageSelectionCompletedEventArgs(fin));
            return;
        }
        if (_selDragging)
        {
            _selDragging = false;
            var dx = pos.X - _selStartScreen.X;
            var dy = pos.Y - _selStartScreen.Y;
            e.Pointer.Capture(null);
            InvalidateVisual();
            var bmp = Bitmap;
            if (bmp is { PixelSize.Width: > 0, PixelSize.Height: > 0 } && dx * dx + dy * dy > 4)
            {
                var box = BuildSelectionBoxInWorld(_selStartScreen, pos, bmp.PixelSize.Width, bmp.PixelSize.Height);
                if (!box.IsEmpty)
                    ImageSelectionCompleted?.Invoke(this, new ImageSelectionCompletedEventArgs(box));
            }
            return;
        }
        if (IsPipetteMode && _pipetteArmed)
        {
            if (!_pipetteDragCancel && TryScreenToImagePixel(pos, out var ix, out var iy))
                ImagePixelPicked?.Invoke(this, new ImagePixelPickedEventArgs(ix, iy));
            _pipetteArmed = false;
            e.Pointer.Capture(null);
            return;
        }
        _panning = false;
        e.Pointer.Capture(null);
    }

    protected override void OnPointerMoved(PointerEventArgs e)
    {
        base.OnPointerMoved(e);
        var pos = e.GetPosition(this);

        if (_midPanning)
        {
            var d = pos - _lastPointer;
            _lastPointer = pos;
            PanX += d.X;
            PanY += d.Y;
            return;
        }
        if (_eraserDragging)
        {
            _eraserCurScreen = pos;
            FireEraserStroke(pos);
            InvalidateVisual();
            return;
        }
        if (_frameDragging && _selectedFrameIndex >= 0)
        {
            // delta in pixel-immagine
            var z = Math.Max(Zoom, 0.0001);
            var dxImage = (pos.X - _frameDragStartScreen.X) / z;
            var dyImage = (pos.Y - _frameDragStartScreen.Y) / z;
            FrameDragged?.Invoke(this, new FrameDragEventArgs(
                _selectedFrameIndex,
                (int)Math.Round(dxImage),
                (int)Math.Round(dyImage),
                isCommit: false));
            return;
        }
        if (_floatingDragging)
        {
            var (wx, wy) = ScreenToImageFloat(pos, PanX, PanY, Zoom);
            var nx = (int)Math.Floor(wx - _floatingGrabWx);
            var ny = (int)Math.Floor(wy - _floatingGrabWy);
            FloatingOverlayMoved?.Invoke(this, new FloatingOverlayMoveEventArgs(nx, ny));
            InvalidateVisual();
            return;
        }
        if (_selAdjustDragging)
        {
            ApplyCommittedSelectionAdjustment(pos);
            return;
        }
        if (_selDragging)
        {
            _selCurScreen = pos;
            InvalidateVisual();
            return;
        }
        if (IsPipetteMode && _pipetteArmed && !_pipetteDragCancel)
        {
            var dx = pos.X - _pipettePress.X;
            var dy = pos.Y - _pipettePress.Y;
            if (dx * dx + dy * dy > 16) _pipetteDragCancel = true;
            return;
        }
        if (IsEraserMode)
        {
            _eraserCurScreen = pos;
            InvalidateVisual();
            return;
        }
        if (_panning)
        {
            var d = pos - _lastPointer;
            _lastPointer = pos;
            PanX += d.X;
            PanY += d.Y;
        }
    }

    // ─── Helpers ─────────────────────────────────────────────────────────────

    private void FireEraserStroke(Avalonia.Point screenPos)
    {
        var bmp = Bitmap;
        if (bmp is null || bmp.PixelSize.Width == 0) return;
        var (wx, wy) = ScreenToImageFloat(screenPos, PanX, PanY, Zoom);
        var ix = (int)Math.Floor(wx);
        var iy = (int)Math.Floor(wy);
        if (ix >= 0 && iy >= 0 && ix < bmp.PixelSize.Width && iy < bmp.PixelSize.Height)
        {
            if (ix == _lastEraserX && iy == _lastEraserY)
                return;
            _lastEraserX = ix;
            _lastEraserY = iy;
            EraserStroke?.Invoke(this, new EraserStrokeEventArgs(ix, iy, EraserRadius));
        }
    }

    private AxisAlignedBox BuildSelectionBoxInWorld(Avalonia.Point screenA, Avalonia.Point screenB, int imgW, int imgH)
    {
        var (wx0, wy0) = ScreenToImageFloat(screenA, PanX, PanY, Zoom);
        var (wx1, wy1) = ScreenToImageFloat(screenB, PanX, PanY, Zoom);
        if (SnapToGrid && SnapGridSize > 0)
        {
            // Soglia di aggancio in world-pixel
            var worldThresh = SnapThreshold / Math.Max(Zoom, 0.0001);
            var xLines = BuildXSnapLines();
            var yLines = BuildYSnapLines();
            wx0 = SnapCoordToLines(wx0, xLines, SnapGridSize, worldThresh);
            wy0 = SnapCoordToLines(wy0, yLines, SnapGridSize, worldThresh);
            wx1 = SnapCoordToLines(wx1, xLines, SnapGridSize, worldThresh);
            wy1 = SnapCoordToLines(wy1, yLines, SnapGridSize, worldThresh);
        }
        return AxisAlignedBox.FromWorldCornersHalfOpen(wx0, wy0, wx1, wy1, imgW, imgH);
    }

    /// <summary>Costruisce la lista di coordinate X di snap (bordi delle celle, se presenti).</summary>
    private List<int> BuildXSnapLines()
    {
        if (_spriteCells.Count == 0) return [];
        var set = new HashSet<int>();
        foreach (var c in _spriteCells) { set.Add(c.BoundsInAtlas.MinX); set.Add(c.BoundsInAtlas.MaxX); }
        return [.. set.OrderBy(x => x)];
    }

    /// <summary>Costruisce la lista di coordinate Y di snap (bordi delle celle, se presenti).</summary>
    private List<int> BuildYSnapLines()
    {
        if (_spriteCells.Count == 0) return [];
        var set = new HashSet<int>();
        foreach (var c in _spriteCells) { set.Add(c.BoundsInAtlas.MinY); set.Add(c.BoundsInAtlas.MaxY); }
        return [.. set.OrderBy(y => y)];
    }

    /// <summary>
    /// Aggancia <paramref name="v"/> alla linea più vicina in <paramref name="lines"/>
    /// (se entro <paramref name="threshold"/> world-px), oppure alla griglia <paramref name="fallbackGrid"/>.
    /// </summary>
    private static double SnapCoordToLines(double v, IReadOnlyList<int> lines, int fallbackGrid, double threshold)
    {
        if (lines.Count > 0)
        {
            var best     = lines[0];
            var bestDist = Math.Abs(v - best);
            for (var i = 1; i < lines.Count; i++)
            {
                var d = Math.Abs(v - lines[i]);
                if (d < bestDist) { best = lines[i]; bestDist = d; }
            }
            if (bestDist <= threshold) return best;
        }
        // Fallback: snap a griglia regolare (sempre esatto — nessuna soglia)
        return SnapCoord(v, fallbackGrid);
    }

    private static double SnapCoord(double v, int gridSize) =>
        Math.Round(v / gridSize) * gridSize;

    private static IEnumerable<(double x, double y)> CommittedSelectionGripPoints(AxisAlignedBox cs)
    {
        var mx = (cs.MinX + cs.MaxX) * 0.5;
        var my = (cs.MinY + cs.MaxY) * 0.5;
        yield return (cs.MinX, cs.MinY);
        yield return (cs.MaxX, cs.MinY);
        yield return (cs.MinX, cs.MaxY);
        yield return (cs.MaxX, cs.MaxY);
        yield return (mx, cs.MinY);
        yield return (mx, cs.MaxY);
        yield return (cs.MinX, my);
        yield return (cs.MaxX, my);
    }

    private void ApplyCommittedSelectionAdjustment(Avalonia.Point screenPos)
    {
        var bmp = Bitmap;
        if (bmp is null || bmp.PixelSize.Width < 1 || bmp.PixelSize.Height < 1) return;

        var imgW = bmp.PixelSize.Width;
        var imgH = bmp.PixelSize.Height;
        var (wx, wy) = ScreenToImageFloat(screenPos, PanX, PanY, Zoom);
        var (sx, sy) = SnapWorldPoint(wx, wy);
        var B0 = _selAdjustBoxStart;

        var nb = _selAdjustGrip switch
        {
            SelectionAdjustGrip.Move => AdjustCommittedMove(in B0, sx, sy, imgW, imgH),
            SelectionAdjustGrip.N => AxisAlignedBox.FromWorldCornersHalfOpen(B0.MinX, sy, B0.MaxX, B0.MaxY, imgW, imgH),
            SelectionAdjustGrip.Ne => AxisAlignedBox.FromWorldCornersHalfOpen(B0.MinX, sy, sx, B0.MaxY, imgW, imgH),
            SelectionAdjustGrip.E => AxisAlignedBox.FromWorldCornersHalfOpen(B0.MinX, B0.MinY, sx, B0.MaxY, imgW, imgH),
            SelectionAdjustGrip.Se => AxisAlignedBox.FromWorldCornersHalfOpen(B0.MinX, B0.MinY, sx, sy, imgW, imgH),
            SelectionAdjustGrip.S => AxisAlignedBox.FromWorldCornersHalfOpen(B0.MinX, B0.MinY, B0.MaxX, sy, imgW, imgH),
            SelectionAdjustGrip.Sw => AxisAlignedBox.FromWorldCornersHalfOpen(sx, B0.MinY, B0.MaxX, sy, imgW, imgH),
            SelectionAdjustGrip.W => AxisAlignedBox.FromWorldCornersHalfOpen(sx, B0.MinY, B0.MaxX, B0.MaxY, imgW, imgH),
            SelectionAdjustGrip.Nw => AxisAlignedBox.FromWorldCornersHalfOpen(sx, sy, B0.MaxX, B0.MaxY, imgW, imgH),
            _ => B0
        };

        if (nb.IsEmpty) return;

        _committedSelection = nb;
        CommittedSelectionEdited?.Invoke(this, new ImageSelectionCompletedEventArgs(nb));
        InvalidateVisual();
    }

    private AxisAlignedBox AdjustCommittedMove(in AxisAlignedBox B0, double sx, double sy, int imgW, int imgH)
    {
        var dx = (int)Math.Round(sx - _selAdjustPointerWorldStart.X);
        var dy = (int)Math.Round(sy - _selAdjustPointerWorldStart.Y);
        var nmx = B0.MinX + dx;
        var nmy = B0.MinY + dy;
        var w = B0.Width;
        var h = B0.Height;
        if (w < 1 || h < 1) return B0;
        if (nmx < 0) nmx = 0;
        if (nmy < 0) nmy = 0;
        if (nmx + w > imgW) nmx = imgW - w;
        if (nmy + h > imgH) nmy = imgH - h;
        return new AxisAlignedBox(nmx, nmy, nmx + w, nmy + h);
    }

    private double ScreenDistSq(Avalonia.Point screen, double worldX, double worldY)
    {
        var sx = worldX * Zoom + PanX;
        var sy = worldY * Zoom + PanY;
        var dx = screen.X - sx;
        var dy = screen.Y - sy;
        return dx * dx + dy * dy;
    }

    private Avalonia.Point WorldToScreen(double worldX, double worldY) =>
        new(worldX * Zoom + PanX, worldY * Zoom + PanY);

    private static double PointSegmentDistSq(Avalonia.Point p, Avalonia.Point a, Avalonia.Point b)
    {
        var vx = b.X - a.X;
        var vy = b.Y - a.Y;
        var wx = p.X - a.X;
        var wy = p.Y - a.Y;
        var c1 = vx * wx + vy * wy;
        if (c1 <= 0) return wx * wx + wy * wy;
        var c2 = vx * vx + vy * vy;
        if (c2 <= c1)
        {
            var dx = p.X - b.X;
            var dy = p.Y - b.Y;
            return dx * dx + dy * dy;
        }
        var t = c1 / c2;
        var projx = a.X + t * vx;
        var projy = a.Y + t * vy;
        var dx2 = p.X - projx;
        var dy2 = p.Y - projy;
        return dx2 * dx2 + dy2 * dy2;
    }

    private SelectionAdjustGrip HitTestCommittedSelectionGrip(Avalonia.Point screen, in AxisAlignedBox cs)
    {
        const double cornerHitSq = 10.0 * 10.0;
        const double edgeHitSq = 9.0 * 9.0;

        if (ScreenDistSq(screen, cs.MinX, cs.MinY) <= cornerHitSq) return SelectionAdjustGrip.Nw;
        if (ScreenDistSq(screen, cs.MaxX, cs.MinY) <= cornerHitSq) return SelectionAdjustGrip.Ne;
        if (ScreenDistSq(screen, cs.MaxX, cs.MaxY) <= cornerHitSq) return SelectionAdjustGrip.Se;
        if (ScreenDistSq(screen, cs.MinX, cs.MaxY) <= cornerHitSq) return SelectionAdjustGrip.Sw;

        var n0 = WorldToScreen(cs.MinX, cs.MinY);
        var n1 = WorldToScreen(cs.MaxX, cs.MinY);
        var s0 = WorldToScreen(cs.MinX, cs.MaxY);
        var s1 = WorldToScreen(cs.MaxX, cs.MaxY);
        var w0 = WorldToScreen(cs.MinX, cs.MinY);
        var w1 = WorldToScreen(cs.MinX, cs.MaxY);
        var e0 = WorldToScreen(cs.MaxX, cs.MinY);
        var e1 = WorldToScreen(cs.MaxX, cs.MaxY);

        if (PointSegmentDistSq(screen, n0, n1) <= edgeHitSq) return SelectionAdjustGrip.N;
        if (PointSegmentDistSq(screen, s0, s1) <= edgeHitSq) return SelectionAdjustGrip.S;
        if (PointSegmentDistSq(screen, w0, w1) <= edgeHitSq) return SelectionAdjustGrip.W;
        if (PointSegmentDistSq(screen, e0, e1) <= edgeHitSq) return SelectionAdjustGrip.E;

        var (wx, wy) = ScreenToImageFloat(screen, PanX, PanY, Zoom);
        if (wx >= cs.MinX && wx < cs.MaxX && wy >= cs.MinY && wy < cs.MaxY)
            return SelectionAdjustGrip.Move;

        return SelectionAdjustGrip.None;
    }

    private (double sx, double sy) SnapWorldPoint(double wx, double wy)
    {
        if (!SnapToGrid || SnapGridSize <= 0)
            return (wx, wy);

        var worldThresh = SnapThreshold / Math.Max(Zoom, 0.0001);
        var xLines = BuildXSnapLines();
        var yLines = BuildYSnapLines();
        var sx = SnapCoordToLines(wx, xLines, SnapGridSize, worldThresh);
        var sy = SnapCoordToLines(wy, yLines, SnapGridSize, worldThresh);
        return (sx, sy);
    }

    // ─── Draw helpers ─────────────────────────────────────────────────────────

    private static void DrawCellOverlay(DrawingContext ctx, List<SpriteCell> cells, double zoom)
    {
        var strokeT = 1.5 / Math.Max(zoom, 0.0001);
        for (var i = 0; i < cells.Count; i++)
        {
            var c     = cells[i];
            var color = CellPalette[i % CellPalette.Length];
            ctx.FillRectangle(
                new SolidColorBrush(Avalonia.Media.Color.FromArgb(30, color.R, color.G, color.B)),
                new Rect(c.BoundsInAtlas.MinX, c.BoundsInAtlas.MinY, c.BoundsInAtlas.Width, c.BoundsInAtlas.Height));
            ctx.DrawRectangle(
                new Pen(new SolidColorBrush(color), strokeT),
                new Rect(c.BoundsInAtlas.MinX, c.BoundsInAtlas.MinY, c.BoundsInAtlas.Width, c.BoundsInAtlas.Height));
        }
    }

    private static void DrawSliceGrid(DrawingContext ctx, int worldW, int worldH, int rows, int cols, double zoom)
    {
        var (cellW, cellH) = GridSlicer.ComputeCellSize(worldW, worldH, rows, cols);
        if (cellW < 1 || cellH < 1)
            return;

        var t = 1.5 / Math.Max(zoom, 0.0001);
        var pen = new Pen(new SolidColorBrush(Avalonia.Media.Color.FromArgb(220, 255, 180, 0)), t);

        // Stessi bordi delle <see cref="SpriteCell"/> da GridSlicer (non worldW/cols in floating).
        static void AppendBoundary(HashSet<double> set, double v, double max)
        {
            if (v >= 0 && v <= max)
                set.Add(v);
        }

        var xs = new HashSet<double> { 0, worldW };
        for (var k = 1; k <= cols; k++)
            AppendBoundary(xs, Math.Min(k * cellW, worldW), worldW);

        var ys = new HashSet<double> { 0, worldH };
        for (var k = 1; k <= rows; k++)
            AppendBoundary(ys, Math.Min(k * cellH, worldH), worldH);

        foreach (var x in xs.OrderBy(v => v))
            ctx.DrawLine(pen, new Avalonia.Point(x, 0), new Avalonia.Point(x, worldH));
        foreach (var y in ys.OrderBy(v => v))
            ctx.DrawLine(pen, new Avalonia.Point(0, y), new Avalonia.Point(worldW, y));
    }

    private static void DrawFrameWorkbench(DrawingContext ctx, List<AxisAlignedBox> cells, int selectedIdx, double zoom)
    {
        var t = 1.5 / Math.Max(zoom, 0.0001);
        var penUnselected = new Pen(new SolidColorBrush(Avalonia.Media.Color.FromArgb(180, 90, 200, 255)), t);
        var penSelected   = new Pen(new SolidColorBrush(Avalonia.Media.Color.FromArgb(255, 255, 200, 0)), t * 1.6);

        for (var i = 0; i < cells.Count; i++)
        {
            var c = cells[i];
            var rect = new Rect(c.MinX, c.MinY, c.Width, c.Height);
            ctx.DrawRectangle(i == selectedIdx ? penSelected : penUnselected, rect);

            if (i == selectedIdx)
            {
                // crosshair: linee di centro X e Y + bordi (baseline = bottom)
                var cx   = c.MinX + c.Width  / 2.0;
                var cy   = c.MinY + c.Height / 2.0;
                var penGuide  = new Pen(new SolidColorBrush(Avalonia.Media.Color.FromArgb(140, 255, 200, 0)), t * 0.8)
                { DashStyle = new DashStyle([3.0, 2.0], 0) };
                var penBaseline = new Pen(new SolidColorBrush(Avalonia.Media.Color.FromArgb(180, 0, 220, 140)), t * 0.8)
                { DashStyle = new DashStyle([2.0, 2.0], 0) };

                // centro V/H
                ctx.DrawLine(penGuide, new Avalonia.Point(cx, c.MinY), new Avalonia.Point(cx, c.MaxY));
                ctx.DrawLine(penGuide, new Avalonia.Point(c.MinX, cy), new Avalonia.Point(c.MaxX, cy));
                // baseline (bottom)
                ctx.DrawLine(penBaseline, new Avalonia.Point(c.MinX, c.MaxY - 0.5), new Avalonia.Point(c.MaxX, c.MaxY - 0.5));
            }
        }
    }

    private static void DrawSnapGrid(DrawingContext ctx, int worldW, int worldH, int snapSize, double zoom)
    {
        var screenCellSize = snapSize * zoom;
        if (screenCellSize < 4) return;
        var alpha = (byte)Math.Clamp((int)((screenCellSize - 4) / 8.0 * 80), 20, 100);
        var pen = new Pen(new SolidColorBrush(Avalonia.Media.Color.FromArgb(alpha, 255, 165, 0)),
                          1.0 / Math.Max(zoom, 0.0001));
        for (var x = 0; x <= worldW; x += snapSize)
            ctx.DrawLine(pen, new Avalonia.Point(x, 0), new Avalonia.Point(x, worldH));
        for (var y = 0; y <= worldH; y += snapSize)
            ctx.DrawLine(pen, new Avalonia.Point(0, y), new Avalonia.Point(worldW, y));
    }

    private static void DrawCheckerboard(DrawingContext ctx, double w, double h)
    {
        const int cell = 12;
        for (var y = 0; y < h + cell; y += cell)
        for (var x = 0; x < w + cell; x += cell)
        {
            var alt = ((x / cell) + (y / cell)) % 2 == 0;
            ctx.FillRectangle(alt ? CheckerBrushA : CheckerBrushB, new Rect(x, y, cell, cell));
        }
    }

    private static void DrawLightGrid(DrawingContext ctx, int worldW, int worldH, int step, double zoom)
    {
        if (step < 1) return;
        var screenStep = step * zoom;
        if (screenStep < 2) return; // troppo piccola per essere visibile, salta
        var t   = 1.0 / Math.Max(zoom, 0.0001);
        var pen = new Pen(new SolidColorBrush(Avalonia.Media.Color.FromArgb(60, 255, 255, 255)), t);
        for (var x = 0; x <= worldW; x += step)
            ctx.DrawLine(pen, new Avalonia.Point(x, 0), new Avalonia.Point(x, worldH));
        for (var y = 0; y <= worldH; y += step)
            ctx.DrawLine(pen, new Avalonia.Point(0, y), new Avalonia.Point(worldW, y));
    }

    /// <summary>
    /// Griglia tile + gutter (spacing tra tile). Linee a inizio tile e, se spacing &gt; 0, a fine tile.
    /// Coordinate mondo (pixel immagine).
    /// </summary>
    private static void DrawAlignGrid(DrawingContext ctx, int worldW, int worldH,
        int offsetX, int offsetY, int cellW, int cellH, int spacingX, int spacingY, double zoom)
    {
        if (cellW < 1 || cellH < 1 || worldW < 1 || worldH < 1)
            return;

        var periodX = cellW + spacingX;
        var periodY = cellH + spacingY;
        if (periodX < 1 || periodY < 1)
            return;

        var t = 1.25 / Math.Max(zoom, 0.0001);
        var penMajor = new Pen(new SolidColorBrush(Avalonia.Media.Color.FromArgb(210, 140, 200, 255)), t);
        var penGutter = spacingX > 0 || spacingY > 0
            ? new Pen(new SolidColorBrush(Avalonia.Media.Color.FromArgb(160, 255, 140, 200)), t * 0.85)
            { DashStyle = new DashStyle([4.0, 3.0], 0) }
            : penMajor;

        static double SnapLine(double v) => Math.Floor(v) + 0.5;

        void Vertical(double x)
        {
            if (x < 0 || x > worldW) return;
            var sx = SnapLine(x);
            ctx.DrawLine(penMajor, new Avalonia.Point(sx, 0), new Avalonia.Point(sx, worldH));
        }

        void VerticalGutter(double x)
        {
            if (x < 0 || x > worldW) return;
            var sx = SnapLine(x);
            ctx.DrawLine(penGutter, new Avalonia.Point(sx, 0), new Avalonia.Point(sx, worldH));
        }

        void Horizontal(double y)
        {
            if (y < 0 || y > worldH) return;
            var sy = SnapLine(y);
            ctx.DrawLine(penMajor, new Avalonia.Point(0, sy), new Avalonia.Point(worldW, sy));
        }

        void HorizontalGutter(double y)
        {
            if (y < 0 || y > worldH) return;
            var sy = SnapLine(y);
            ctx.DrawLine(penGutter, new Avalonia.Point(0, sy), new Avalonia.Point(worldW, sy));
        }

        for (var vx = (double)offsetX; vx < worldW + 1e-6; vx += periodX)
        {
            Vertical(vx);
            if (spacingX > 0)
            {
                var endX = vx + cellW;
                VerticalGutter(endX);
            }
        }

        for (var vy = (double)offsetY; vy < worldH + 1e-6; vy += periodY)
        {
            Horizontal(vy);
            if (spacingY > 0)
            {
                var endY = vy + cellH;
                HorizontalGutter(endY);
            }
        }
    }
}

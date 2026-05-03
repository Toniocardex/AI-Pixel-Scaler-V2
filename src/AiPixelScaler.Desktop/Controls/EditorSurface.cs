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

    public static readonly StyledProperty<int> EraserSizeProperty =
        AvaloniaProperty.Register<EditorSurface, int>(nameof(EraserSize), 1);

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

    /// <summary>
    /// Se <c>true</c> (default) disegna un bordo sottile attorno al documento,
    /// rendendo visibili i limiti del foglio di lavoro anche su canvas trasparenti.
    /// </summary>
    public static readonly StyledProperty<bool> ShowDocumentBorderProperty =
        AvaloniaProperty.Register<EditorSurface, bool>(nameof(ShowDocumentBorder), true);

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

    private static readonly SolidColorBrush CheckerBrushA       = new(Avalonia.Media.Color.FromArgb(0xff, 0x2a, 0x2a, 0x2e));
    private static readonly SolidColorBrush CheckerBrushB       = new(Avalonia.Media.Color.FromArgb(0xff, 0x1e, 0x1e, 0x22));
    private static readonly SolidColorBrush InfoBgBrush         = new(Avalonia.Media.Color.FromArgb(190, 8, 8, 18));
    private static readonly SolidColorBrush InfoFgBrush         = new(Avalonia.Media.Color.FromArgb(255, 255, 255, 255));
    private static readonly SolidColorBrush EraserBrush         = new(Avalonia.Media.Color.FromArgb(50, 255, 80, 80));
    private static readonly SolidColorBrush LightGridBrush      = new(Avalonia.Media.Color.FromArgb(60,  255, 255, 255));
    private static readonly SolidColorBrush SliceGridBrush      = new(Avalonia.Media.Color.FromArgb(220, 255, 180, 0));
    private static readonly SolidColorBrush AlignMajorBrush     = new(Avalonia.Media.Color.FromArgb(210, 140, 200, 255));
    private static readonly SolidColorBrush AlignGutterBrush    = new(Avalonia.Media.Color.FromArgb(160, 255, 140, 200));
    private static readonly SolidColorBrush FrameUnselBrush     = new(Avalonia.Media.Color.FromArgb(180, 90,  200, 255));
    private static readonly SolidColorBrush FrameSelBrush       = new(Avalonia.Media.Color.FromArgb(255, 255, 200, 0));
    private static readonly SolidColorBrush FrameGuideBrush     = new(Avalonia.Media.Color.FromArgb(140, 255, 200, 0));
    private static readonly SolidColorBrush FrameBaselineBrush  = new(Avalonia.Media.Color.FromArgb(180, 0,   220, 140));
    private static readonly SolidColorBrush SelBrush            = new(Avalonia.Media.Color.FromArgb(255, 80,  220, 255));
    private static readonly SolidColorBrush SelCommBrush        = new(Avalonia.Media.Color.FromArgb(210, 80,  220, 255));
    private static readonly SolidColorBrush SelFillBrush        = new(Avalonia.Media.Color.FromArgb(22,  80,  200, 255));
    private static readonly SolidColorBrush SnapDotBrush        = new(Avalonia.Media.Color.FromArgb(220, 255, 165, 0));
    private static readonly SolidColorBrush SnapGuideBrush      = new(Avalonia.Media.Color.FromArgb(110, 255, 165, 0));
    private static readonly SolidColorBrush TilePreviewBrush    = new(Avalonia.Media.Color.FromArgb(180, 255, 200, 0));
    private static readonly SolidColorBrush DocBorderBrush      = new(Avalonia.Media.Color.FromArgb(140, 175, 180, 205));
    private static readonly SolidColorBrush FloatingBrush       = new(Avalonia.Media.Color.FromArgb(255, 255, 220, 80));
    private static readonly SolidColorBrush EraserOutlineBrush  = new(Avalonia.Media.Color.FromArgb(220, 255, 80,  80));

    private static readonly Avalonia.Media.Color[] CellPalette =
    [
        Avalonia.Media.Color.FromArgb(180, 255, 80,  80),
        Avalonia.Media.Color.FromArgb(180, 80,  200, 255),
        Avalonia.Media.Color.FromArgb(180, 80,  255, 130),
        Avalonia.Media.Color.FromArgb(180, 255, 200, 60),
        Avalonia.Media.Color.FromArgb(180, 200, 80,  255),
        Avalonia.Media.Color.FromArgb(180, 255, 130, 40),
    ];

    // Pre-built fill/stroke brushes per palette slot — avoids per-cell allocation in DrawCellOverlay
    private static readonly SolidColorBrush[] CellFillBrushes =
        CellPalette.Select(c => new SolidColorBrush(Avalonia.Media.Color.FromArgb(30, c.R, c.G, c.B))).ToArray();
    private static readonly SolidColorBrush[] CellStrokeBrushes =
        CellPalette.Select(c => new SolidColorBrush(c)).ToArray();

    private List<SpriteCell> _spriteCells = [];

    // ─── Zoom-cached pens (rebuilt only when zoom changes) ────────────────────
    private double _cachedPenZoom = double.NaN;
    private Pen _lightGridPen     = null!;
    private Pen _sliceGridPen     = null!;
    private Pen _alignMajorPen    = null!;
    private Pen _alignGutterPen   = null!;
    private Pen _frameUnselPen    = null!;
    private Pen _frameSelPen      = null!;
    private Pen _frameGuidePen    = null!;
    private Pen _frameBasePen     = null!;
    private Pen _selDragPen       = null!;
    private Pen _selCommPen       = null!;
    private Pen _eraserOutPen     = null!;
    private Pen _tilePreviewPen   = null!;
    private Pen _docBorderPen     = null!;
    private Pen _floatingPen      = null!;
    private Pen _selSnapGuidePen  = null!;

    // ─── Public API ───────────────────────────────────────────────────────────

    public EditorSurface()
    {
        ClipToBounds = true;
        Focusable = true;
    }

    protected override void OnDetachedFromVisualTree(VisualTreeAttachmentEventArgs e)
    {
        base.OnDetachedFromVisualTree(e);
        foreach (var f in _workbenchRenderFrames) f.Dispose();
        _workbenchRenderFrames = [];
        _frameCells = [];
    }

    public List<SpriteCell> SpriteCells
    {
        get => _spriteCells;
        set { _spriteCells = value ?? []; InvalidateSnapLineCache(); InvalidateVisual(); }
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
            if (IsEraserMode == value) return;
            SetValue(IsEraserModeProperty, value);
            Cursor = value ? new Cursor(StandardCursorType.Cross) : null;
        }
    }

    /// <summary>
    /// Lato del quadrato gomma in pixel-immagine (1 = singolo pixel).
    /// La gomma è sempre quadrata: un valore di N cancella N×N pixel per ogni colpo.
    /// </summary>
    public int EraserSize
    {
        get => GetValue(EraserSizeProperty);
        set => SetValue(EraserSizeProperty, Math.Clamp(value, 1, 64));
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
            if (IsFrameEditMode == value) return;
            if (!value) { _selectedFrameIndex = -1; _frameDragging = false; }
            SetValue(IsFrameEditModeProperty, value);
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
    /// Bordo di riferimento attorno al documento (default: <c>true</c>).
    /// Visibile sempre, indispensabile su canvas trasparenti.
    /// </summary>
    public bool ShowDocumentBorder
    {
        get => GetValue(ShowDocumentBorderProperty);
        set => SetValue(ShowDocumentBorderProperty, value);
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

    /// <summary>Adatta l'immagine alla viewport lasciando un margine visivo in pixel schermo.</summary>
    public bool FitImageInViewport(double screenPadding = 48)
    {
        var bmp = Bitmap;
        if (bmp is null || bmp.PixelSize.Width <= 0 || bmp.PixelSize.Height <= 0) return false;
        if (Bounds.Width <= 0 || Bounds.Height <= 0) return false;

        var maxPadding = Math.Min(Bounds.Width, Bounds.Height) * 0.45;
        var padding = Math.Clamp(screenPadding, 0, maxPadding);
        var availableW = Math.Max(1, Bounds.Width - padding * 2);
        var availableH = Math.Max(1, Bounds.Height - padding * 2);
        var zoom = Math.Clamp(
            Math.Min(availableW / bmp.PixelSize.Width, availableH / bmp.PixelSize.Height),
            0.05,
            64.0);

        Zoom = zoom;
        PanX = (Bounds.Width - bmp.PixelSize.Width * zoom) * 0.5;
        PanY = (Bounds.Height - bmp.PixelSize.Height * zoom) * 0.5;
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

    public void UpdateBitmapRegion(Image<Rgba32> image, int xMin, int yMin, int xMax, int yMax)
    {
        if (Bitmap is WriteableBitmap wb)
        {
            Rgba32BitmapBridge.UpdateRect(wb, image, xMin, yMin, xMax, yMax);
            InvalidateVisual();
        }
        else
        {
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

    // ─── Snap-line cache ──────────────────────────────────────────────────────

    private List<SpriteCell>? _snapLineCacheSource;
    private List<int> _cachedXSnapLines = [];
    private List<int> _cachedYSnapLines = [];

    private void InvalidateSnapLineCache() { _snapLineCacheSource = null; }

    private void EnsureSnapLineCache()
    {
        if (ReferenceEquals(_snapLineCacheSource, _spriteCells)) return;
        _snapLineCacheSource = _spriteCells;
        if (_spriteCells.Count == 0) { _cachedXSnapLines = []; _cachedYSnapLines = []; return; }
        var xs = new HashSet<int>();
        var ys = new HashSet<int>();
        foreach (var c in _spriteCells)
        {
            xs.Add(c.BoundsInAtlas.MinX); xs.Add(c.BoundsInAtlas.MaxX);
            ys.Add(c.BoundsInAtlas.MinY); ys.Add(c.BoundsInAtlas.MaxY);
        }
        _cachedXSnapLines = [.. xs.OrderBy(v => v)];
        _cachedYSnapLines = [.. ys.OrderBy(v => v)];
    }

    // ─── Zoom-dependent pen cache ─────────────────────────────────────────────

    private void RefreshPens(double zoom)
    {
        if (Math.Abs(zoom - _cachedPenZoom) < 1e-9) return;
        _cachedPenZoom = zoom;
        var t  = 1.0  / Math.Max(zoom, 0.0001);
        var t5 = 1.5  * t;
        _lightGridPen    = new Pen(LightGridBrush,     t);
        _sliceGridPen    = new Pen(SliceGridBrush,     t5);
        _alignMajorPen   = new Pen(AlignMajorBrush,   1.25 * t);
        _alignGutterPen  = new Pen(AlignGutterBrush,  1.25 * t * 0.85)
            { DashStyle = new DashStyle([4.0, 3.0], 0) };
        _frameUnselPen   = new Pen(FrameUnselBrush,   t5);
        _frameSelPen     = new Pen(FrameSelBrush,     t5 * 1.6);
        _frameGuidePen   = new Pen(FrameGuideBrush,   t5 * 0.8)
            { DashStyle = new DashStyle([3.0, 2.0], 0) };
        _frameBasePen    = new Pen(FrameBaselineBrush, t5 * 0.8)
            { DashStyle = new DashStyle([2.0, 2.0], 0) };
        _selDragPen      = new Pen(SelBrush,           t5)
            { LineCap = PenLineCap.Square, DashStyle = new DashStyle([4.0, 3.0], 0) };
        _selCommPen      = new Pen(SelCommBrush,       t5)
            { LineCap = PenLineCap.Square, DashStyle = new DashStyle([4.0, 3.0], 0) };
        _eraserOutPen    = new Pen(EraserOutlineBrush, t5)
            { LineCap = PenLineCap.Square, LineJoin = PenLineJoin.Miter };
        _tilePreviewPen  = new Pen(TilePreviewBrush,  t5);
        _docBorderPen    = new Pen(DocBorderBrush,    t);
        _floatingPen     = new Pen(FloatingBrush,     1.2 * t)
            { DashStyle = new DashStyle([3.0, 3.0], 0) };
        _selSnapGuidePen = new Pen(SnapGuideBrush,    0.8 * t);
    }

    // ─── Property invalidation ────────────────────────────────────────────────

    private static readonly HashSet<AvaloniaProperty> s_visualProps =
    [
        ZoomProperty, PanXProperty, PanYProperty, BitmapProperty,
        ShowGridProperty, WorldGridSizeProperty,
        SliceGridRowsProperty, SliceGridColsProperty,
        SnapToGridProperty, SnapGridSizeProperty,
        ShowAlignGridProperty,
        AlignGridOffsetXProperty, AlignGridOffsetYProperty,
        AlignGridCellWidthProperty, AlignGridCellHeightProperty,
        AlignGridSpacingXProperty, AlignGridSpacingYProperty,
        IsEraserModeProperty, EraserSizeProperty,
        IsFrameEditModeProperty, FrameSnapRadiusProperty, FrameSnapEnabledProperty,
        IsTilePreviewModeProperty, IsCellClickModeProperty,
    ];

    protected override void OnPropertyChanged(AvaloniaPropertyChangedEventArgs change)
    {
        base.OnPropertyChanged(change);
        if (s_visualProps.Contains(change.Property)) InvalidateVisual();
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

        RefreshPens(_viewport.Zoom);
        EnsureSnapLineCache();

        DrawCheckerboard(context, w, h);

        var bmp = Bitmap;
        var inWorkbench = IsFrameEditMode && _workbenchRenderFrames.Count > 0;

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

        var m = Matrix.CreateScale(_viewport.Zoom, _viewport.Zoom)
              * Matrix.CreateTranslation(_viewport.PanX, _viewport.PanY);

        using (context.PushTransform(m))
        {
            if (inWorkbench)
            {
                // RENDER DIRETTO dei frame con i loro offset correnti — 60 FPS fluidi sul drag.
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
                    context.DrawImage(bmp,
                        new Rect(0, 0, worldW, worldH),
                        new Rect(tx * worldW, ty * worldH, worldW, worldH));
                context.DrawRectangle(_tilePreviewPen, new Rect(0, 0, worldW, worldH));
            }
            else if (bmp is not null)
            {
                context.DrawImage(bmp, new Rect(0, 0, worldW, worldH), new Rect(0, 0, worldW, worldH));
            }

            if (!inWorkbench && _floatingOverlayBitmap is not null && _floatingW > 0 && _floatingH > 0)
            {
                using (context.PushRenderOptions(new RenderOptions { BitmapInterpolationMode = BitmapInterpolationMode.None }))
                {
                    var srcW = _floatingOverlayBitmap.PixelSize.Width;
                    var srcH = _floatingOverlayBitmap.PixelSize.Height;
                    context.DrawImage(_floatingOverlayBitmap,
                        new Rect(0, 0, srcW, srcH),
                        new Rect(_floatingX, _floatingY, _floatingW, _floatingH));
                }
                context.DrawRectangle(_floatingPen, new Rect(_floatingX, _floatingY, _floatingW, _floatingH));
            }
        }

        // ── Bordo documento ───────────────────────────────────────────────────
        // Disegna sempre il contorno del foglio di lavoro, indispensabile su canvas
        // trasparenti dove i pixel non forniscono riferimenti visivi.
        if (ShowDocumentBorder && !inWorkbench && !IsTilePreviewMode)
        {
            using (context.PushTransform(m))
                context.DrawRectangle(_docBorderPen, new Rect(0, 0, worldW, worldH));
        }

        if (ShowGrid)
        {
            using (context.PushTransform(m))
                DrawLightGrid(context, worldW, worldH, WorldGridSize, _lightGridPen);
        }

        if (SnapToGrid && SnapGridSize > 0)
        {
            using (context.PushTransform(m))
                DrawSnapGrid(context, worldW, worldH, SnapGridSize, _viewport.Zoom);
        }

        if (SliceGridRows > 0 && SliceGridCols > 0)
        {
            using (context.PushTransform(m))
                DrawSliceGrid(context, worldW, worldH, SliceGridRows, SliceGridCols, _sliceGridPen);
        }

        if (ShowAlignGrid && AlignGridCellWidth >= 1 && AlignGridCellHeight >= 1)
        {
            using (context.PushTransform(m))
                DrawAlignGrid(context, worldW, worldH,
                    AlignGridOffsetX, AlignGridOffsetY,
                    AlignGridCellWidth, AlignGridCellHeight,
                    AlignGridSpacingX, AlignGridSpacingY,
                    _alignMajorPen, _alignGutterPen);
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
                using (context.PushTransform(m))
                {
                    context.DrawRectangle(_selDragPen, new Rect(box.MinX, box.MinY, box.Width, box.Height));

                    if (SnapToGrid && SnapGridSize > 0)
                    {
                        var (cwx, cwy) = ScreenToImageFloat(_selCurScreen, PanX, PanY, Zoom);
                        var worldThresh = SnapThreshold / Math.Max(_viewport.Zoom, 0.0001);
                        var snappedX = SnapCoordToLines(cwx, _cachedXSnapLines, SnapGridSize, worldThresh);
                        var snappedY = SnapCoordToLines(cwy, _cachedYSnapLines, SnapGridSize, worldThresh);
                        var dotR = 4.0 / Math.Max(_viewport.Zoom, 0.0001);
                        context.FillRectangle(SnapDotBrush,
                            new Rect(snappedX - dotR, snappedY - dotR, dotR * 2, dotR * 2));

                        if (_spriteCells.Count > 0)
                        {
                            context.DrawLine(_selSnapGuidePen, new Avalonia.Point(box.MinX, 0), new Avalonia.Point(box.MinX, worldH));
                            context.DrawLine(_selSnapGuidePen, new Avalonia.Point(box.MaxX, 0), new Avalonia.Point(box.MaxX, worldH));
                            context.DrawLine(_selSnapGuidePen, new Avalonia.Point(0, box.MinY), new Avalonia.Point(worldW, box.MinY));
                            context.DrawLine(_selSnapGuidePen, new Avalonia.Point(0, box.MaxY), new Avalonia.Point(worldW, box.MaxY));
                        }
                    }
                }
                DrawSelectionInfo(context, box.Width, box.Height, _selCurScreen, w, h);
            }
        }

        // ── Selezione committed (canvas-mode, persiste dopo il drag) ──────────
        if (IsSelectionMode && !_selDragging && _committedSelection is { IsEmpty: false } cs)
        {
            using (context.PushTransform(m))
            {
                context.FillRectangle(SelFillBrush, new Rect(cs.MinX, cs.MinY, cs.Width, cs.Height));
                context.DrawRectangle(_selCommPen, new Rect(cs.MinX, cs.MinY, cs.Width, cs.Height));

                var hr = 3.5 / Math.Max(_viewport.Zoom, 0.0001);
                foreach (var (hx, hy) in CommittedSelectionGripPoints(cs))
                    context.FillRectangle(SelBrush, new Rect(hx - hr, hy - hr, hr * 2, hr * 2));
            }
            var infoScrPt = new Avalonia.Point(
                cs.MaxX * _viewport.Zoom + _viewport.PanX,
                cs.MaxY * _viewport.Zoom + _viewport.PanY + 4);
            DrawSelectionInfo(context, cs.Width, cs.Height, infoScrPt, w, h);
        }

        // ── Workbench overlay (modalità frame edit) ───────────────────────────
        if (IsFrameEditMode && _frameCells.Count > 0)
        {
            using (context.PushTransform(m))
                DrawFrameWorkbench(context, _frameCells, _selectedFrameIndex,
                    _frameUnselPen, _frameSelPen, _frameGuidePen, _frameBasePen);
        }

        // ── Cursore gomma ──────────────────────────────────────────────────────
        if (IsEraserMode && (IsPointerOver || _eraserDragging))
        {
            if (TryGetEraserBox(_eraserCurScreen, worldW, worldH, out var xMin, out var yMin, out var xMax, out var yMax))
            {
                using (context.PushTransform(m))
                {
                    var rect = new Rect(xMin, yMin, xMax - xMin, yMax - yMin);
                    context.FillRectangle(EraserBrush, rect);
                    context.DrawRectangle(_eraserOutPen, rect);
                }
            }
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

    /// <summary>Detent zoom standard (potenze di 2): snap se entro <see cref="ZoomDetentTolerancePct"/>.</summary>
    private static readonly double[] ZoomDetents =
        { 0.0625, 0.125, 0.25, 0.5, 1.0, 2.0, 4.0, 8.0, 16.0, 32.0, 64.0 };

    private const double ZoomDetentTolerancePct = 0.04;
    private const double WheelZoomFactor = 1.41421356237; // √2: due step di rotella ⇒ ×2
    private const double WheelPanStepPx = 60.0;

    private static double SnapZoomToDetent(double z)
    {
        foreach (var d in ZoomDetents)
            if (Math.Abs(z - d) / d <= ZoomDetentTolerancePct) return d;
        return z;
    }

    protected override void OnPointerWheelChanged(PointerWheelEventArgs e)
    {
        base.OnPointerWheelChanged(e);
        var deltaY = e.Delta.Y;
        if (deltaY == 0) return;

        var ctrl  = e.KeyModifiers.HasFlag(KeyModifiers.Control);
        var shift = e.KeyModifiers.HasFlag(KeyModifiers.Shift);

        // Ctrl + rotella in modalità gomma → ridimensiona il quadrato gomma
        if (IsEraserMode && ctrl)
        {
            EraserSize = Math.Clamp(EraserSize + (deltaY > 0 ? 1 : -1), 1, 64);
            e.Handled = true;
            return;
        }

        if (ctrl)
        {
            // Ctrl + rotella → zoom verso cursore (√2, con snap a detent)
            var p = e.GetPosition(this);
            var factor = deltaY > 0 ? WheelZoomFactor : 1.0 / WheelZoomFactor;
            SetZoomTowardScreenPoint(SnapZoomToDetent(Zoom * factor), p.X, p.Y);
        }
        else if (shift)
        {
            // Shift + rotella → pan orizzontale
            PanByWheel(deltaY * WheelPanStepPx, 0);
        }
        else
        {
            // Rotella sola → pan verticale
            PanByWheel(0, deltaY * WheelPanStepPx);
        }

        e.Handled = true;
    }

    /// <summary>
    /// Pan da rotella con clamp ai bordi del world. Convenzione: <c>delta > 0</c> sposta verso l'alto/sinistra
    /// (PanY/PanX positivi), coerente con scroll up = contenuto giù.
    /// </summary>
    private void PanByWheel(double dx, double dy)
    {
        if (!TryGetPanWorldSize(out var ww, out var wh)) return;
        var vw = Bounds.Width;
        var vh = Bounds.Height;
        if (vw <= 0 || vh <= 0) return;

        var z = Math.Max(Zoom, 0.0001);
        var iw = ww * z;
        var ih = wh * z;
        var panMinX = Math.Min(0, vw - iw);
        var panMaxX = Math.Max(0, vw - iw);
        var panMinY = Math.Min(0, vh - ih);
        var panMaxY = Math.Max(0, vh - ih);

        if (dx != 0) PanX = Math.Clamp(PanX + dx, panMinX, panMaxX);
        if (dy != 0) PanY = Math.Clamp(PanY + dy, panMinY, panMaxY);
    }

    protected override void OnPointerPressed(PointerPressedEventArgs e)
    {
        base.OnPointerPressed(e);
        var cur = e.GetCurrentPoint(this);
        var pos = e.GetPosition(this);

        if (cur.Properties.IsMiddleButtonPressed)
        {
            _midPanning = true; _lastPointer = pos;
            e.Pointer.Capture(this); e.Handled = true; return;
        }
        if (!cur.Properties.IsLeftButtonPressed) return;

        if (TryBeginFloatingDrag(pos, e)) return;
        if (IsEraserMode       && HandleEraserPress(pos, e))     return;
        if (IsFrameEditMode    && HandleFrameEditPress(pos, e))   return;
        if (IsCellClickMode    && HandleCellClickPress(pos, e))   return;
        if (IsSelectionMode    && HandleSelectionPress(pos, e))   return;
        if (IsPipetteMode)     { HandlePipettePress(pos, e); return; }

        _panning = true; _lastPointer = pos;
        e.Pointer.Capture(this);
    }

    private bool TryBeginFloatingDrag(Avalonia.Point pos, PointerPressedEventArgs e)
    {
        if (_floatingOverlayBitmap is null || IsEraserMode || IsPipetteMode
            || IsSelectionMode || IsCellClickMode || IsTilePreviewMode
            || (IsFrameEditMode && _frameCells.Count > 0)) return false;

        var (fwx, fwy) = ScreenToImageFloat(pos, PanX, PanY, Zoom);
        if (fwx < _floatingX || fwy < _floatingY
            || fwx >= _floatingX + _floatingW || fwy >= _floatingY + _floatingH) return false;

        _floatingDragging = true;
        _floatingGrabWx   = fwx - _floatingX;
        _floatingGrabWy   = fwy - _floatingY;
        e.Pointer.Capture(this); e.Handled = true;
        return true;
    }

    private bool HandleEraserPress(Avalonia.Point pos, PointerPressedEventArgs e)
    {
        _eraserDragging = true; _eraserCurScreen = pos;
        e.Pointer.Capture(this); FireEraserStroke(pos); e.Handled = true;
        return true;
    }

    private bool HandleFrameEditPress(Avalonia.Point pos, PointerPressedEventArgs e)
    {
        if (_frameCells.Count == 0) return false;
        var (wx, wy) = ScreenToImageFloat(pos, PanX, PanY, Zoom);
        for (var i = 0; i < _frameCells.Count; i++)
        {
            var c = _frameCells[i];
            if (wx >= c.MinX && wy >= c.MinY && wx < c.MaxX && wy < c.MaxY)
            {
                _selectedFrameIndex = i;
                FrameSelected?.Invoke(this, i);
                _frameDragging = true; _frameDragStartScreen = pos;
                e.Pointer.Capture(this); InvalidateVisual(); e.Handled = true;
                return true;
            }
        }
        return false;
    }

    private bool HandleCellClickPress(Avalonia.Point pos, PointerPressedEventArgs e)
    {
        if (_spriteCells.Count == 0) return false;
        var (wx, wy) = ScreenToImageFloat(pos, PanX, PanY, Zoom);
        for (var i = 0; i < _spriteCells.Count; i++)
        {
            var c = _spriteCells[i].BoundsInAtlas;
            if (wx >= c.MinX && wy >= c.MinY && wx < c.MaxX && wy < c.MaxY)
            {
                CellClicked?.Invoke(this, i); e.Handled = true;
                return true;
            }
        }
        return false;
    }

    private bool HandleSelectionPress(Avalonia.Point pos, PointerPressedEventArgs e)
    {
        var bmp = Bitmap;
        if (bmp is { PixelSize.Width: > 0, PixelSize.Height: > 0 }
            && !_selDragging
            && _committedSelection is { IsEmpty: false } csHit)
        {
            var grip = HitTestCommittedSelectionGrip(pos, csHit);
            if (grip != SelectionAdjustGrip.None)
            {
                _selAdjustDragging = true; _selAdjustGrip = grip; _selAdjustBoxStart = csHit;
                var (wpx, wpy) = ScreenToImageFloat(pos, PanX, PanY, Zoom);
                var (spx, spy) = SnapWorldPoint(wpx, wpy);
                _selAdjustPointerWorldStart = new Avalonia.Point(spx, spy);
                e.Pointer.Capture(this); InvalidateVisual(); e.Handled = true;
                return true;
            }
        }
        _selDragging = true; _selStartScreen = pos; _selCurScreen = pos;
        e.Pointer.Capture(this); InvalidateVisual(); e.Handled = true;
        return true;
    }

    private void HandlePipettePress(Avalonia.Point pos, PointerPressedEventArgs e)
    {
        _pipetteArmed = true; _pipetteDragCancel = false; _pipettePress = pos;
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
            if (TryGetEraserBox(screenPos, bmp.PixelSize.Width, bmp.PixelSize.Height, out var xMin, out var yMin, out _, out _))
                EraserStroke?.Invoke(this, new EraserStrokeEventArgs(xMin, yMin, EraserSize));
        }
    }

    private bool TryGetEraserBox(Avalonia.Point screenPos, int imageW, int imageH, out int xMin, out int yMin, out int xMax, out int yMax)
    {
        xMin = yMin = xMax = yMax = 0;
        if (imageW <= 0 || imageH <= 0) return false;

        var (wx, wy) = ScreenToImageFloat(screenPos, PanX, PanY, Zoom);
        var cx = (int)Math.Floor(wx);
        var cy = (int)Math.Floor(wy);
        if (cx < 0 || cy < 0 || cx >= imageW || cy >= imageH) return false;

        var size = Math.Max(1, EraserSize);
        var halfBefore = (size - 1) / 2;
        var rawXMin = cx - halfBefore;
        var rawYMin = cy - halfBefore;
        xMin = Math.Clamp(rawXMin, 0, imageW);
        yMin = Math.Clamp(rawYMin, 0, imageH);
        xMax = Math.Clamp(rawXMin + size, 0, imageW);
        yMax = Math.Clamp(rawYMin + size, 0, imageH);
        return xMin < xMax && yMin < yMax;
    }

    private AxisAlignedBox BuildSelectionBoxInWorld(Avalonia.Point screenA, Avalonia.Point screenB, int imgW, int imgH)
    {
        var (wx0, wy0) = ScreenToImageFloat(screenA, PanX, PanY, Zoom);
        var (wx1, wy1) = ScreenToImageFloat(screenB, PanX, PanY, Zoom);
        if (SnapToGrid && SnapGridSize > 0)
        {
            var worldThresh = SnapThreshold / Math.Max(Zoom, 0.0001);
            wx0 = SnapCoordToLines(wx0, _cachedXSnapLines, SnapGridSize, worldThresh);
            wy0 = SnapCoordToLines(wy0, _cachedYSnapLines, SnapGridSize, worldThresh);
            wx1 = SnapCoordToLines(wx1, _cachedXSnapLines, SnapGridSize, worldThresh);
            wy1 = SnapCoordToLines(wy1, _cachedYSnapLines, SnapGridSize, worldThresh);
        }
        return AxisAlignedBox.FromWorldCornersHalfOpen(wx0, wy0, wx1, wy1, imgW, imgH);
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

        // 4 corner points in screen space — reused for both edge segments
        var tl = WorldToScreen(cs.MinX, cs.MinY);
        var tr = WorldToScreen(cs.MaxX, cs.MinY);
        var bl = WorldToScreen(cs.MinX, cs.MaxY);
        var br = WorldToScreen(cs.MaxX, cs.MaxY);

        if (PointSegmentDistSq(screen, tl, tr) <= edgeHitSq) return SelectionAdjustGrip.N;
        if (PointSegmentDistSq(screen, bl, br) <= edgeHitSq) return SelectionAdjustGrip.S;
        if (PointSegmentDistSq(screen, tl, bl) <= edgeHitSq) return SelectionAdjustGrip.W;
        if (PointSegmentDistSq(screen, tr, br) <= edgeHitSq) return SelectionAdjustGrip.E;

        var (wx, wy) = ScreenToImageFloat(screen, PanX, PanY, Zoom);
        if (wx >= cs.MinX && wx < cs.MaxX && wy >= cs.MinY && wy < cs.MaxY)
            return SelectionAdjustGrip.Move;

        return SelectionAdjustGrip.None;
    }

    private (double sx, double sy) SnapWorldPoint(double wx, double wy)
    {
        if (!SnapToGrid || SnapGridSize <= 0) return (wx, wy);
        EnsureSnapLineCache();
        var worldThresh = SnapThreshold / Math.Max(Zoom, 0.0001);
        return (SnapCoordToLines(wx, _cachedXSnapLines, SnapGridSize, worldThresh),
                SnapCoordToLines(wy, _cachedYSnapLines, SnapGridSize, worldThresh));
    }

    // ─── Draw helpers ─────────────────────────────────────────────────────────

    private static void DrawCellOverlay(DrawingContext ctx, List<SpriteCell> cells, double zoom)
    {
        var strokeT = 1.5 / Math.Max(zoom, 0.0001);
        for (var i = 0; i < cells.Count; i++)
        {
            var c   = cells[i];
            var idx = i % CellPalette.Length;
            var rect = new Rect(c.BoundsInAtlas.MinX, c.BoundsInAtlas.MinY, c.BoundsInAtlas.Width, c.BoundsInAtlas.Height);
            ctx.FillRectangle(CellFillBrushes[idx], rect);
            ctx.DrawRectangle(new Pen(CellStrokeBrushes[idx], strokeT), rect);
        }
    }

    private static void DrawSliceGrid(DrawingContext ctx, int worldW, int worldH, int rows, int cols, Pen pen)
    {
        var (cellW, cellH) = GridSlicer.ComputeCellSize(worldW, worldH, rows, cols);
        if (cellW < 1 || cellH < 1) return;

        // SortedSet keeps values ordered — no OrderBy needed
        var xs = new SortedSet<double> { 0, worldW };
        for (var k = 1; k <= cols; k++) { var v = Math.Min(k * cellW, worldW); if (v >= 0 && v <= worldW) xs.Add(v); }

        var ys = new SortedSet<double> { 0, worldH };
        for (var k = 1; k <= rows; k++) { var v = Math.Min(k * cellH, worldH); if (v >= 0 && v <= worldH) ys.Add(v); }

        foreach (var x in xs)
            ctx.DrawLine(pen, new Avalonia.Point(x, 0), new Avalonia.Point(x, worldH));
        foreach (var y in ys)
            ctx.DrawLine(pen, new Avalonia.Point(0, y), new Avalonia.Point(worldW, y));
    }

    private static void DrawFrameWorkbench(DrawingContext ctx, List<AxisAlignedBox> cells, int selectedIdx,
        Pen penUnsel, Pen penSel, Pen penGuide, Pen penBaseline)
    {
        for (var i = 0; i < cells.Count; i++)
        {
            var c    = cells[i];
            var rect = new Rect(c.MinX, c.MinY, c.Width, c.Height);
            ctx.DrawRectangle(i == selectedIdx ? penSel : penUnsel, rect);

            if (i == selectedIdx)
            {
                var cx = c.MinX + c.Width  / 2.0;
                var cy = c.MinY + c.Height / 2.0;
                ctx.DrawLine(penGuide, new Avalonia.Point(cx, c.MinY), new Avalonia.Point(cx, c.MaxY));
                ctx.DrawLine(penGuide, new Avalonia.Point(c.MinX, cy), new Avalonia.Point(c.MaxX, cy));
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

    private static void DrawLightGrid(DrawingContext ctx, int worldW, int worldH, int step, Pen pen)
    {
        if (step < 1) return;
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
        int offsetX, int offsetY, int cellW, int cellH, int spacingX, int spacingY,
        Pen penMajor, Pen penGutter)
    {
        if (cellW < 1 || cellH < 1 || worldW < 1 || worldH < 1) return;
        var periodX = cellW + spacingX;
        var periodY = cellH + spacingY;
        if (periodX < 1 || periodY < 1) return;

        var hasGutter = spacingX > 0 || spacingY > 0;

        for (var vx = (double)offsetX; vx < worldW + 1e-6; vx += periodX)
        {
            if (vx >= 0 && vx <= worldW)
            {
                var sx = Math.Floor(vx) + 0.5;
                ctx.DrawLine(penMajor, new Avalonia.Point(sx, 0), new Avalonia.Point(sx, worldH));
            }
            if (hasGutter && spacingX > 0)
            {
                var ex = vx + cellW;
                if (ex >= 0 && ex <= worldW)
                {
                    var sx = Math.Floor(ex) + 0.5;
                    ctx.DrawLine(penGutter, new Avalonia.Point(sx, 0), new Avalonia.Point(sx, worldH));
                }
            }
        }

        for (var vy = (double)offsetY; vy < worldH + 1e-6; vy += periodY)
        {
            if (vy >= 0 && vy <= worldH)
            {
                var sy = Math.Floor(vy) + 0.5;
                ctx.DrawLine(penMajor, new Avalonia.Point(0, sy), new Avalonia.Point(worldW, sy));
            }
            if (hasGutter && spacingY > 0)
            {
                var ey = vy + cellH;
                if (ey >= 0 && ey <= worldH)
                {
                    var sy = Math.Floor(ey) + 0.5;
                    ctx.DrawLine(penGutter, new Avalonia.Point(0, sy), new Avalonia.Point(worldW, sy));
                }
            }
        }
    }
}

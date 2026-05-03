using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Diagnostics.CodeAnalysis;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Threading.Tasks;
using AiPixelScaler.Core.Geometry;
using AiPixelScaler.Core.Pipeline.Export;
using AiPixelScaler.Core.Pipeline.Imaging;
using AiPixelScaler.Core.Pipeline.Editor;
using AiPixelScaler.Core.Pipeline.Normalization;
using AiPixelScaler.Core.Pipeline.Slicing;
using AiPixelScaler.Core.Pipeline.Templates;
using AiPixelScaler.Core.Pipeline.Tiling;
using AiPixelScaler.Desktop.Controls;
using AiPixelScaler.Desktop.Controllers;
using AiPixelScaler.Desktop.Imaging;
using AiPixelScaler.Desktop.Services;
using AiPixelScaler.Desktop.Utilities;
using AiPixelScaler.Desktop.ViewModels;
using AiPixelScaler.Desktop.ViewModels.Studios;
using AiPixelScaler.Desktop.Views.Studios;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Controls.Primitives;
using Avalonia.Input;
using Avalonia.Input.Platform;
using Avalonia.Interactivity;
using Avalonia.Media.Imaging;
using Avalonia.Platform.Storage;
using Avalonia.Threading;
using SixLabors.ImageSharp;
using SixLabors.ImageSharp.Formats.Png;
using SixLabors.ImageSharp.PixelFormats;
using SixLabors.ImageSharp.Processing;

namespace AiPixelScaler.Desktop.Views;

public partial class MainWindow : Window
{
    /// <summary>Allineato a <see cref="EditorSurface"/> rotellina / <see cref="EditorSurface.SetZoomTowardScreenPoint"/>.</summary>
    private const double WorkspaceZoomSliderMin = 0.05;
    private const double WorkspaceZoomSliderMax = 64.0;

    private const int MaxUndo = 20;

    private Image<Rgba32>? _document;
    private Image<Rgba32>? _backup;
    private List<SpriteCell> _cells = new();
    private bool _hasUserFile;
    private bool _cleanApplied;
    private readonly WorkspaceUndoCoordinator _undoCoordinator;
    private readonly FloatingPasteCoordinator _floatingPaste;
    private readonly PipelineViewModel _pipelineVm = new();
    private PipelineViewModel.PipelineFormState _pipelineFormState = new(
        EnableBackgroundIsolation: false,
        EnableBackgroundSnapRgb: false,
        BackgroundHex: "#00FF00",
        BackgroundTolerance: "0",
        EnableQuantize: false,
        MaxColors: "16",
        QuantizerIndex: 0,
        EnableMajorityDenoise: false,
        MinIsland: "2",
        EnableOutline: false,
        OutlineHex: "#000000",
        EnableAlphaThreshold: true,
        AlphaThreshold: "128",
        DefringeOpaque: "250",
        MajorityMinNeighbors: "1");
    private string _backgroundIsolationHex = "#00FF00";
    private string _backgroundIsolationTolerance = "10";
    private string _backgroundIsolationEdgeThreshold = "100"; // soglia Sobel: 100 ignora artefatti JPEG (<80), blocca bordi sprite (200+)
    /// <summary>
    /// Coordinata immagine dell'ultima pick con la pipetta sfondo.
    /// Se impostata, viene passata come seed aggiuntivo al prossimo <see cref="RunBackgroundIsolation"/>
    /// così il flood raggiunge anche pixel residui non connessi al bordo.
    /// Viene azzerata dopo ogni utilizzo (one-shot).
    /// </summary>
    private (int X, int Y)? _lastPipettePoint;
    private string _quickColorsAfterText = "-";
    private bool _isApplyingPipelinePreset;
    private readonly WorkflowShellViewModel _workflowShell = new();
    private readonly DelegateCommand _runQuickProcessCommand;
    private readonly DelegateCommand _applyDefaultPresetCommand;
    private readonly DelegateCommand _applySafePresetCommand;
    private readonly DelegateCommand _applyAggressivePresetCommand;
    private readonly DelegateCommand _applyPipelineCommand;
    private readonly WorkspaceTabsController _workspaceTabs = new();
    private readonly AiPixelScaler.Desktop.Services.UiPreferencesService _uiPrefs = new();
    private bool _workspaceTabSwitching;
    private bool _workspaceScrollBarSync;
    private bool _workspaceZoomSliderSync;
    private int _workspaceScrollBarLayoutDepth;
    private StudioKind _currentStudio = StudioKind.Start;
    private TilesetState _tilesetState = new(
        PalettePresetIndex: 0,
        PaletteColors: "16",
        DitherEnabled: false,
        SeamlessBlend: "4",
        PadMultiple: "16",
        CellW: "64",
        CellH: "64",
        Cols: "4",
        Rows: "4",
        PivotIndex: 4,
        PivotCustomX: 0.5,
        PivotCustomY: 0.5,
        ShowBorder: true,
        ShowPivot: true,
        ShowBaseline: true,
        ShowIndex: true,
        ShowTint: false,
        CropModeIndex: 0,
        CropAlpha: "1",
        CropPadding: "4",
        CropPotIndex: 0);

    private AnimationState _animationState = new(
        SnapToGrid:           true,
        ExtractPadding:       "0",
        NormalizePolicyIndex: 0,
        PivotX:               0.5,
        PivotY:               0.5);

    // ── Selezione canvas ─────────────────────────────────────────────────────
    private bool             _toolbarSelectionModeEnabled;
    private bool             _manualSpriteCropMode;
    private AxisAlignedBox?  _activeSelectionBox;    // ultima selezione confermata
    private int              _selectedSpriteCellIndex = -1;

    public MainWindow()
    {
        InitializeComponent();
        _runQuickProcessCommand = new DelegateCommand(RunQuickProcess, () => _document is not null);
        _applyDefaultPresetCommand = new DelegateCommand(ApplyDefaultPresetToControls, () => _document is not null);
        _applySafePresetCommand = new DelegateCommand(ApplySafePresetToControls, () => _document is not null);
        _applyAggressivePresetCommand = new DelegateCommand(ApplyAggressivePresetToControls, () => _document is not null);
        _applyPipelineCommand = new DelegateCommand(RunPixelPipeline, () => _document is not null);
        _undoCoordinator = new WorkspaceUndoCoordinator(MenuUndo, BtnUndo, MaxUndo, () =>
        {
            _workspaceTabs.MarkActiveDirty();
            RefreshWorkspaceChrome();
        });
        _floatingPaste = new FloatingPasteCoordinator(Editor);
        LoadWelcomeDocument();
        StudioShell.IsVisible = false;
        StartPage.StudioSelected += (_, studio) => ActivateStudio(studio);
        SpriteStudioPanel.ActionRequested    += OnSpriteStudioActionRequested;
        TilesetStudioPanel.ActionRequested   += OnTilesetStudioActionRequested;
        AnimationStudioPanel.ActionRequested += OnAnimationStudioActionRequested;
        TilesetStudioPanel.GridSection.ActionRequested   += OnGridSectionActionRequested;
        AnimationStudioPanel.GridSection.ActionRequested += OnGridSectionActionRequested;

        AddHandler(KeyDownEvent, OnWindowKeyDown, RoutingStrategies.Tunnel);

        // Menu
        MenuOpen.Click    += async (_, _) => await OpenImageAsync();
        MenuRevert.Click  += (_, _) => RevertToBackup();
        MenuExit.Click    += (_, _) => Close();
        MenuSandbox.Click += (_, _) => OpenSandbox();
        MenuUndo.Click    += (_, _) => TryUndo();
        MenuCopyImage.Click += async (_, _) => await TryCopyImageToClipboardAsync();
        MenuPasteImage.Click += async (_, _) => await TryPasteFromClipboardAsync();

        // Toolbar
        BtnOpen.Click    += async (_, _) => await OpenImageAsync();
        BtnRevert.Click  += (_, _) => RevertToBackup();
        BtnUndo.Click    += (_, _) => TryUndo();
        // Preview / Sandbox ora in AnimationStudioPanel
        BtnToolbarRilevaSprite.Click += (_, _) => RunCcl();
        MiToolbarSelectionMode.Click += (_, _) => ToggleToolbarSelectionMode();
        MiToolbarCropToSelection.Click += (_, _) => CropToSelection();
        MiToolbarRemoveSelectedArea.Click += (_, _) => RemoveSelectedArea();
        MiToolbarExportSelection.Click += async (_, _) => await ExportSelectionAsync();
        MenuAnimPreview.Click += (_, _) => OpenAnimationPreview();
        EmptyStateOpen.Click += async (_, _) => await OpenImageAsync();

        // Canvas
        ChkWorldGrid.IsCheckedChanged += (_, _) =>
            Editor.ShowGrid = ChkWorldGrid.IsChecked == true;
        TxtWorldGridSize.ValueChanged += (_, _) =>
        {
            Editor.WorldGridSize = (int)(TxtWorldGridSize.Value ?? 16);
            TxtSnapSize.Value = TxtWorldGridSize.Value;
        };

        ChkSnapGrid.IsCheckedChanged += (_, _) =>
        {
            var on = ChkSnapGrid.IsChecked == true;
            Editor.SnapToGrid     = on;
            TxtSnapSize.IsEnabled = on;
            // Quando si attiva la magnetica, sincronizza il passo con la griglia principale.
            if (on)
                TxtSnapSize.Value = TxtWorldGridSize.Value;
        };
        TxtSnapSize.ValueChanged += (_, _) =>
            Editor.SnapGridSize = (int)(TxtSnapSize.Value ?? 16);
        BtnCenterCanvas.Click += (_, _) => RunCenterCanvas();

        ChkEraser.IsCheckedChanged += (_, _) =>
        {
            var on = ChkEraser.IsChecked == true;
            Editor.IsEraserMode        = on;
            EraserSizePanel.IsEnabled  = on;
            // Modalità mutualmente esclusive
            if (on)
            {
                ChkPipette.IsChecked    = false;
                SetManualSpriteCropMode(false);
            }
        };

        // Sincronizza NumericUpDown → EditorSurface
        TxtEraserSize.ValueChanged += (_, _) =>
            Editor.EraserSize = (int)(TxtEraserSize.Value ?? 1);

        // Sincronizza EditorSurface → NumericUpDown (es. Ctrl+rotella)
        Editor.PropertyChanged += (_, args) =>
        {
            if (args.Property == AiPixelScaler.Desktop.Controls.EditorSurface.EraserSizeProperty)
                TxtEraserSize.Value = Editor.EraserSize;
        };

        // Preset rapidi dimensione gomma
        BtnEraserSize1.Click += (_, _) => TxtEraserSize.Value = 1;
        BtnEraserSize2.Click += (_, _) => TxtEraserSize.Value = 2;
        BtnEraserSize4.Click += (_, _) => TxtEraserSize.Value = 4;
        BtnEraserSize8.Click += (_, _) => TxtEraserSize.Value = 8;

        Editor.EraserStroke += OnEraserStroke;
        Editor.EraserStrokeEnded += OnEraserStrokeEnded;
        ChkPipette.IsCheckedChanged += (_, _) => UpdatePipetteMode();
        Editor.ImagePixelPicked += OnEditorImagePixelPicked;
        Editor.ImageSelectionCompleted += OnEditorImageSelectionCompleted;
        Editor.CommittedSelectionEdited += OnEditorCommittedSelectionEdited;
        Editor.FloatingOverlayMoved += OnEditorFloatingOverlayMoved;

        InitWorkspaceScrollBars();

        // Pivot: gestito in AnimationStudioPanel

        _isApplyingPipelinePreset = true;
        try
        {
            ApplyPipelineFormStateToControls(_pipelineVm.ToFormState());
        }
        finally
        {
            _isApplyingPipelinePreset = false;
        }

        UpdatePipelinePresetBadge();

        // Passo 3 — Allinea: ora in AnimationStudioPanel

        Editor.CellClicked   += (_, idx) => OnCellPasteClicked(idx);

        // Pannello destro — comprimi/espandi
        BtnCollapsePanel.Click += (_, _) => SetPanelCollapsed(true);
        BtnExpandPanel.Click   += (_, _) => SetPanelCollapsed(false);

        MainTabs.SelectionChanged += OnMainTabChanged;
        InitAlignGridPanel();
        BtnWorkflowPrimaryAction.Click += async (_, _) => await RunWorkspaceGuideActionAsync();
        BtnWorkflowNextStep.Click += async (_, _) => await AdvanceWorkflowStepAsync();
        BtnStepImporta.Click += (_, _) => ExecuteSelectStepCommand(WorkflowShellViewModel.WorkflowStep.Importa);
        BtnStepPulisci.Click += (_, _) => ExecuteSelectStepCommand(WorkflowShellViewModel.WorkflowStep.Pulisci);
        BtnStepSliceAllinea.Click += (_, _) => ExecuteSelectStepCommand(WorkflowShellViewModel.WorkflowStep.SliceAllinea);
        BtnStepEsporta.Click += (_, _) => ExecuteSelectStepCommand(WorkflowShellViewModel.WorkflowStep.Esporta);
        BtnGoSprite.Click += (_, _) => ActivateStudio(StudioKind.Sprite);
        BtnGoAllinea.Click  += (_, _) => ActivateStudio(StudioKind.Animation);
        BtnGoTemplate.Click += (_, _) => ActivateStudio(StudioKind.Tileset);
        ChkExportCustomCellSize.IsCheckedChanged += (_, _) =>
        {
            var enabled = ChkExportCustomCellSize.IsChecked == true;
            TxtExportCellW.IsEnabled = enabled;
            TxtExportCellH.IsEnabled = enabled;
            UpdateExportCellMinRequiredHint();
        };
        TxtExportCellW.TextChanged += (_, _) => UpdateExportCellMinRequiredHint();
        TxtExportCellH.TextChanged += (_, _) => UpdateExportCellMinRequiredHint();
        // CenterInCells / SnapCells: ora in AnimationStudioPanel / TilesetStudioPanel
        Editor.FrameSelected += OnFrameSelected;
        Editor.FrameDragged  += OnFrameDragged;

        // Export
        BtnExportTiled.Click += async (_, _) => await ExportTiledMapJsonAsync();
        // ExportFramesZip: ora in AnimationStudioPanel

        InitializeWorkspaceTabs();
        UpdateUndoUi();
        UpdateWorkspaceGuidance();
    }

    private void ActivateStudio(StudioKind studio)
    {
        _currentStudio = studio;
        StartPage.IsVisible = studio == StudioKind.Start;
        StudioShell.IsVisible = studio != StudioKind.Start;
        if (studio == StudioKind.Start)
            return;

        MainTabs.SelectedIndex = studio switch
        {
            StudioKind.Sprite => 0,
            StudioKind.Animation => 0,
            StudioKind.Tileset => 0,
            _ => MainTabs.SelectedIndex
        };
        SpriteStudioPanel.IsVisible    = studio == StudioKind.Sprite;
        TilesetStudioPanel.IsVisible   = studio == StudioKind.Tileset;
        AnimationStudioPanel.IsVisible = studio == StudioKind.Animation;

        // Mantiene la griglia sincronizzata tra i due studio che la condividono
        var gridState = GridStateFromTilesetState();
        if (studio == StudioKind.Tileset)
            TilesetStudioPanel.GridSection.SetGridState(gridState);
        else if (studio == StudioKind.Animation)
            AnimationStudioPanel.GridSection.SetGridState(gridState);

        SetStatus($"{studio switch
        {
            StudioKind.Sprite => "Sprite Studio",
            StudioKind.Animation => "Animation Studio",
            StudioKind.Tileset => "Tileset Studio",
            _ => "Studio"
        }} attivo.");
        EnterSelectionCanvas(IsRoiSelectionModeRequested());
    }

    private async void OnSpriteStudioActionRequested(object? sender, SpriteStudioAction action)
    {
        try
        {
            switch (action)
            {
            case SpriteStudioAction.CreateBlankCanvas:
                await CreateBlankCanvasAsync();
                break;
            case SpriteStudioAction.OpenImage:
                await OpenImageAsync();
                break;
            case SpriteStudioAction.ApplyDefaultPreset:
                ApplyDefaultPresetToControls();
                break;
            case SpriteStudioAction.ApplySafePreset:
                ApplySafePresetToControls();
                break;
            case SpriteStudioAction.ApplyAggressivePreset:
                ApplyAggressivePresetToControls();
                break;
            case SpriteStudioAction.RunCleanup:
                ApplySpriteCleanupStateToControls();
                RunQuickProcess();
                break;
            case SpriteStudioAction.RunOneClickCleanup:
                ApplySpriteCleanupStateToControls();
                RunAiCleanupWizard();
                break;
            case SpriteStudioAction.ApplyPipeline:
                ApplySpriteCleanupStateToControls();
                RunPixelPipeline();
                break;
            case SpriteStudioAction.ApplyDefringe:
                ApplySpriteCleanupStateToControls();
                RunDefringe();
                break;
            case SpriteStudioAction.ApplyMedian:
                RunMedianFilter();
                break;
            case SpriteStudioAction.ApplyBackgroundIsolation:
                ApplySpriteCleanupStateToControls();
                RunBackgroundIsolation();
                break;
            case SpriteStudioAction.ApplyGlobalChromaKey:
                ApplySpriteCleanupStateToControls();
                RunGlobalChromaKey();
                break;
            case SpriteStudioAction.MorphologyErode:
                RunMorphology(MorphOp.Erode);
                break;
            case SpriteStudioAction.MorphologyDilate:
                RunMorphology(MorphOp.Dilate);
                break;
            case SpriteStudioAction.MorphologyOpen:
                RunMorphology(MorphOp.Open);
                break;
            case SpriteStudioAction.MorphologyClose:
                RunMorphology(MorphOp.Close);
                break;
            case SpriteStudioAction.RemoveIsolatedIslands:
                RunRemoveIsolatedIslands();
                break;
            case SpriteStudioAction.RemoveColorOutliers:
                RunRemoveColorOutliers();
                break;
            case SpriteStudioAction.ActivatePipetteForBackground:
                ActivatePipette(0);
                break;
            case SpriteStudioAction.ApplyIslandCleanup:
                ApplySpriteCleanupStateToControls();
                RunIslandCleanup();
                break;
            case SpriteStudioAction.ApplyMajorityDenoise:
                RunMajorityDenoise();
                break;
            case SpriteStudioAction.SelectArea:
                if (!_toolbarSelectionModeEnabled)
                    ToggleToolbarSelectionMode();
                break;
            case SpriteStudioAction.SelectAll:
                SelectAll();
                break;
            case SpriteStudioAction.ClearSelection:
                ClearSelection();
                break;
            case SpriteStudioAction.ExportSelection:
                await ExportSelectionAsync();
                break;
            case SpriteStudioAction.CropSelection:
                CropToSelection();
                break;
            case SpriteStudioAction.RemoveSelection:
                RemoveSelectedArea();
                break;
            case SpriteStudioAction.ToggleManualCrop:
                SetManualSpriteCropMode(SpriteStudioPanel.IsManualCropEnabled);
                break;
            case SpriteStudioAction.DetectSprites:
                RunCcl();
                break;
            case SpriteStudioAction.GridSlice:
                RunGridSlice();
                break;
            case SpriteStudioAction.SaveSelectedFrame:
                _selectedSpriteCellIndex = SpriteStudioPanel.SelectedCellIndex;
                await SaveSelectedFrameAsync(_selectedSpriteCellIndex);
                break;
            case SpriteStudioAction.ExportAllFramesZip:
                await ExportFramesZipAsync();
                break;
            case SpriteStudioAction.OpenFloatingPaste:
                EnterPasteMode();
                break;
            case SpriteStudioAction.PasteDestination:
                SwitchPasteView(toSource: false);
                break;
            case SpriteStudioAction.PasteSource:
                SwitchPasteView(toSource: true);
                break;
            case SpriteStudioAction.CopyPasteSelection:
                CopySourceSelectionToBuffer();
                break;
            case SpriteStudioAction.ExitFloatingPaste:
                ExitPasteMode();
                break;
            case SpriteStudioAction.ApplyQuantize:
                RunSpriteQuantize();
                break;
            case SpriteStudioAction.AnalyzePalette:
                RunAnalyzePalette();
                break;
            case SpriteStudioAction.PaletteColorPickedAsBackground:
                // Il TextBox è già aggiornato dallo swatch click; sincronizza solo il campo interno.
                _backgroundIsolationHex = SpriteStudioPanel.GetCleanupState().BackgroundHex;
                SetStatus($"Colore sfondo impostato dalla palette: {_backgroundIsolationHex}");
                break;
            case SpriteStudioAction.MirrorHorizontal:
                RunMirror(horizontal: true);
                break;
            case SpriteStudioAction.MirrorVertical:
                RunMirror(horizontal: false);
                break;
            case SpriteStudioAction.ResizeNearest:
                RunNearestResize();
                break;
            case SpriteStudioAction.ExportPng:
                await ExportPngAsync();
                break;
            case SpriteStudioAction.ExportJson:
                await ExportJsonAsync();
                break;
            }
        }
        catch (Exception ex)
        {
            ReportStudioActionFailure(ex, "Sprite Studio", action);
        }
    }

    private async void OnTilesetStudioActionRequested(object? sender, TilesetStudioAction action)
    {
        try
        {
            switch (action)
            {
            case TilesetStudioAction.OpenImage:
                await OpenImageAsync();
                break;
            case TilesetStudioAction.ApplyPalette:
                ApplyTilesetStateToControls();
                RunPaletteReduce();
                break;
            case TilesetStudioAction.MakeTileable:
                ApplyTilesetStateToControls();
                RunMakeTileable();
                break;
            case TilesetStudioAction.ToggleTilePreview:
                ApplyTilesetStateToControls();
                Editor.IsTilePreviewMode = TilesetStudioPanel.IsTilePreviewEnabled;
                break;
            case TilesetStudioAction.PadToMultiple:
                ApplyTilesetStateToControls();
                RunPadToMultiple();
                break;
            case TilesetStudioAction.ExportTiledJson:
                await ExportTiledMapJsonAsync();
                break;
            case TilesetStudioAction.ApplyCropPot:
                ApplyTilesetStateToControls();
                RunCropPipeline();
                break;
            case TilesetStudioAction.SnapCellsToGrid:
                RunSnapCellsToGrid();
                break;
            }
        }
        catch (Exception ex)
        {
            ReportStudioActionFailure(ex, "Tileset Studio", action);
        }
    }

    private void ApplyTilesetStateToControls() =>
        _tilesetState = TilesetStudioPanel.GetTilesetState();

    // ── Grid Section (condivisa tra Tileset e Animation Studio) ──────────────

    private async void OnGridSectionActionRequested(object? sender, GridSectionAction action)
    {
        try
        {
            if (sender is GridSectionView gs)
                ApplyGridStateFromSection(gs);

            switch (action)
            {
                case GridSectionAction.GenerateTemplate:
                    RunGenerateTemplate();
                    break;
                case GridSectionAction.ExportTemplatePng:
                    await ExportTemplateAsync();
                    break;
                case GridSectionAction.ImportFrames:
                    await RunImportFramesAsync();
                    break;
            }
        }
        catch (Exception ex)
        {
            ReportStudioActionFailure(ex, "Griglia", action);
        }
    }

    private void ApplyGridStateFromSection(GridSectionView gs)
    {
        var g = gs.GetGridState();
        _tilesetState = _tilesetState with
        {
            CellW        = g.CellW,
            CellH        = g.CellH,
            Cols         = g.Cols,
            Rows         = g.Rows,
            PivotIndex   = g.PivotIndex,
            PivotCustomX = g.PivotCustomX,
            PivotCustomY = g.PivotCustomY,
            ShowBorder   = g.ShowBorder,
            ShowPivot    = g.ShowPivot,
            ShowBaseline = g.ShowBaseline,
            ShowIndex    = g.ShowIndex,
            ShowTint     = g.ShowTint,
        };
    }

    private GridState GridStateFromTilesetState() => new(
        CellW:        _tilesetState.CellW,
        CellH:        _tilesetState.CellH,
        Cols:         _tilesetState.Cols,
        Rows:         _tilesetState.Rows,
        PivotIndex:   _tilesetState.PivotIndex,
        PivotCustomX: _tilesetState.PivotCustomX,
        PivotCustomY: _tilesetState.PivotCustomY,
        ShowBorder:   _tilesetState.ShowBorder,
        ShowPivot:    _tilesetState.ShowPivot,
        ShowBaseline: _tilesetState.ShowBaseline,
        ShowIndex:    _tilesetState.ShowIndex,
        ShowTint:     _tilesetState.ShowTint);

    // ── Animation Studio ─────────────────────────────────────────────────────

    private void ApplyAnimationStateToControls() =>
        _animationState = AnimationStudioPanel.GetAnimationState();

    private async void OnAnimationStudioActionRequested(object? sender, AnimationStudioAction action)
    {
        try
        {
            switch (action)
            {
            case AnimationStudioAction.OpenAnimationPreview:
                OpenAnimationPreview();
                break;
            case AnimationStudioAction.OpenSandbox:
                OpenSandbox();
                break;
            case AnimationStudioAction.EnterFrameWorkbench:
                ApplyAnimationStateToControls();
                EnterFrameAlignMode();
                break;
            case AnimationStudioAction.CommitFrameWorkbench:
                CommitFrameAlignMode();
                break;
            case AnimationStudioAction.CancelFrameWorkbench:
                CancelFrameAlignMode();
                break;
            case AnimationStudioAction.AlignAllCenter:
                ApplyAnimationStateToControls();
                AlignAllFramesCenter();
                break;
            case AnimationStudioAction.AlignAllBaseline:
                AlignAllFramesBaseline();
                break;
            case AnimationStudioAction.ResetAll:
                ResetAllFrames();
                break;
            case AnimationStudioAction.AlignSelectedCenter:
                ApplyAnimationStateToControls();
                AlignSelectedFrame(center: true);
                break;
            case AnimationStudioAction.AlignSelectedBaseline:
                AlignSelectedFrame(center: false);
                break;
            case AnimationStudioAction.AlignSelectedLayoutCenter:
                AlignSelectedFrameToLayoutCenter();
                break;
            case AnimationStudioAction.ResetSelected:
                ResetSelectedFrame();
                break;
            case AnimationStudioAction.RunGlobalScan:
                RunGlobalScan();
                break;
            case AnimationStudioAction.RunBaselineAlignment:
                ApplyAnimationStateToControls();
                RunBaselineAlignment();
                break;
            case AnimationStudioAction.RunCenterInCells:
                ApplyAnimationStateToControls();
                RunCenterInCells();
                break;
            case AnimationStudioAction.ImportFromVideo:
                await ImportFramesFromVideoAsync();
                break;
            case AnimationStudioAction.ExportFramesZip:
                await ExportFramesZipAsync();
                break;
            }
        }
        catch (Exception ex)
        {
            ReportStudioActionFailure(ex, "Animation Studio", action);
        }
    }

    private void ApplySpriteCleanupStateToControls()
    {
        var state = SpriteStudioPanel.GetCleanupState();
        _backgroundIsolationHex = state.BackgroundHex;
        _backgroundIsolationTolerance = state.BackgroundTolerance;
        _backgroundIsolationEdgeThreshold = state.BackgroundEdgeThreshold;
        _pipelineFormState = _pipelineFormState with
        {
            EnableBackgroundIsolation = false,
            BackgroundHex = state.BackgroundHex,
            BackgroundTolerance = state.BackgroundTolerance,
            EnableBackgroundSnapRgb = false,
            EnableAlphaThreshold = state.EnableAlphaThreshold,
            AlphaThreshold = state.AlphaThreshold,
            DefringeOpaque = state.DefringeOpaque,
            EnableOutline = state.EnableOutline,
            OutlineHex = state.OutlineHex,
            EnableMajorityDenoise = state.EnableMajorityDenoise,
            MinIsland = state.MinIsland,
            MajorityMinNeighbors = state.MajorityMinNeighbors,
            EnableQuantize = state.EnableQuantize,
            MaxColors = state.QuantizeColors,
            QuantizerIndex = state.QuantizeMethodIndex
        };
    }

    private void SyncSpriteCleanupStateFromControls()
    {
        SpriteStudioPanel.SetCleanupState(new SpriteCleanupState(
            BackgroundHex: _backgroundIsolationHex,
            BackgroundTolerance: _backgroundIsolationTolerance,
            BackgroundEdgeThreshold: _backgroundIsolationEdgeThreshold,
            EnableAlphaThreshold: _pipelineFormState.EnableAlphaThreshold,
            AlphaThreshold: _pipelineFormState.AlphaThreshold,
            DefringeOpaque: _pipelineFormState.DefringeOpaque,
            EnableOutline: _pipelineFormState.EnableOutline,
            OutlineHex: _pipelineFormState.OutlineHex,
            MinIsland: _pipelineFormState.MinIsland,
            EnableMajorityDenoise: _pipelineFormState.EnableMajorityDenoise,
            MajorityMinNeighbors: _pipelineFormState.MajorityMinNeighbors,
            EnableQuantize: _pipelineFormState.EnableQuantize,
            QuantizeColors: _pipelineFormState.MaxColors,
            QuantizeMethodIndex: _pipelineFormState.QuantizerIndex));
    }

    private void OnWindowKeyDown(object? sender, KeyEventArgs e)
    {
        if (FocusManager?.GetFocusedElement() is TextBox) return;

        if (_floatingPaste.HasActiveSession)
        {
            if (e.Key == Key.Enter && e.KeyModifiers == KeyModifiers.None)
            {
                CommitFloatingPaste();
                e.Handled = true;
                return;
            }
            if (e.Key == Key.Escape && e.KeyModifiers == KeyModifiers.None)
            {
                CancelFloatingPaste();
                e.Handled = true;
                return;
            }
        }

        if (e.Key == Key.V && e.KeyModifiers.HasFlag(KeyModifiers.Control))
        {
            _ = TryPasteFromClipboardAsync();
            e.Handled = true;
            return;
        }

        if (e.Key == Key.C && e.KeyModifiers.HasFlag(KeyModifiers.Control))
        {
            _ = TryCopyImageToClipboardAsync();
            e.Handled = true;
            return;
        }

        if (e.Key == Key.Delete && e.KeyModifiers == KeyModifiers.None)
        {
            if (_document is not null && _activeSelectionBox is not null)
            {
                RemoveSelectedArea();
                e.Handled = true;
            }
            return;
        }

        if (e.Key == Key.Z && e.KeyModifiers.HasFlag(KeyModifiers.Control))
        {
            TryUndo();
            e.Handled = true;
            return;
        }
    }

    private void TryUndo()
    {
        _floatingPaste.ClearSession();
        if (!_undoCoordinator.TryPop(out var s))
        {
            SetStatus("Niente da annullare.");
            return;
        }

        _document?.Dispose();
        _document = s!.Image;
        _cells = s.Cells;
        Editor.SliceGridRows = s.GridRows;
        Editor.SliceGridCols = s.GridCols;
        Editor.SpriteCells = s.SpriteOverlay;
        RefreshCellList();
        RefreshView();
        UpdateUndoUi();
        _activeSelectionBox = null;
        Editor.SetCommittedSelection(null);
        UpdateSelectionInfo();
        SetStatus("Operazione annullata.");
    }

    private bool PushUndo() =>
        _undoCoordinator.Push(_document, _cells, Editor.SliceGridRows, Editor.SliceGridCols, Editor.SpriteCells);

    private void ClearUndoStack() => _undoCoordinator.ClearAndDisposeAll();

    private void UpdateUndoUi() => _undoCoordinator.UpdateUndoUi();

    private void RefreshWorkspaceChrome()
    {
        BtnWorkspaceCloseTab.IsEnabled = _workspaceTabs.CanClose;
        TxtWorkspaceContext.Text = _workspaceTabs.ContextText;
    }

    /// <summary>
    /// Workspace attivo con contenuto da non sovrascrivere aprendo un file (non solo atlas di benvenuto intatto).
    /// </summary>
    private bool IsActiveWorkspaceOccupied()
    {
        if (_hasUserFile) return true;
        if (_workspaceTabs.ActiveTab?.IsDirty == true) return true;
        if (_cells.Count > 0) return true;
        if (Editor.SpriteCells.Count > 0) return true;
        if (Editor.SliceGridRows > 0 && Editor.SliceGridCols > 0) return true;
        if (_undoCoordinator.Snapshots.Count > 0) return true;
        return false;
    }

    private static string WorkspaceTitleFromStorageFile(IStorageFile file)
    {
        var name = file.Name;
        if (string.IsNullOrWhiteSpace(name))
            return "Immagine";
        var baseName = Path.GetFileNameWithoutExtension(name);
        var segment = string.IsNullOrWhiteSpace(baseName) ? name : baseName;
        return SanitizeFileSegment(segment);
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

    private WorkspaceTabViewModel CaptureWorkspaceState(string title, bool isDirty) =>
        WorkspaceRuntimeAdapter.CaptureTabState(
            title,
            isDirty,
            _document,
            _backup,
            _cells,
            _undoCoordinator.Snapshots,
            _hasUserFile,
            _cleanApplied,
            true,
            Editor.SliceGridRows,
            Editor.SliceGridCols,
            Editor.SpriteCells,
            CreateWelcomeAtlas);

    private void RestoreWorkspaceState(WorkspaceTabViewModel state)
    {
        ClearFloatingPasteSession();
        _document?.Dispose();
        _backup?.Dispose();

        var runtime = WorkspaceRuntimeAdapter.RestoreRuntimeSnapshot(state);
        _document = runtime.Document;
        _backup = runtime.Backup;
        _cells = runtime.Cells;
        _hasUserFile = runtime.HasUserFile;
        _cleanApplied = runtime.CleanApplied;
        Editor.SliceGridRows = runtime.GridRows;
        Editor.SliceGridCols = runtime.GridCols;
        Editor.SpriteCells = runtime.SpriteOverlay;
        _undoCoordinator.ImportReplacing(runtime.UndoStack);

        _activeSelectionBox = null;
        Editor.SetCommittedSelection(null);
        UpdateSelectionInfo();
        RefreshCellList();
        RefreshView();
        RefreshEmptyState();
        UpdateUndoUi();
    }

    private WorkspaceTabViewModel CreateWelcomeWorkspaceState(string title)
        => WorkspaceStateFactory.CreateWelcome(title, CreateWelcomeAtlas);

    private WorkspaceTabViewModel CreateWorkspaceStateFromImage(Image<Rgba32> image, string title)
        => WorkspaceStateFactory.CreateFromImage(image, title);

    private void SaveCurrentWorkspaceState()
    {
        var current = _workspaceTabs.ActiveTab;
        if (current is null)
            return;
        var replacement = CaptureWorkspaceState(current.Title, current.IsDirty);
        _workspaceTabs.ReplaceActive(replacement);
        RefreshWorkspaceChrome();
    }

    private void SwitchToWorkspace(int index)
    {
        if (!_workspaceTabs.TryActivate(index))
            return;
        _workspaceTabSwitching = true;
        try
        {
            var active = _workspaceTabs.ActiveTab;
            if (active is null) return;
            RestoreWorkspaceState(active);
            WorkspaceTabs.SelectedIndex = index;
            RefreshWorkspaceChrome();
            SetStatus($"Workspace attivo: {WorkspaceTabs.SelectedItem ?? active.Title}.");
        }
        finally
        {
            _workspaceTabSwitching = false;
        }
    }

    private void InitializeWorkspaceTabs()
    {
        WorkspaceTabs.ItemsSource = _workspaceTabs.StripItems;
        WorkspaceTabs.SelectionChanged += OnWorkspaceTabsSelectionChanged;
        BtnWorkspaceNewTab.Click += (_, _) => AddNewWorkspaceTab();
        BtnWorkspaceCloseTab.Click += (_, _) => CloseActiveWorkspaceTab();
        BtnWorkspaceNewFromClipboard.Click += async (_, _) => await AddWorkspaceFromClipboardAsync();
        _workspaceTabs.Initialize(CaptureWorkspaceState("Workspace 1", isDirty: false));
        RefreshWorkspaceChrome();
        WorkspaceTabs.SelectedIndex = 0;
    }

    private void OnWorkspaceTabsSelectionChanged(object? sender, SelectionChangedEventArgs e)
    {
        if (_workspaceTabSwitching) return;
        var idx = WorkspaceTabs.SelectedIndex;
        if (idx < 0 || idx == _workspaceTabs.ActiveIndex) return;
        SaveCurrentWorkspaceState();
        SwitchToWorkspace(idx);
    }

    private void AddNewWorkspaceTab()
    {
        SaveCurrentWorkspaceState();
        var title = $"Workspace {_workspaceTabs.Count + 1}";
        _workspaceTabs.AddAndActivate(CreateWelcomeWorkspaceState(title));
        RefreshWorkspaceChrome();
        SwitchToWorkspace(_workspaceTabs.ActiveIndex);
    }

    private void CloseActiveWorkspaceTab()
    {
        if (!_workspaceTabs.CanClose)
        {
            SetStatus("Serve almeno un workspace aperto.");
            return;
        }
        CloseWorkspaceTabAt(_workspaceTabs.ActiveIndex);
    }

    /// <summary>Chiude il tab all’indice dato; salva il runtime solo se necessario e aggiorna selezione senza restore se il tab chiuso non era attivo.</summary>
    private void CloseWorkspaceTabAt(int index)
    {
        if (index < 0 || index >= _workspaceTabs.Count)
            return;

        if (!_workspaceTabs.CanClose)
        {
            SetStatus("Serve almeno un workspace aperto.");
            return;
        }

        var closingActive = index == _workspaceTabs.ActiveIndex;
        SaveCurrentWorkspaceState();

        if (closingActive)
            WorkspaceTabs.SelectedIndex = -1;

        if (!_workspaceTabs.TryCloseAt(index))
        {
            if (closingActive && _workspaceTabs.Count > 0)
                WorkspaceTabs.SelectedIndex = _workspaceTabs.ActiveIndex;
            return;
        }

        RefreshWorkspaceChrome();

        if (closingActive)
            SwitchToWorkspace(_workspaceTabs.ActiveIndex);
        else
        {
            _workspaceTabSwitching = true;
            try
            {
                WorkspaceTabs.SelectedIndex = _workspaceTabs.ActiveIndex;
            }
            finally
            {
                _workspaceTabSwitching = false;
            }
        }
    }

    private void OnWorkspaceTabStripCloseClick(object? sender, RoutedEventArgs e)
    {
        e.Handled = true;
        if (sender is not Button { Tag: WorkspaceTabStripItem item })
            return;
        var idx = _workspaceTabs.IndexOfStripItem(item);
        if (idx < 0)
            return;
        CloseWorkspaceTabAt(idx);
    }

    private async Task AddWorkspaceFromClipboardAsync()
    {
        var top = TopLevel.GetTopLevel(this);
        if (top?.Clipboard is null)
        {
            SetStatus("Appunti non disponibili.");
            return;
        }

        var img = await ClipboardBitmapInterop.TryReadImageAsync(top.Clipboard);
        if (img is null)
        {
            SetStatus("Appunti: nessuna immagine disponibile.");
            return;
        }

        SaveCurrentWorkspaceState();
        var title = $"Clipboard {_workspaceTabs.Count + 1}";
        var state = CreateWorkspaceStateFromImage(img, title);
        img.Dispose();
        _workspaceTabs.AddAndActivate(state);
        RefreshWorkspaceChrome();
        SwitchToWorkspace(_workspaceTabs.ActiveIndex);
    }

    private void RefreshEmptyState()
    {
        // Con atlas di benvenuto (nuovo tab / avvio) il canvas è già utilizzabile: niente overlay che suggerisca obbligo di aprire un file.
        var show = _document is null;
        EmptyStateDim.IsVisible = show;
        EmptyStatePanel.IsVisible = show;
    }

    private void ActivatePipette(int targetIndex)
    {
        CmbPipetteTarget.SelectedIndex = targetIndex;
        ChkPipette.IsChecked = true;
    }

    private void UpdatePipetteMode()
    {
        var on = ChkPipette.IsChecked == true;
        if (on)
        {
            SetManualSpriteCropMode(false);
            _toolbarSelectionModeEnabled = false;
            Editor.IsSelectionMode = false;
        }
        Editor.IsPipetteMode = on;
        PipetteHintBar.IsVisible = on;
        TxtPipetteHint.Text = on
            ? "Clicca su un pixel dell'immagine per campionare il colore. Trascina oltre 4px per annullare. Tasto centrale o Ctrl+trascina: pan."
            : "";
    }

    private void SetManualSpriteCropMode(bool on)
    {
        _manualSpriteCropMode = on;
        SpriteStudioPanel.IsManualCropEnabled = on;
        if (on)
            ChkPipette.IsChecked = false;
        ApplyEditorSelectionMode();
    }

    private void OnEditorImageSelectionCompleted(object? sender, ImageSelectionCompletedEventArgs e)
    {
        if (_document is null) return;
        if (e.Box.IsEmpty) return;

        // Memorizza sempre l'ultima ROI utente per "Crop & POT modalità ROI"
        _lastUserRoi = e.Box;

        // ── Modalità Atlas pulito: memorizza per "Copia selezione" ─────────────
        if (_pasteModeActive && _viewingPasteSource)
        {
            _pasteLastSelection = e.Box;
            SetStatus($"Selezione: {e.Box.Width}×{e.Box.Height} px. Premi 'Copia selezione' per riempire il buffer.");
            return;
        }

        // ── Modalità Selezione canvas: mostra info, non ritaglia ──────────────
        if (IsRoiSelectionModeRequested())
        {
            _activeSelectionBox = e.Box;
            Editor.SetCommittedSelection(e.Box);
            UpdateSelectionInfo();
            // Mantieni la selezione attiva per permettere nuove selezioni
            Editor.IsSelectionMode = true;
            return;
        }

        // ── Default: ritaglia il documento ───────────────────────────────────
        try
        {
            PushUndo();
            var box = e.Box;
            var cropped = AtlasCropper.Crop(_document, in box);
            if (cropped.Width < 1 || cropped.Height < 1)
            {
                cropped.Dispose();
                SetStatus("Ritaglio: nessun pixel nella selezione.");
                return;
            }
            _document.Dispose();
            _document = cropped;
            _cells.Clear();
            ClearSliceGrid();
            ClearSpriteCellList();
            SetManualSpriteCropMode(false);
            RefreshView();
            SetStatus($"Ritaglio applicato: {_document.Width}×{_document.Height} px. Annulla (Ctrl+Z) se serve.");
        }
        catch (Exception ex)
        {
            SetStatus($"Ritaglio: {ex.Message}");
        }
    }

    private void OnEditorCommittedSelectionEdited(object? sender, ImageSelectionCompletedEventArgs e)
    {
        if (e.Box.IsEmpty) return;
        _activeSelectionBox = e.Box;
        _lastUserRoi = e.Box;
        if (_pasteModeActive && _viewingPasteSource)
            _pasteLastSelection = e.Box;
        UpdateSelectionInfo();
    }

    private void OnEditorImagePixelPicked(object? sender, ImagePixelPickedEventArgs e)
    {
        if (_document is null) return;

        // Campiona il colore dominante in un'area 3×3 attorno al punto cliccato
        // (raggio 1 = 3×3 px). Più preciso di 5×5 per residui da 1-2px: un'area
        // troppo grande campiona i pixel sprite circostanti, restituendo il colore
        // sbagliato. 3×3 mantiene robustezza anti-noise pur rimanendo preciso.
        // Pixel trasparenti / semi-trasparenti (alpha < 128) vengono ignorati.
        var sampled = BackgroundIsolation.SampleRegionColor(_document, e.X, e.Y, radius: 1);
        var hx = RgbaToHex(sampled);

        switch (CmbPipetteTarget.SelectedIndex)
        {
            case 0:
                _backgroundIsolationHex = hx;
                // Salva il punto esatto del click: verrà usato come seed aggiuntivo
                // dal prossimo RunBackgroundIsolation per raggiungere pixel residui
                // interni non connessi al bordo (one-shot, azzerato dopo l'uso).
                _lastPipettePoint = (e.X, e.Y);
                // Non usare SyncSpriteCleanupStateFromControls(): sovrascriverebbe tolleranza/bordi
                // letti dal pannello con copie (_*) possibilmente non aggiornate dopo edit solo UI.
                {
                    var ui = SpriteStudioPanel.GetCleanupState();
                    SpriteStudioPanel.SetCleanupState(ui with { BackgroundHex = hx });
                    _backgroundIsolationTolerance = ui.BackgroundTolerance;
                    _backgroundIsolationEdgeThreshold = ui.BackgroundEdgeThreshold;
                }
                break;
            case 1:
                _pipelineFormState = _pipelineFormState with { OutlineHex = hx };
                {
                    var ui = SpriteStudioPanel.GetCleanupState();
                    SpriteStudioPanel.SetCleanupState(ui with { OutlineHex = hx });
                    _backgroundIsolationHex = ui.BackgroundHex;
                    _backgroundIsolationTolerance = ui.BackgroundTolerance;
                    _backgroundIsolationEdgeThreshold = ui.BackgroundEdgeThreshold;
                }
                break;
        }

        // Auto-disattiva la pipetta dopo aver campionato.
        // Impedisce ri-pick accidentale durante la navigazione canvas.
        ChkPipette.IsChecked = false;

        // Feedback: mostra colore campionato con le coordinate originali del click
        SetStatus($"Pipetta: {hx}  centro ({e.X},{e.Y})  campione 5×5 px  A={sampled.A}");
    }

    private static string RgbaToHex(Rgba32 p) => $"#{p.R:X2}{p.G:X2}{p.B:X2}";

    private void OpenSandbox()
    {
        var s = new SandboxWindow { WindowStartupLocation = WindowStartupLocation.CenterOwner };
        s.Show(this);
    }

    private void OpenAnimationPreview()
    {
        if (_document is null)
        {
            SetStatus("Apri prima un'immagine per l'anteprima animazione.");
            return;
        }
        if (_cells.Count == 0)
        {
            SetStatus("Genera prima i frame: usa 'Rileva sprite' nella toolbar o 'Dividi a griglia' nel tab Dividi.");
            return;
        }

        var win = new AnimationPreviewWindow
        {
            WindowStartupLocation = WindowStartupLocation.CenterOwner
        };
        win.LoadAnimation(_document, _cells);
        win.Show(this);
        SetStatus($"Anteprima animazione aperta: {_cells.Count} frame.");
    }

    private static void UpdatePivotLabels()
    {
        // Pivot labels gestiti in AnimationStudioView; metodo tenuto per compatibilità call-sites residui.
    }

    private void SetStatus(string message) =>
        TxtStatus.Text = string.IsNullOrEmpty(message) ? "" : $"[{DateTime.Now:HH:mm:ss}] {message}";

    private void ReportStudioActionFailure(Exception ex, string panelLabel, Enum action)
    {
        Trace.WriteLine($"[{panelLabel}] {action}: {ex}");
        SetStatus($"{panelLabel}: errore ({action}) — {ex.Message}");
    }

    private void LoadWelcomeDocument()
    {
        ClearFloatingPasteSession();
        _document?.Dispose();
        _document = CreateWelcomeAtlas();
        _backup?.Dispose();
        _backup = _document.Clone();
        _cells.Clear();
        ClearSliceGrid();
        RefreshView();
        ClearSpriteCellList();
        AnimationStudioPanel.SetGlobalScanResult("— Esegui prima la rilevazione sprite");
        _hasUserFile = false;
        _cleanApplied = false;
        RefreshEmptyState();
        ClearUndoStack();
        _activeSelectionBox = null;
        Editor.SetCommittedSelection(null);
        UpdateSelectionInfo();
        _workspaceTabs.MarkActiveClean();
        RefreshWorkspaceChrome();
        SetStatus("Pronto. Apri un'immagine per iniziare.");
    }

    private void RevertToBackup()
    {
        if (_backup is null)
        {
            LoadWelcomeDocument();
            return;
        }
        ClearFloatingPasteSession();
        _document?.Dispose();
        _document = _backup.Clone();
        _cells.Clear();
        _cleanApplied = false;
        ClearSliceGrid();
        RefreshView();
        ClearSpriteCellList();
        _workspaceTabs.MarkActiveClean();
        RefreshWorkspaceChrome();
        SetStatus("Immagine ripristinata all'originale.");
    }

    private void ClearSliceGrid()
    {
        Editor.SliceGridRows = 0;
        Editor.SliceGridCols = 0;
        Editor.SpriteCells = [];
    }

    private void InitAlignGridPanel() => PushAlignGridToEditor();

    private void PushAlignGridToEditor()
    {
        Editor.ShowAlignGrid = false;
        Editor.AlignGridCellWidth = 32;
        Editor.AlignGridCellHeight = 32;
        Editor.AlignGridOffsetX = 0;
        Editor.AlignGridOffsetY = 0;
        Editor.AlignGridSpacingX = 0;
        Editor.AlignGridSpacingY = 0;
    }

    private void RunCropToAlignGrid() =>
        SetStatus("Ritaglio griglia spostato nel Tileset Studio.");

    /// <summary>
    /// Dopo crop con offset azzerato: rigenera la lista sprite con gutter, senza SliceGridRows/Cols (la griglia arancione usa snap-8).
    /// </summary>
    private void RegenerateCellsFromAlignGridAfterCrop(int cw, int ch, int sx, int sy)
    {
        ClearSliceGrid();

        if (_document is null)
        {
            _cells.Clear();
            ClearSpriteCellList();
            return;
        }

        var cols = (_document.Width + sx) / (cw + sx);
        var rows = (_document.Height + sy) / (ch + sy);
        if (cols < 1 || rows < 1)
        {
            _cells.Clear();
            ClearSpriteCellList();
            Editor.SpriteCells = [];
            return;
        }

        _cells = GridSlicer.SliceExactWithSpacing(cols, rows, cw, ch, sx, sy).ToList();
        Editor.SpriteCells = _cells;
    }

    private static Image<Rgba32> CreateWelcomeAtlas()
    {
        var img = new Image<Rgba32>(64, 64);
        for (var y = 0; y < 64; y++)
        for (var x = 0; x < 64; x++)
            img[x, y] = new Rgba32(26, 26, 34, 255);
        for (var y = 8; y < 56; y++)
        for (var x = 8; x < 56; x++)
        {
            if ((x - 32) * (x - 32) + (y - 32) * (y - 32) < 20 * 20)
                img[x, y] = new Rgba32(200, 80, 255, 255);
        }
        return img;
    }

    private void RefreshView()
    {
        if (_document is null) return;
        Editor.SetSourceImage(_document);
        UpdateQuickWorkflowPanel();
        UpdateWorkspaceGuidance();
        UpdateExportCellMinRequiredHint();
        UpdateWorkspaceZoomSlider();
        UpdateWorkspaceScrollBars();
    }

    private void FitOpenedImageInViewport()
    {
        void ApplyFit()
        {
            if (Editor.FitImageInViewport())
            {
                UpdateWorkspaceZoomSlider();
                UpdateWorkspaceScrollBars();
            }
        }

        ApplyFit();
        Dispatcher.UIThread.Post(ApplyFit);
    }

    private void InitWorkspaceScrollBars()
    {
        SliderWorkspaceZoom.ValueChanged += OnWorkspaceZoomSliderValueChanged;
        EditorScrollH.ValueChanged += OnWorkspaceScrollHValueChanged;
        EditorScrollV.ValueChanged += OnWorkspaceScrollVValueChanged;
        Editor.PropertyChanged += OnEditorPropertyChangedWorkspaceScroll;
        Editor.LayoutUpdated += (_, _) =>
        {
            if (_workspaceScrollBarLayoutDepth > 0) return;
            UpdateWorkspaceScrollBars();
        };
        UpdateWorkspaceZoomSlider();
        UpdateWorkspaceScrollBars();
    }

    private static string FormatWorkspaceZoomPercent(double zoom)
    {
        var p = zoom * 100.0;
        if (Math.Abs(p - Math.Round(p)) < 0.051) return $"{Math.Round(p)} %";
        return $"{p:0.#} %";
    }

    /// <summary>
    /// Posizione slider normalizzata [0,1] → zoom: curva log uniforme (standard Photoshop/Figma).
    /// Ogni step di slider corrisponde alla stessa variazione percentuale di zoom; velocità coerente
    /// su tutto il range. Il 100% non è al centro: con range 5%–6400% si trova a t01≈0.418.
    /// </summary>
    private static double WorkspaceZoomFromSliderPosition(double t01)
    {
        t01 = Math.Clamp(t01, 0.0, 1.0);
        var lnMin = Math.Log(WorkspaceZoomSliderMin);
        var lnMax = Math.Log(WorkspaceZoomSliderMax);
        return Math.Exp(lnMin + t01 * (lnMax - lnMin));
    }

    /// <summary>Inversa di <see cref="WorkspaceZoomFromSliderPosition"/>.</summary>
    private static double WorkspaceSliderPositionFromZoom(double zoom)
    {
        zoom = Math.Clamp(zoom, WorkspaceZoomSliderMin, WorkspaceZoomSliderMax);
        var lnMin = Math.Log(WorkspaceZoomSliderMin);
        var lnMax = Math.Log(WorkspaceZoomSliderMax);
        return Math.Clamp((Math.Log(zoom) - lnMin) / (lnMax - lnMin), 0.0, 1.0);
    }

    private void UpdateWorkspaceZoomSlider()
    {
        if (_workspaceZoomSliderSync) return;
        _workspaceZoomSliderSync = true;
        try
        {
            var z = Math.Clamp(Editor.Zoom, WorkspaceZoomSliderMin, WorkspaceZoomSliderMax);
            var span = SliderWorkspaceZoom.Maximum - SliderWorkspaceZoom.Minimum;
            var norm = WorkspaceSliderPositionFromZoom(z);
            SliderWorkspaceZoom.Value = SliderWorkspaceZoom.Minimum + norm * span;
            TxtWorkspaceZoomPct.Text = FormatWorkspaceZoomPercent(z);
        }
        finally
        {
            _workspaceZoomSliderSync = false;
        }
    }

    /// <summary>Detent zoom standard (potenze di 2): allineati ai detent della rotella in EditorSurface.</summary>
    private static readonly double[] WorkspaceZoomDetents =
        { 0.0625, 0.125, 0.25, 0.5, 1.0, 2.0, 4.0, 8.0, 16.0, 32.0, 64.0 };

    private static double SnapZoomToDetent(double z, double tolerancePct = 0.04)
    {
        foreach (var d in WorkspaceZoomDetents)
            if (Math.Abs(z - d) / d <= tolerancePct) return d;
        return z;
    }

    private void OnWorkspaceZoomSliderValueChanged(object? sender, RangeBaseValueChangedEventArgs e)
    {
        if (_workspaceZoomSliderSync) return;
        var span = SliderWorkspaceZoom.Maximum - SliderWorkspaceZoom.Minimum;
        var t01 = span > 0
            ? (SliderWorkspaceZoom.Value - SliderWorkspaceZoom.Minimum) / span
            : 0.5;
        var zoom = SnapZoomToDetent(WorkspaceZoomFromSliderPosition(t01));
        var w = Editor.Bounds.Width;
        var h = Editor.Bounds.Height;
        var sx = w > 0 ? w * 0.5 : 0;
        var sy = h > 0 ? h * 0.5 : 0;
        Editor.SetZoomTowardScreenPoint(zoom, sx, sy);
        UpdateWorkspaceScrollBars();
    }

    private void OnEditorPropertyChangedWorkspaceScroll(object? sender, AvaloniaPropertyChangedEventArgs e)
    {
        if (_workspaceScrollBarSync) return;
        if (e.Property == EditorSurface.ZoomProperty)
            UpdateWorkspaceZoomSlider();
        if (e.Property == EditorSurface.PanXProperty || e.Property == EditorSurface.PanYProperty
            || e.Property == EditorSurface.ZoomProperty || e.Property == EditorSurface.BitmapProperty
            || e.Property == EditorSurface.IsFrameEditModeProperty
            || e.Property == EditorSurface.IsTilePreviewModeProperty)
            UpdateWorkspaceScrollBars();
    }

    private bool TryComputeWorkspacePanExtents(out double panMinX, out double panMaxX,
        out double panMinY, out double panMaxY)
    {
        panMinX = panMaxX = panMinY = panMaxY = 0;
        var vw = Editor.Bounds.Width;
        var vh = Editor.Bounds.Height;
        if (vw <= 0 || vh <= 0) return false;
        if (!Editor.TryGetPanWorldSize(out var ww, out var wh)) return false;

        var z = Math.Max(Editor.Zoom, 0.0001);
        var iw = ww * z;
        var ih = wh * z;
        panMinX = Math.Min(0, vw - iw);
        panMaxX = Math.Max(0, vw - iw);
        panMinY = Math.Min(0, vh - ih);
        panMaxY = Math.Max(0, vh - ih);
        return true;
    }

    private void UpdateWorkspaceScrollBars()
    {
        if (_workspaceScrollBarLayoutDepth > 0) return;
        _workspaceScrollBarLayoutDepth++;

        try
        {
            if (!TryComputeWorkspacePanExtents(out var panMinX, out var panMaxX, out var panMinY, out var panMaxY))
            {
                EditorScrollH.IsVisible = false;
                EditorScrollV.IsVisible = false;
                return;
            }

            var spanX = panMaxX - panMinX;
            var spanY = panMaxY - panMinY;
            const double eps = 0.5;

            _workspaceScrollBarSync = true;
            try
            {
                EditorScrollH.Minimum = 0;
                EditorScrollH.Maximum = Math.Max(0, spanX);
                EditorScrollH.Value = Math.Clamp(Editor.PanX - panMinX, EditorScrollH.Minimum, EditorScrollH.Maximum);
                EditorScrollH.SmallChange = Math.Max(1, spanX / 24);
                EditorScrollH.LargeChange = Math.Max(EditorScrollH.SmallChange, Editor.Bounds.Width * 0.85);
                EditorScrollH.IsVisible = spanX > eps;

                EditorScrollV.Minimum = 0;
                EditorScrollV.Maximum = Math.Max(0, spanY);
                EditorScrollV.Value = Math.Clamp(Editor.PanY - panMinY, EditorScrollV.Minimum, EditorScrollV.Maximum);
                EditorScrollV.SmallChange = Math.Max(1, spanY / 24);
                EditorScrollV.LargeChange = Math.Max(EditorScrollV.SmallChange, Editor.Bounds.Height * 0.85);
                EditorScrollV.IsVisible = spanY > eps;
            }
            finally
            {
                _workspaceScrollBarSync = false;
            }
        }
        finally
        {
            _workspaceScrollBarLayoutDepth--;
        }
    }

    private void OnWorkspaceScrollHValueChanged(object? sender, RangeBaseValueChangedEventArgs e)
    {
        if (_workspaceScrollBarSync) return;
        if (!TryComputeWorkspacePanExtents(out var panMinX, out var panMaxX, out _, out _)) return;
        _workspaceScrollBarSync = true;
        try
        {
            Editor.PanX = Math.Clamp(EditorScrollH.Value + panMinX, panMinX, panMaxX);
        }
        finally
        {
            _workspaceScrollBarSync = false;
        }
    }

    private void OnWorkspaceScrollVValueChanged(object? sender, RangeBaseValueChangedEventArgs e)
    {
        if (_workspaceScrollBarSync) return;
        if (!TryComputeWorkspacePanExtents(out _, out _, out var panMinY, out var panMaxY)) return;
        _workspaceScrollBarSync = true;
        try
        {
            Editor.PanY = Math.Clamp(EditorScrollV.Value + panMinY, panMinY, panMaxY);
        }
        finally
        {
            _workspaceScrollBarSync = false;
        }
    }

    private void UpdateExportCellMinRequiredHint()
    {
        if (_document is null)
        {
            TxtExportCellMinRequired.Text = "Min richiesto: -";
            return;
        }

        var reqW = _cells.Count > 0 ? _cells.Max(c => Math.Max(1, c.BoundsInAtlas.Width)) : _document.Width;
        var reqH = _cells.Count > 0 ? _cells.Max(c => Math.Max(1, c.BoundsInAtlas.Height)) : _document.Height;
        var text = $"Min richiesto: {reqW}×{reqH}";

        if (ChkExportCustomCellSize.IsChecked == true)
        {
            var cw = Math.Max(1, InputParsing.ParseInt(TxtExportCellW.Text, reqW));
            var ch = Math.Max(1, InputParsing.ParseInt(TxtExportCellH.Text, reqH));
            if (cw < reqW || ch < reqH)
                text += " (custom troppo piccola)";
        }

        TxtExportCellMinRequired.Text = text;
    }

    private void UpdateQuickWorkflowPanel()
    {
        if (_document is null)
        {
            _quickColorsAfterText = "-";
            return;
        }

        var count = PixelArtValidation.CountUniqueColors(_document);
        if (string.IsNullOrWhiteSpace(_quickColorsAfterText) || _quickColorsAfterText == "-")
            _quickColorsAfterText = count.ToString(CultureInfo.InvariantCulture);
    }

    private static string ToHexRgb(Rgba32 c) => $"#{c.R:X2}{c.G:X2}{c.B:X2}";

    private void RunQuickProcess()
    {
        if (!TryBuildPipelineOptions(includeOutline: false, out var options, out var error))
        {
            SetStatus(error);
            return;
        }

        ExecutePipeline(options, "Workflow base");
    }

    private void RunCenterCanvas()
    {
        if (_document is null)
        {
            SetStatus("Nessuna immagine aperta.");
            return;
        }

        if (Editor.CenterImageInViewport())
            SetStatus("Canvas centrato.");
        else
            SetStatus("Impossibile centrare ora: viewport non pronta.");
    }

    private void ApplyDefaultPresetToControls()
    {
        _isApplyingPipelinePreset = true;
        try
        {
            _pipelineVm.ApplyDefaultPreset();
            ApplyPipelineFormStateToControls(_pipelineVm.ToFormState());
        }
        finally
        {
            _isApplyingPipelinePreset = false;
        }
        SetStatus("Preset Default impostato.");
        UpdatePipelinePresetBadge();
    }

    private void ApplySafePresetToControls()
    {
        _isApplyingPipelinePreset = true;
        try
        {
            _pipelineVm.ApplySafePreset();
            ApplyPipelineFormStateToControls(_pipelineVm.ToFormState());
        }
        finally
        {
            _isApplyingPipelinePreset = false;
        }
        SetStatus("Preset Sicuro impostato.");
        UpdatePipelinePresetBadge();
    }

    private void ApplyAggressivePresetToControls()
    {
        _isApplyingPipelinePreset = true;
        try
        {
            _pipelineVm.ApplyAggressivePreset();
            ApplyPipelineFormStateToControls(_pipelineVm.ToFormState());
        }
        finally
        {
            _isApplyingPipelinePreset = false;
        }
        SetStatus("Preset Aggressivo impostato.");
        UpdatePipelinePresetBadge();
    }

    private void RefreshCellList()
    {
        var rows = _cells.Select(c =>
            $"{c.Id}  [{c.BoundsInAtlas.MinX},{c.BoundsInAtlas.MinY} → {c.BoundsInAtlas.Width}×{c.BoundsInAtlas.Height}]").ToList();
        SpriteStudioPanel.SetCells(rows);
    }

    private void ClearSpriteCellList()
    {
        _selectedSpriteCellIndex = -1;
        SpriteStudioPanel.SetCells([]);
    }

    private async Task SaveSelectedFrameAsync(int selectedCellIndex)
    {
        await SlicingController.SaveSelectedFrameAsync(_document, _cells, selectedCellIndex, StorageProvider, SetStatus);
    }
    private bool TryBuildPipelineOptions(bool includeOutline, out PixelArtPipeline.Options options, out string error)
    {
        options = default!;

        if (_document is null)
        {
            error = "Nessuna immagine aperta.";
            return false;
        }

        var formState = ReadPipelineFormStateFromControls();
        return _pipelineVm.TryBuildOptionsFromFormState(formState, includeOutline, out options, out error);
    }

    private void ExecutePipeline(PixelArtPipeline.Options options, string label)
    {
        if (_document is null) return;
        PushUndo();
        var result = PipelineExecutionService.RunInPlace(_document, options, label, ToHexRgb);
        if (!result.Succeeded)
        {
            SetStatus(result.StatusText);
            return;
        }

        _quickColorsAfterText = result.ColorsAfterText;
        _cleanApplied = true;
        ClearSliceGrid();
        _cells.Clear();
        ClearSpriteCellList();
        RefreshView();
        SetStatus(result.StatusText);
    }

    private PipelineViewModel.PipelineFormState ReadPipelineFormStateFromControls()
    {
        return _pipelineFormState;
    }

    private void ApplyPipelineFormStateToControls(PipelineViewModel.PipelineFormState formState)
    {
        _pipelineFormState = formState;
        SyncSpriteCleanupStateFromControls();
    }

    private void HookPipelinePresetResetOnManualChanges()
    {
        // Quando l'utente modifica manualmente i parametri del pannello Sprite Studio,
        // resetta la badge a "Personalizzato" (nascosta).
        // SpriteStudioView non espone eventi di modifica individuali per ogni campo,
        // quindi utilizziamo il TextChanged/CheckedChanged intercettando l'azione di sync.
        // Il reset avviene a ogni ApplySpriteCleanupStateToControls() → SyncSpriteCleanupStateFromControls()
        // è già sufficiente: la badge viene aggiornata tramite UpdatePipelinePresetBadge dopo ogni preset,
        // e si resetta automaticamente quando l'utente applica un filtro individuale senza preset.
    }

    private void ResetPresetOnManualPipelineEdit()
    {
        if (_isApplyingPipelinePreset) return;
        _pipelineVm.ActivePreset = PipelineViewModel.PresetKind.None;
        UpdatePipelinePresetBadge();
    }

    private void UpdatePipelinePresetBadge()
    {
        var label = _pipelineVm.ActivePreset switch
        {
            PipelineViewModel.PresetKind.Default    => "Default",
            PipelineViewModel.PresetKind.Safe       => "Sicuro",
            PipelineViewModel.PresetKind.Aggressive => "Aggressivo",
            _                                       => null   // None → nasconde la badge
        };
        SpriteStudioPanel.SetPresetBadge(label);
    }

    private void RunPixelPipeline()
    {
        if (!TryBuildPipelineOptions(includeOutline: true, out var options, out var error))
        {
            SetStatus(error);
            return;
        }

        ExecutePipeline(options, "Pipeline");
    }

    // ─── Helpers generici per le trasformazioni documento ────────────────────

    /// <summary>
    /// Esegue una trasformazione in-place su <c>_document</c>:
    /// null-check → PushUndo → transform → [ClearSliceGrid] → RefreshView → SetStatus.
    /// </summary>
    private void RunTransform(Action<Image<Rgba32>> transform, string successMsg,
                               bool clearCells = true, string errorPrefix = "Errore")
    {
        if (_document is null) { SetStatus("Nessuna immagine aperta."); return; }
        try
        {
            PushUndo();
            transform(_document);
            if (clearCells) { ClearSliceGrid(); _cells.Clear(); ClearSpriteCellList(); }
            RefreshView();
            SetStatus(successMsg);
        }
        catch (Exception ex) { SetStatus($"{errorPrefix}: {ex.Message}"); }
    }

    /// <summary>
    /// Esegue una trasformazione che crea una nuova immagine (dispone la vecchia):
    /// null-check → PushUndo → next = transform → Dispose old → ClearSliceGrid → RefreshView → SetStatus.
    /// </summary>
    private void RunReplaceTransform(Func<Image<Rgba32>, Image<Rgba32>> transform, string successMsg,
                                      string errorPrefix = "Errore")
    {
        if (_document is null) { SetStatus("Nessuna immagine aperta."); return; }
        try
        {
            PushUndo();
            var next = transform(_document);
            _document.Dispose();
            _document = next;
            ClearSliceGrid();
            _cells.Clear();
            ClearSpriteCellList();
            RefreshView();
            SetStatus(successMsg);
        }
        catch (Exception ex) { SetStatus($"{errorPrefix}: {ex.Message}"); }
    }

    // ─────────────────────────────────────────────────────────────────────────

    private void RunNearestResize()
    {
        var tw = Math.Max(1, InputParsing.ParseInt(SpriteStudioPanel.ResizeWidthText, 64));
        var th = Math.Max(1, InputParsing.ParseInt(SpriteStudioPanel.ResizeHeightText, 64));
        RunReplaceTransform(
            src => NearestNeighborResize.Resize(src, tw, th, 0, 0),
            $"Immagine ridimensionata a {tw}×{th} px.",
            "Errore ridimensionamento");
    }

    private void RunBackgroundIsolation()
    {
        if (!InputParsing.TryParseHexRgb(_backgroundIsolationHex, out var key))
        {
            SetStatus("Rimuovi sfondo: colore sfondo non valido. Usa ◉ per campionare dall'immagine.");
            return;
        }
        var tol  = Math.Max(0, InputParsing.ParseInt(_backgroundIsolationTolerance, 10));
        var edge = Math.Max(0, InputParsing.ParseInt(_backgroundIsolationEdgeThreshold, 100));
        if (_document is null) { SetStatus("Nessuna immagine aperta."); return; }
        try
        {
            // Auto-snap: corregge il colore chiave al pixel di bordo più vicino (3px profondità).
            // Gestisce il caso in cui Quantize abbia rimappato i colori dopo la pipetta.
            var snappedKey = BackgroundIsolation.SnapKeyToBorderColor(_document, key);
            var snapped = snappedKey != key;
            if (snapped)
            {
                _backgroundIsolationHex = RgbaToHex(snappedKey);
                var ui = SpriteStudioPanel.GetCleanupState();
                SpriteStudioPanel.SetCleanupState(ui with { BackgroundHex = _backgroundIsolationHex });
                _backgroundIsolationTolerance = ui.BackgroundTolerance;
                _backgroundIsolationEdgeThreshold = ui.BackgroundEdgeThreshold;
            }

            // Seed extra dall'ultima pick pipetta (one-shot): permette al flood di
            // raggiungere pixel residui interni non connessi al bordo dell'immagine.
            var extraSeed = _lastPipettePoint;
            _lastPipettePoint = null;
            var additionalSeeds = extraSeed.HasValue
                ? (IReadOnlyList<(int X, int Y)>)[extraSeed.Value]
                : null;

            PushUndo();
            var removed = BackgroundIsolation.ApplyInPlace(
                _document,
                new BackgroundIsolation.Options(
                    BackgroundColor:    snappedKey,
                    ColorTolerance:     tol,
                    EdgeThreshold:      edge,
                    UseOklab:           true,
                    ProtectStrongEdges: true,
                    Use8Connectivity:   true,
                    AdditionalSeeds:    additionalSeeds));

            // ── Alpha binarization post-flood (opzionale) ──────────────────────
            // Se "Binarizza alpha" è attiva nel pannello, azzera i pixel semi-trasparenti
            // residui (fringe da operazioni precedenti o immagini con alpha channel misto).
            var alphaApplied = false;
            if (_pipelineFormState.EnableAlphaThreshold && removed > 0)
            {
                var alphaThr = (byte)Math.Clamp(InputParsing.ParseInt(_pipelineFormState.AlphaThreshold, 128), 1, 254);
                AlphaThreshold.ApplyInPlace(_document, alphaThr);
                alphaApplied = true;
            }

            ClearSliceGrid(); _cells.Clear(); ClearSpriteCellList();
            RefreshView();

            var snapNote  = snapped          ? $" [colore auto-corretto da {RgbaToHex(key)}]"  : string.Empty;
            var alphaNote = alphaApplied     ? " + alpha binarizzata"                          : string.Empty;
            var seedNote  = extraSeed.HasValue ? $" [seed ◉ {extraSeed.Value.X},{extraSeed.Value.Y}]" : string.Empty;

            if (removed > 0)
            {
                SetStatus($"Sfondo rimosso: {removed:N0} px — colore {RgbaToHex(snappedKey)}, tol {tol}, bordi {edge}{snapNote}{alphaNote}{seedNote}.");
                _cleanApplied = true;
                ResetPresetOnManualPipelineEdit();
                UpdateWorkspaceGuidance();
            }
            else
            {
                // Diagnostica dettagliata: aiuta l'utente a capire perché il flood ha fallito.
                var hints = new System.Text.StringBuilder();
                hints.Append($"Nessun pixel rimosso — colore {RgbaToHex(snappedKey)}, tol {tol}, bordi {edge}{snapNote}. ");
                if (tol < 5)
                    hints.Append("Aumenta tolleranza (prova 15-30). ");
                if (edge > 80)
                    hints.Append("Riduci protezione bordi (prova 50-0) se il flood è bloccato su sfondo uniforme. ");
                if (extraSeed.HasValue)
                    hints.Append($"Il punto ◉ ({extraSeed.Value.X},{extraSeed.Value.Y}) è stato usato come seed ma nessun pixel corrispondente trovato lì — ricontrolla colore/tolleranza. ");
                else
                    hints.Append("Se i residui non sono connessi al bordo: usa ◉ direttamente SUL pixel residuo, poi 'Rimuovi sfondo' seminerà anche da quel punto. ");
                SetStatus(hints.ToString().TrimEnd());
            }
        }
        catch (Exception ex) { SetStatus($"Errore rimozione sfondo: {ex.Message}"); }
    }

    private void RunGlobalChromaKey()
    {
        if (!InputParsing.TryParseHexRgb(_backgroundIsolationHex, out var key))
        {
            SetStatus("Global Chroma-Key: colore sfondo non valido. Usa ◉ per campionare dall'immagine.");
            return;
        }
        var tol = Math.Max(0, InputParsing.ParseInt(_backgroundIsolationTolerance, 10));
        if (_document is null) { SetStatus("Nessuna immagine aperta."); return; }
        try
        {
            PushUndo();
            var removed = GlobalChromaKey.ApplyInPlace(_document, key, tol);

            // ── Alpha binarization post-rimozione (opzionale, stessa logica del flood) ──
            var alphaApplied = false;
            if (_pipelineFormState.EnableAlphaThreshold && removed > 0)
            {
                var alphaThr = (byte)Math.Clamp(InputParsing.ParseInt(_pipelineFormState.AlphaThreshold, 128), 1, 254);
                AlphaThreshold.ApplyInPlace(_document, alphaThr);
                alphaApplied = true;
            }

            ClearSliceGrid(); _cells.Clear(); ClearSpriteCellList();
            RefreshView();

            var alphaNote = alphaApplied ? " + alpha binarizzata" : string.Empty;

            if (removed > 0)
            {
                SetStatus($"Global Chroma-Key: {removed:N0} px rimossi — colore {RgbaToHex(key)}, tol {tol}{alphaNote}.");
                _cleanApplied = true;
                ResetPresetOnManualPipelineEdit();
                UpdateWorkspaceGuidance();
            }
            else
            {
                var sb = new System.Text.StringBuilder();
                sb.Append($"Global Chroma-Key: nessun pixel rimosso — colore {RgbaToHex(key)}, tol {tol}. ");
                if (tol < 5)
                    sb.Append("Prova ad aumentare la tolleranza (5-15). ");
                sb.Append("Usa ◉ per campionare il colore esatto dal pixel da rimuovere.");
                SetStatus(sb.ToString().TrimEnd());
            }
        }
        catch (Exception ex) { SetStatus($"Errore Global Chroma-Key: {ex.Message}"); }
    }

    // ─────────────────────────────────────────────────────────────────────────
    // Morfologia + Anomaly Detection
    // ─────────────────────────────────────────────────────────────────────────

    private enum MorphOp { Erode, Dilate, Open, Close }

    private void RunMorphology(MorphOp op)
    {
        if (_document is null) { SetStatus("Nessuna immagine aperta."); return; }
        var iter = Math.Clamp(InputParsing.ParseInt(SpriteStudioPanel.MorphIterationsText, 1), 1, 10);
        try
        {
            PushUndo();
            string msg;
            switch (op)
            {
                case MorphOp.Erode:
                {
                    var removed = Morphology.Erode(_document, iter);
                    msg = $"Erode ({iter}×): {removed:N0} px rimossi dal bordo opaco.";
                    break;
                }
                case MorphOp.Dilate:
                {
                    var added = Morphology.Dilate(_document, iter);
                    msg = $"Dilate ({iter}×): {added:N0} px aggiunti (edge padding).";
                    break;
                }
                case MorphOp.Open:
                {
                    var (eroded, dilated) = Morphology.Open(_document, iter);
                    msg = $"Open ({iter}×): {eroded:N0} px erosi, {dilated:N0} px dilatati — protrusioni rimosse.";
                    break;
                }
                default: // Close
                {
                    var (dilated, eroded) = Morphology.Close(_document, iter);
                    msg = $"Close ({iter}×): {dilated:N0} px dilatati, {eroded:N0} px erosi — buchi chiusi.";
                    break;
                }
            }
            RefreshView();
            _cleanApplied = true;
            SetStatus(msg);
        }
        catch (Exception ex) { SetStatus($"Errore morfologia: {ex.Message}"); }
    }

    private void RunRemoveIsolatedIslands()
    {
        if (_document is null) { SetStatus("Nessuna immagine aperta."); return; }
        var minSize = Math.Max(1, InputParsing.ParseInt(SpriteStudioPanel.AnomalyMinIslandText, 4));
        try
        {
            PushUndo();
            var removed = AnomalyDetector.RemoveIsolatedIslands(_document, minSize);
            RefreshView();
            if (removed > 0)
            {
                _cleanApplied = true;
                SetStatus($"Isole isolate: {removed:N0} px rimossi (cluster < {minSize} px).");
            }
            else
            {
                SetStatus($"Nessuna isola isolata trovata (soglia {minSize} px). Prova ad abbassare il valore.");
            }
        }
        catch (Exception ex) { SetStatus($"Errore rimozione isole: {ex.Message}"); }
    }

    private void RunRemoveColorOutliers()
    {
        if (_document is null) { SetStatus("Nessuna immagine aperta."); return; }
        var tol = Math.Max(1.0, InputParsing.ParseInt(SpriteStudioPanel.AnomalyOutlierTolText, 20));
        try
        {
            PushUndo();
            var removed = AnomalyDetector.RemoveColorOutliers(_document, tol);
            RefreshView();
            if (removed > 0)
            {
                _cleanApplied = true;
                SetStatus($"Outlier colore: {removed:N0} px rimossi (tolleranza Oklab ≈{tol:F0} RGB).");
            }
            else
            {
                SetStatus($"Nessun outlier trovato (tolleranza {tol:F0}). Prova ad alzare la soglia.");
            }
        }
        catch (Exception ex) { SetStatus($"Errore outlier detection: {ex.Message}"); }
    }

    private async Task ExportTiledMapJsonAsync()
    {
        await ExportController.ExportTiledMapJsonAsync(_document, _cells, StorageProvider, SetStatus);
    }

    private async Task ExportFramesZipAsync()
    {
        await ExportController.ExportFramesZipAsync(_document, _cells, StorageProvider, SetStatus);
    }
    /// <summary>
    /// Chiude modalità transiente (atlas pulito, workbench frame, ROI crop, strumenti canvas)
    /// dopo aver sostituito il documento e svuotato <see cref="_cells"/>.
    /// Usare solo per un «nuovo progetto» (es. Apri immagine), non dopo import frame nello stesso progetto.
    /// </summary>
    private void ResetTransientStateForNewOpenedProject()
    {
        _pasteSource?.Dispose();
        _pasteSource = null;
        _pasteBuffer?.Dispose();
        _pasteBuffer = null;
        _pasteLastSelection = null;
        _pasteModeActive = false;
        _viewingPasteSource = false;
        Editor.IsCellClickMode = false;
        UpdatePasteBufferStatus();

        ExitFrameAlignMode();

        _lastUserRoi = null;
        _frameDragInitialOffset = null;

        _toolbarSelectionModeEnabled = false;
        ChkPipette.IsChecked = false;
        ChkEraser.IsChecked = false;
        SetManualSpriteCropMode(false);
        Editor.IsPipetteMode = false;
        PipetteHintBar.IsVisible = false;
        Editor.IsEraserMode = false;
        ClearFloatingPasteSession();
    }

    private void ClearFloatingPasteSession() => _floatingPaste.ClearSession();

    private void OnEditorFloatingOverlayMoved(object? sender, FloatingOverlayMoveEventArgs e) =>
        _floatingPaste.OnOverlayMoved(e);

    private async Task TryPasteFromClipboardAsync()
    {
        await _floatingPaste.TryBeginAsync(
            _document,
            TopLevel.GetTopLevel(this)?.Clipboard,
            () => Editor.IsFrameEditMode || _pasteModeActive || Editor.IsTilePreviewMode,
            (iw, ih, cw, ch) => PasteOversizeDialog.ShowAsync(this, iw, ih, cw, ch),
            msg => SetStatus(msg));
    }

    private async Task TryCopyImageToClipboardAsync()
    {
        await SelectionController.CopyImageToClipboardAsync(
            _document,
            _activeSelectionBox,
            TopLevel.GetTopLevel(this)?.Clipboard,
            Rgba32BitmapBridge.ToBitmap,
            ClipboardBitmapInterop.SetBitmapAndFlushAsync,
            SetStatus);
    }
    private void CommitFloatingPaste() =>
        _floatingPaste.TryCommit(_document, PushUndo, msg => SetStatus(msg), RefreshView);

    private void CancelFloatingPaste() => _floatingPaste.Cancel(msg => SetStatus(msg));

    private async Task OpenImageAsync()
    {
        var file = await StorageProvider.OpenFilePickerAsync(new FilePickerOpenOptions
        {
            Title = "Apri immagine (atlas / sprite sheet)",
            AllowMultiple = false,
            FileTypeFilter =
            [
                new FilePickerFileType("Immagini")
                {
                    Patterns = ["*.png", "*.webp", "*.jpg", "*.jpeg", "*.bmp", "*.gif"]
                }
            ]
        });

        if (file is not { Count: > 0 })
            return;

        try
        {
            await using var stream = await file[0].OpenReadAsync();
            var loaded = await SixLabors.ImageSharp.Image.LoadAsync<Rgba32>(stream);

            if (IsActiveWorkspaceOccupied())
            {
                SaveCurrentWorkspaceState();
                var tabTitle = WorkspaceTitleFromStorageFile(file[0]);
                var state = CreateWorkspaceStateFromImage(loaded, tabTitle);
                loaded.Dispose();
                _workspaceTabs.AddAndActivate(state);
                SwitchToWorkspace(_workspaceTabs.ActiveIndex);
                ResetTransientStateForNewOpenedProject();
                MainTabs.SelectedIndex = 0;
                EnterSelectionCanvas(IsRoiSelectionModeRequested());
                UpdateSelectionInfo();
                RefreshView();
                FitOpenedImageInViewport();
                _workspaceTabs.MarkActiveClean();
                RefreshWorkspaceChrome();
                SetStatus($"Immagine aperta in nuovo workspace ({tabTitle}): {_document!.Width}×{_document.Height} px.");
                return;
            }

            _document?.Dispose();
            _document = loaded;
            _backup?.Dispose();
            _backup = _document.Clone();
            _cells.Clear();
            ClearSliceGrid();
            ResetTransientStateForNewOpenedProject();
            ClearSpriteCellList();
            // TxtAabb rimosso
            AnimationStudioPanel.SetGlobalScanResult("— Esegui prima la rilevazione sprite");
            _hasUserFile = true;
            _cleanApplied = false;
            RefreshEmptyState();
            ClearUndoStack();
            _activeSelectionBox = null;
            Editor.SetCommittedSelection(null);
            MainTabs.SelectedIndex = 0;
            EnterSelectionCanvas(IsRoiSelectionModeRequested());
            UpdateSelectionInfo();
            RefreshView();
            FitOpenedImageInViewport();
            _workspaceTabs.MarkActiveClean();
            RefreshWorkspaceChrome();
            SetStatus($"Immagine aperta: {_document.Width}×{_document.Height} px. Scheda «Immagine» o «Sprite».");
        }
        catch (Exception ex)
        {
            SetStatus($"Errore apertura immagine: {ex.Message}");
        }
    }

    private async Task ImportFramesFromVideoAsync()
    {
        // 1. Individua FFmpeg
        if (!AiPixelScaler.Desktop.Services.FFmpegLocator.TryLocate(_uiPrefs, out var ffmpegPath, out var ffprobePath))
        {
            var folder = await FfmpegSetupDialog.ShowAsync(this, _uiPrefs.LoadFfmpegFolder());
            if (folder is null) return;
            _uiPrefs.SaveFfmpegFolder(folder);
            if (!AiPixelScaler.Desktop.Services.FFmpegLocator.TryLocate(_uiPrefs, out ffmpegPath, out ffprobePath))
            {
                SetStatus("FFmpeg non trovato anche dopo la configurazione. Verifica la cartella.");
                return;
            }
        }

        // 2. Apri il dialogo di import
        var result = await VideoImportDialog.ShowAsync(this, ffmpegPath, ffprobePath);
        if (result is null) return;

        // 3. Crea cartella temporanea
        var tempDir = System.IO.Path.Combine(
            System.IO.Path.GetTempPath(),
            "AiPixelScaler_Vid_" + Guid.NewGuid().ToString("N")[..8]);
        System.IO.Directory.CreateDirectory(tempDir);

        SetStatus("Estrazione frame in corso…");
        try
        {
            // 4. Estrai frame
            var opts = new AiPixelScaler.Desktop.Services.ExtractOptions(
                result.FilePath,
                result.StartSec,
                result.EndSec,
                result.UseFpsTarget,
                result.FpsOrEveryN);

            var n = await AiPixelScaler.Desktop.Services.VideoFrameExtractor.ExtractFramesAsync(
                ffmpegPath, opts, tempDir);

            if (n == 0)
            {
                SetStatus("Nessun frame estratto. Verifica il range e i parametri.");
                return;
            }

            // 5. Carica PNG e costruisce atlas
            var files = System.IO.Directory.GetFiles(tempDir, "*.png");
            System.Array.Sort(files, System.StringComparer.OrdinalIgnoreCase);

            int cellW, cellH;
            using (var first = await SixLabors.ImageSharp.Image.LoadAsync<Rgba32>(files[0]))
            {
                cellW = first.Width;
                cellH = first.Height;
            }

            var cols  = (int)Math.Ceiling(Math.Sqrt(n));
            var rows  = (int)Math.Ceiling((double)n / cols);
            var atlas = new Image<Rgba32>(cols * cellW, rows * cellH, new Rgba32(0, 0, 0, 0));
            var cells = new List<SpriteCell>(n);

            for (var i = 0; i < files.Length; i++)
            {
                var col   = i % cols;
                var row   = i / cols;
                var cellX = col * cellW;
                var cellY = row * cellH;

                using var frame = await SixLabors.ImageSharp.Image.LoadAsync<Rgba32>(files[i]);
                atlas.Mutate(ctx => ctx.DrawImage(
                    frame,
                    new SixLabors.ImageSharp.Point(cellX, cellY),
                    1f));
                cells.Add(new SpriteCell(
                    $"frame_{i:000}",
                    new AiPixelScaler.Core.Geometry.AxisAlignedBox(cellX, cellY, cellX + cellW, cellY + cellH)));
            }

            // 6. Imposta come documento corrente
            PushUndo();
            _document?.Dispose();
            _document = atlas;
            _backup?.Dispose();
            _backup = atlas.Clone();
            _cells  = cells;
            ClearSliceGrid();
            Editor.SpriteCells = _cells;
            RefreshCellList();
            RefreshView();
            FitOpenedImageInViewport();
            RefreshEmptyState();
            _workspaceTabs.MarkActiveClean();
            RefreshWorkspaceChrome();
            SetStatus($"Importati {n} frame da video — atlas {atlas.Width}×{atlas.Height} px " +
                      $"({cellW}×{cellH} px/cella). Usa Animation Studio per la preview.");
        }
        catch (OperationCanceledException)
        {
            SetStatus("Estrazione video annullata.");
        }
        catch (Exception ex)
        {
            SetStatus($"Errore estrazione video: {ex.Message}");
        }
        finally
        {
            try { System.IO.Directory.Delete(tempDir, recursive: true); } catch { }
        }
    }

    private async Task CreateBlankCanvasAsync()
    {
        var result = await NewCanvasDialog.ShowAsync(this);
        if (result is null)
            return;

        // Costruisce il colore di sfondo (trasparente se BackgroundHex è null)
        Rgba32 bgColor;
        if (result.BackgroundHex is not null &&
            SixLabors.ImageSharp.Color.TryParseHex(result.BackgroundHex, out var parsed))
        {
            bgColor = parsed.ToPixel<Rgba32>();
            bgColor.A = 255;
        }
        else
        {
            bgColor = new Rgba32(0, 0, 0, 0); // trasparente
        }

        var blank = new Image<Rgba32>(result.Width, result.Height, bgColor);
        var title = $"Nuovo {result.Width}×{result.Height}";

        if (IsActiveWorkspaceOccupied())
        {
            SaveCurrentWorkspaceState();
            var state = CreateWorkspaceStateFromImage(blank, title);
            blank.Dispose();
            _workspaceTabs.AddAndActivate(state);
            SwitchToWorkspace(_workspaceTabs.ActiveIndex);
            ResetTransientStateForNewOpenedProject();
            MainTabs.SelectedIndex = 0;
            EnterSelectionCanvas(IsRoiSelectionModeRequested());
            UpdateSelectionInfo();
            RefreshView();
            FitOpenedImageInViewport();
            _workspaceTabs.MarkActiveClean();
            RefreshWorkspaceChrome();
            SetStatus($"Canvas vuoto creato in nuovo workspace ({title}): {result.Width}×{result.Height} px.");
            return;
        }

        _document?.Dispose();
        _document = blank;
        _backup?.Dispose();
        _backup = _document.Clone();
        _cells.Clear();
        ClearSliceGrid();
        ResetTransientStateForNewOpenedProject();
        ClearSpriteCellList();
        AnimationStudioPanel.SetGlobalScanResult("— Esegui prima la rilevazione sprite");
        _hasUserFile = false;
        _cleanApplied = false;
        RefreshEmptyState();
        ClearUndoStack();
        _activeSelectionBox = null;
        Editor.SetCommittedSelection(null);
        MainTabs.SelectedIndex = 0;
        EnterSelectionCanvas(IsRoiSelectionModeRequested());
        UpdateSelectionInfo();
        RefreshView();
        FitOpenedImageInViewport();
        _workspaceTabs.MarkActiveClean();
        RefreshWorkspaceChrome();
        SetStatus($"Canvas vuoto creato: {result.Width}×{result.Height} px. Importa sprite con «Apri» o l'atlas.");
    }

    private void RunIslandCleanup()
    {
        var minA = Math.Max(1, InputParsing.ParseInt(_pipelineFormState.MinIsland, 2));
        RunTransform(
            img => IslandDenoise.ApplyInPlace(img, new IslandDenoise.Options(1, minA)),
            $"Island cleanup applicato (blob < {minA} px rimossi).",
            clearCells: true,
            "Errore island cleanup");
        _cleanApplied = true;
        UpdateWorkspaceGuidance();
    }

    private void RunMajorityDenoise()
    {
        var minNeighbors = Math.Max(1, InputParsing.ParseInt(SpriteStudioPanel.MajorityMinNeighborsText, 1));
        RunTransform(
            img => MajorityNeighborDenoise.ApplyInPlace(img, minNeighbors),
            $"Majority denoise applicato (vicini min: {minNeighbors}).",
            clearCells: true,
            "Errore majority denoise");
        _cleanApplied = true;
        UpdateWorkspaceGuidance();
    }

    private void RunGridSlice()
    {
        var cells = SlicingController.RunGridSlice(
            _document,
            SpriteStudioPanel.GridRowsText,
            SpriteStudioPanel.GridColsText,
            () => PushUndo(),
            (rows, cols) =>
            {
                Editor.SliceGridRows = rows;
                Editor.SliceGridCols = cols;
            },
            cells => Editor.SpriteCells = cells.ToList(),
            SetStatus);
        if (cells is not null)
        {
            _cells = cells;
            RefreshCellList();
            UpdateWorkspaceGuidance();
        }
    }
    private async Task RunWorkspaceGuideActionAsync()
    {
        switch (_workflowShell.ActiveStep)
        {
            case WorkflowShellViewModel.WorkflowStep.Importa:
                await OpenImageAsync();
                break;
            case WorkflowShellViewModel.WorkflowStep.Pulisci:
                _runQuickProcessCommand.Execute(null);
                break;
            case WorkflowShellViewModel.WorkflowStep.SliceAllinea:
                RunCcl();
                break;
            case WorkflowShellViewModel.WorkflowStep.Esporta:
                await ExportPngAsync();
                break;
        }
    }

    private async Task AdvanceWorkflowStepAsync()
    {
        _workflowShell.AdvanceWorkflowStepCommand.Execute(null);
        SelectWorkflowStep(_workflowShell.ActiveStep);
        await RunWorkspaceGuideActionAsync();
    }

    private void ExecuteSelectStepCommand(WorkflowShellViewModel.WorkflowStep step)
    {
        _workflowShell.SelectWorkflowStepCommand.Execute(step);
        SelectWorkflowStep(step);
    }

    private void SelectWorkflowStep(WorkflowShellViewModel.WorkflowStep step)
    {
        MainTabs.SelectedIndex = step switch
        {
            WorkflowShellViewModel.WorkflowStep.Importa => 0,
            WorkflowShellViewModel.WorkflowStep.Pulisci => 0,
            WorkflowShellViewModel.WorkflowStep.SliceAllinea => 0,
            WorkflowShellViewModel.WorkflowStep.Esporta => 1,
            _ => 0
        };
        UpdateWorkflowShell();
    }

    private void UpdateWorkspaceGuidance()
    {
        if (_document is null)
        {
            _workflowShell.UpdateFromReadiness(hasDocument: false, cleanApplied: false, hasCells: false);
            UpdateWorkflowShell();
            return;
        }

        if (!_cleanApplied)
        {
            _workflowShell.UpdateFromReadiness(hasDocument: true, cleanApplied: false, hasCells: false);
            UpdateWorkflowShell();
            return;
        }

        if (_cells.Count == 0)
        {
            _workflowShell.UpdateFromReadiness(hasDocument: true, cleanApplied: true, hasCells: false);
            UpdateWorkflowShell();
            return;
        }

        _workflowShell.UpdateFromReadiness(hasDocument: true, cleanApplied: true, hasCells: true);
        UpdateWorkflowShell();
    }

    private void UpdateWorkflowShell()
    {
        BtnStepImporta.IsChecked = _workflowShell.ActiveStep == WorkflowShellViewModel.WorkflowStep.Importa;
        BtnStepPulisci.IsChecked = _workflowShell.ActiveStep == WorkflowShellViewModel.WorkflowStep.Pulisci;
        BtnStepSliceAllinea.IsChecked = _workflowShell.ActiveStep == WorkflowShellViewModel.WorkflowStep.SliceAllinea;
        BtnStepEsporta.IsChecked = _workflowShell.ActiveStep == WorkflowShellViewModel.WorkflowStep.Esporta;

        TxtWorkflowStepSummary.Text = _workflowShell.ActiveStep switch
        {
            WorkflowShellViewModel.WorkflowStep.Importa => $"{_workflowShell.StepSummary} · Apri un file PNG da elaborare.",
            WorkflowShellViewModel.WorkflowStep.Pulisci => $"{_workflowShell.StepSummary} · Applica un preset o workflow rapido.",
            WorkflowShellViewModel.WorkflowStep.SliceAllinea => $"{_workflowShell.StepSummary} · Rileva/sistema le celle sprite.",
            WorkflowShellViewModel.WorkflowStep.Esporta => $"{_workflowShell.StepSummary} · Esporta atlas PNG/JSON.",
            _ => _workflowShell.StepSummary
        };
        BtnWorkflowPrimaryAction.Content = _workflowShell.PrimaryActionLabel;
        UpdateWorkflowStepStates();
    }

    private void UpdateWorkflowStepStates()
    {
        var hasDoc = _document is not null;
        var cleanDone = hasDoc && _cleanApplied;
        var hasCells = _cells.Count > 0;

        TxtStepImportaState.Text = hasDoc ? "✔ completato" : "● richiesto";
        TxtStepPulisciState.Text = !hasDoc ? "🔒 bloccato" : (cleanDone ? "✔ completato" : "● richiesto");
        TxtStepSliceState.Text = !cleanDone ? "🔒 bloccato" : (hasCells ? "✔ completato" : "● richiesto");
        TxtStepEsportaState.Text = !hasCells ? "🔒 bloccato" : "● pronto";
    }

    private void SetWorkspaceBadge(string text, string bgHex, string borderHex, string fgHex) { }

    private void RunCcl()
    {
        var cells = SlicingController.RunCcl(
            _document,
            () => PushUndo(),
            ClearSliceGrid,
            cells => Editor.SpriteCells = cells.ToList(),
            SetStatus);
        if (cells is not null)
        {
            _cells = cells;
            RefreshCellList();
            UpdateWorkspaceGuidance();
        }
    }
    private void RunGlobalScan()
    {
        if (_document is null || _cells.Count == 0)
        {
            AnimationStudioPanel.SetGlobalScanResult("— Esegui prima la rilevazione sprite");
            SetStatus("Nessun sprite trovato: usa 'Rileva sprite' nella toolbar o 'Dividi a griglia' nel tab Dividi.");
            return;
        }
        try
        {
            // Statistiche complete (max / mediana / p90 / outlier) sulle dimensioni cella
            var cellBoxes = _cells.Select(c => c.BoundsInAtlas).ToList();
            var stats = FrameStatistics.Compute(cellBoxes);
            var warning = FrameStatistics.FormatOutlierWarning(stats);

            AnimationStudioPanel.SetGlobalScanResult(
                $"Max: {stats.MaxW}×{stats.MaxH} · Mediana: {stats.MedianW}×{stats.MedianH} · " +
                $"P90: {stats.Percentile90W}×{stats.Percentile90H}" +
                (warning is null ? "" : $"\n{warning}"));
            SetStatus($"Statistiche calcolate su {stats.Count} celle.");
        }
        catch (Exception ex)
        {
            SetStatus($"Errore scansione globale: {ex.Message}");
        }
    }

    private void RunBaselineAlignment()
    {
        if (_document is null)  { SetStatus("Nessuna immagine aperta."); return; }
        if (_cells.Count == 0)  { SetStatus("Nessun sprite trovato: usa 'Rileva sprite' nella toolbar o 'Dividi a griglia' nel tab Dividi."); return; }
        try
        {
            var policy = _animationState.NormalizePolicyIndex switch
            {
                1 => FrameStatistics.NormalizePolicy.Median,
                2 => FrameStatistics.NormalizePolicy.Percentile90,
                _ => FrameStatistics.NormalizePolicy.Max,
            };
            var result = BaselineAlignment.Align(_document, _cells, policy);
            _document?.Dispose();
            _document = result.Atlas;
            _cells    = result.Cells.ToList();
            ClearSliceGrid();
            Editor.SpriteCells = _cells;
            RefreshCellList();
            RefreshView();
            AnimationStudioPanel.SetGlobalScanResult($"Normalizzato: ogni cella {result.Atlas.Width / _cells.Count}×{result.Atlas.Height} px");
            SetStatus($"Allineamento completato: {_cells.Count} sprite, tutti con baseline ai piedi.");
        }
        catch (Exception ex)
        {
            SetStatus($"Errore allineamento: {ex.Message}");
        }
    }

    private void RunCenterInCells()
    {
        if (_document is null)   { SetStatus("Nessuna immagine aperta."); return; }
        if (_cells.Count == 0)   { SetStatus("Nessun sprite trovato: usa 'Rileva sprite' nella toolbar o 'Dividi a griglia' nel tab Dividi."); return; }
        try
        {
            PushUndo();
            var snap = _animationState.SnapToGrid ? 8 : 0;
            var result = CellCentering.Center(_document, _cells, alphaThreshold: 1, opaqueCornerSnapMultiple: snap);
            _document?.Dispose();
            _document = result.Atlas;
            Editor.SpriteCells = _cells;
            RefreshView();
            SetStatus($"Centrati {_cells.Count} sprite nelle rispettive celle.");
        }
        catch (Exception ex)
        {
            SetStatus($"Errore centratura: {ex.Message}");
        }
    }

    private void RunSnapCellsToGrid()
    {
        if (_document is null) { SetStatus("Nessuna immagine aperta."); return; }
        if (_cells.Count == 0) { SetStatus("Nessun sprite: usa 'Rileva sprite' nella toolbar o 'Dividi a griglia' nel tab Dividi."); return; }
        var g = Math.Max(1, (int)(TxtWorldGridSize.Value ?? 16));
        try
        {
            PushUndo();
            var result = GridCellSnap.SnapToReferenceGrid(_document, _cells, g);
            _document.Dispose();
            _document = result.Atlas;
            _cells = result.Cells.ToList();
            ClearSliceGrid();
            Editor.SpriteCells = _cells;
            RefreshCellList();
            RefreshView();
            SetStatus($"Agganciate {_cells.Count} celle alla griglia di riferimento ({g} px). Ctrl+Z per annullare.");
        }
        catch (Exception ex)
        {
            SetStatus($"Errore aggancio griglia: {ex.Message}");
        }
    }

    // ─── Pulizia AI ──────────────────────────────────────────────────────────

    // ─── Crop & POT (asset singolo) ──────────────────────────────────────────

    private AxisAlignedBox? _lastUserRoi;

    private void RunCropPipeline()
    {
        if (_document is null) { SetStatus("Nessuna immagine aperta."); return; }
        try
        {
            var mode = _tilesetState.CropModeIndex switch
            {
                1 => CropPipeline.CropMode.TrimToContentPadded,
                2 => CropPipeline.CropMode.UserRoi,
                _ => CropPipeline.CropMode.TrimToContent,
            };
            var pot = _tilesetState.CropPotIndex switch
            {
                1 => CropPipeline.PotPolicy.PerAxis,
                2 => CropPipeline.PotPolicy.Square,
                _ => CropPipeline.PotPolicy.None,
            };
            var alpha    = (byte)Math.Clamp(InputParsing.ParseInt(_tilesetState.CropAlpha, 1), 1, 255);
            var padding  = Math.Clamp(InputParsing.ParseInt(_tilesetState.CropPadding, 4), 0, 64);

            if (mode == CropPipeline.CropMode.UserRoi && _lastUserRoi is null)
            {
                SetStatus("ROI utente non disponibile: usa il toggle 'Selezione' nella toolbar " +
                          "oppure 'Ritaglio manuale' nella tab 'Dividi', trascina un rettangolo e riprova.");
                return;
            }

            PushUndo();
            using var result = CropPipeline.Apply(_document, new CropPipeline.Options
            {
                Mode           = mode,
                AlphaThreshold = alpha,
                PaddingPx      = padding,
                Pot            = pot,
                UserRoi        = _lastUserRoi,
            });

            // Sostituisce _document col risultato (Result.Image è disposata da using; cloniamo).
            var newDoc = result.Image.Clone();
            _document.Dispose();
            _document = newDoc;
            _cells.Clear();
            ClearSliceGrid();
            ClearSpriteCellList();
            RefreshView();
            SetStatus($"Crop applicato: {result.Description}.");
        }
        catch (Exception ex)
        {
            SetStatus($"Errore crop: {ex.Message}");
        }
    }

    private void RunAiCleanupWizard()
    {
        if (_document is null) { SetStatus("Nessuna immagine aperta."); return; }
        try
        {
            PushUndo();

            var alphaThr = (byte)Math.Clamp(InputParsing.ParseInt(_pipelineFormState.AlphaThreshold, 128), 0, 255);
            var defOpaque = (byte)Math.Clamp(InputParsing.ParseInt(_pipelineFormState.DefringeOpaque, 250), 1, 254);
            var minIsland = Math.Max(1, InputParsing.ParseInt(_pipelineFormState.MinIsland, 4));
            var palColors = Math.Clamp(InputParsing.ParseInt(_pipelineFormState.MaxColors, 16), 2, 64);

            var report = AiCleanupWizard.Apply(_document, new AiCleanupWizard.Options
            {
                RemoveBgColor   = false,
                DefringeEdges   = true,
                DefringeOpaque  = defOpaque,
                BinarizeAlpha   = true,
                AlphaThreshold  = alphaThr,
                DenoiseSpike    = true,
                DenoiseIslands  = true,
                IslandMinSize   = minIsland,
                ReducePalette   = _pipelineFormState.EnableQuantize,
                PaletteColors   = palColors,
                PaletteDither   = false,
            });

            ClearSliceGrid();
            _cells.Clear();
            ClearSpriteCellList();
            _cleanApplied = true;
            RefreshView();
            ResetPresetOnManualPipelineEdit();
            UpdateWorkspaceGuidance();
            SetStatus($"Pulizia AI: {report}");
        }
        catch (Exception ex)
        {
            SetStatus($"Errore pulizia AI: {ex.Message}");
        }
    }

    private void RunDefringe()
    {
        if (_document is null) { SetStatus("Nessuna immagine aperta."); return; }
        try
        {
            var opaque = (byte)Math.Clamp(InputParsing.ParseInt(_pipelineFormState.DefringeOpaque, 250), 1, 254);
            // Pre-flight: defringe agisce solo su pixel semi-trasparenti (0 < α < opaque).
            // Se non ce ne sono, è no-op → avvisa l'utente esplicitamente.
            var semiCount = ImageUtils.CountSemiTransparent(_document, opaque);
            if (semiCount == 0)
            {
                SetStatus($"Defringe: nessun pixel semi-trasparente (α 1–{opaque - 1}). " +
                          "Esegui prima 'Rimuovi sfondo' per generare i bordi semi-trasparenti, poi applica Defringe.");
                return;
            }
            PushUndo();
            Defringe.FromOpaqueNeighbors(_document, opaque);
            _cleanApplied = true;
            RefreshView();
            UpdateWorkspaceGuidance();
            SetStatus($"Defringe applicato: {semiCount:N0} pixel di edge ricolorati (soglia opaca {opaque}).");
        }
        catch (Exception ex)
        {
            SetStatus($"Errore defringe: {ex.Message}");
        }
    }

    private void RunMedianFilter()
    {
        RunTransform(MedianFilter.ApplyInPlace, "Filtro mediano 3×3 applicato.",
                     clearCells: false, "Errore median filter");
        _cleanApplied = true;
        UpdateWorkspaceGuidance();
    }

    private void RunPaletteReduce()
    {
        if (_document is null) { SetStatus("Nessuna immagine aperta."); return; }
        try
        {
            var presetIdx = _tilesetState.PalettePresetIndex;
            IReadOnlyList<Rgba32> palette;
            string label;

            if (presetIdx <= 0)
            {
                var n = Math.Clamp(InputParsing.ParseInt(_tilesetState.PaletteColors, 16), 2, 64);
                palette = PaletteExtractorAlgorithms.ExtractWu(_document, n);
                if (palette.Count == 0) { SetStatus("Nessun colore opaco trovato."); return; }
                label = $"Auto AI Wu {palette.Count}";
            }
            else
            {
                var preset = presetIdx switch
                {
                    1 => PalettePresets.Preset.GameBoyDMG,
                    2 => PalettePresets.Preset.Pico8,
                    3 => PalettePresets.Preset.NES16,
                    4 => PalettePresets.Preset.Sweetie16,
                    5 => PalettePresets.Preset.CGA4,
                    _ => PalettePresets.Preset.CleanAI,
                };
                palette = PalettePresets.Get(preset);
                if (palette.Count == 0) { SetStatus("Preset palette non valido."); return; }
                label = preset.ToString();
            }

            PushUndo();
            var dither = _tilesetState.DitherEnabled
                ? PaletteMapper.DitherMode.FloydSteinberg
                : PaletteMapper.DitherMode.None;
            PaletteMapper.ApplyInPlace(_document, palette, dither);
            RefreshView();
            SetStatus($"Palette {label} applicata ({palette.Count} colori{(dither == PaletteMapper.DitherMode.FloydSteinberg ? " + Floyd-Steinberg" : "")}).");
        }
        catch (Exception ex)
        {
            SetStatus($"Errore riduzione palette: {ex.Message}");
        }
    }

    private void RunSpriteQuantize()
    {
        if (_document is null) { SetStatus("Nessuna immagine aperta."); return; }
        try
        {
            ApplySpriteCleanupStateToControls();
            var n = Math.Clamp(InputParsing.ParseInt(_pipelineFormState.MaxColors, 16), 2, 64);
            var palette = _pipelineFormState.QuantizerIndex switch
            {
                1 => PaletteExtractorAlgorithms.ExtractOctree(_document, n),
                _ => PaletteExtractorAlgorithms.ExtractWu(_document, n),
            };
            if (palette.Count == 0) { SetStatus("Nessun colore opaco trovato."); return; }
            PushUndo();
            PaletteMapper.ApplyInPlace(_document, palette, PaletteMapper.DitherMode.None);

            // Dopo la rimappatura tutti i pixel appartengono alla palette.
            // Aggiorna il colore chiave sfondo all'entrata più vicina in RGB²:
            // così la rimozione sfondo successiva trova esattamente il pixel giusto.
            if (InputParsing.TryParseHexRgb(_backgroundIsolationHex, out var bgKey))
            {
                var nearest = palette.MinBy(c => ColorDistSq(c, bgKey));
                var newHex = $"#{nearest.R:X2}{nearest.G:X2}{nearest.B:X2}";
                _backgroundIsolationHex = newHex;
                var ui = SpriteStudioPanel.GetCleanupState();
                SpriteStudioPanel.SetCleanupState(ui with { BackgroundHex = newHex });
            }

            // Mostra swatch palette nel pannello
            SpriteStudioPanel.SetPalette(palette);

            _cleanApplied = true;
            RefreshView();
            UpdateWorkspaceGuidance();
            var method = _pipelineFormState.QuantizerIndex == 1 ? "Octree" : "Wu";
            SetStatus($"Quantize applicato: {palette.Count} colori ({method}). Colore sfondo aggiornato alla voce più vicina.");
        }
        catch (Exception ex)
        {
            SetStatus($"Errore quantize: {ex.Message}");
        }
    }

    private void RunAnalyzePalette()
    {
        if (_document is null) { SetStatus("Nessuna immagine aperta."); return; }
        try
        {
            ApplySpriteCleanupStateToControls();
            var n = Math.Clamp(InputParsing.ParseInt(_pipelineFormState.MaxColors, 16), 2, 256);
            var palette = _pipelineFormState.QuantizerIndex switch
            {
                1 => PaletteExtractorAlgorithms.ExtractOctree(_document, n),
                _ => PaletteExtractorAlgorithms.ExtractWu(_document, n),
            };
            if (palette.Count == 0) { SetStatus("Nessun colore opaco trovato."); return; }
            SpriteStudioPanel.SetPalette(palette);
            var method = _pipelineFormState.QuantizerIndex == 1 ? "Octree" : "Wu";
            SetStatus($"Palette analizzata: {palette.Count} colori ({method}). Immagine non modificata. Clicca uno swatch per impostarlo come colore sfondo.");
        }
        catch (Exception ex)
        {
            SetStatus($"Errore analisi palette: {ex.Message}");
        }
    }

    /// <summary>Distanza al quadrato tra due colori RGB (nessuna sqrt — solo per confronto).</summary>
    private static int ColorDistSq(Rgba32 a, Rgba32 b)
    {
        int dr = a.R - b.R;
        int dg = a.G - b.G;
        int db = a.B - b.B;
        return dr * dr + dg * dg + db * db;
    }

    private void RunMakeTileable()
    {
        var blend = Math.Clamp(InputParsing.ParseInt(_tilesetState.SeamlessBlend, 4), 1, 16);
        RunReplaceTransform(
            src => SeamlessEdge.MakeTileable(src, blend),
            $"Tile ripetibile generato (banda dither {blend} px). Attiva 'Anteprima tile 3×3' per verificare.",
            "Errore make tileable");
    }

    private void RunMirror(bool horizontal) =>
        RunTransform(
            img => { if (horizontal) SymmetryMirror.MirrorHorizontal(img, fromLeft: true);
                     else            SymmetryMirror.MirrorVertical  (img, fromTop:  true); },
            $"Simmetria {(horizontal ? "orizzontale" : "verticale")} applicata.",
            clearCells: false,
            "Errore simmetria");

    private void RunPadToMultiple()
    {
        if (_document is null) { SetStatus("Nessuna immagine aperta."); return; }
        try
        {
            var m = Math.Max(2, InputParsing.ParseInt(_tilesetState.PadMultiple, 16));
            PushUndo();
            var padded = AutoPad.PadToMultiple(_document, m);
            _document.Dispose();
            _document = padded;
            ClearSliceGrid();
            _cells.Clear();
            ClearSpriteCellList();
            RefreshView();
            SetStatus($"Allineato a multiplo di {m} px → {_document.Width}×{_document.Height}.");
        }
        catch (Exception ex)
        {
            SetStatus($"Errore padding: {ex.Message}");
        }
    }

    private async Task ExportPngAsync()
    {
        await ExportController.ExportPngAsync(
            _document,
            _cells,
            _animationState.PivotX,
            _animationState.PivotY,
            ChkExportCustomCellSize.IsChecked == true,
            ChkExportKeepCellSize.IsChecked == true,
            ChkExportAtlasIndexedPng.IsChecked == true,
            TxtExportCellW.Text,
            TxtExportCellH.Text,
            StorageProvider,
            SetStatus);
    }

    private async Task ExportJsonAsync()
    {
        await ExportController.ExportJsonAsync(
            _document,
            _cells,
            _animationState.PivotX,
            _animationState.PivotY,
            ChkExportCustomCellSize.IsChecked == true,
            ChkExportKeepCellSize.IsChecked == true,
            TxtExportCellW.Text,
            TxtExportCellH.Text,
            null,
            StorageProvider,
            SetStatus);
    }
    // ─── Gomma ───────────────────────────────────────────────────────────────

    private bool _eraserUndoPushed;
    private int _eraserDirtyMinX = int.MaxValue;
    private int _eraserDirtyMaxX = int.MinValue;
    private int _eraserDirtyMinY = int.MaxValue;
    private int _eraserDirtyMaxY = int.MinValue;
    private long _eraserLastFlushTicks;

    private void OnEraserStroke(object? sender, AiPixelScaler.Desktop.Controls.EraserStrokeEventArgs e)
    {
        if (_document is null) return;

        // Push undo una volta sola per passata (non ad ogni pixel)
        if (!_eraserUndoPushed)
        {
            PushUndo();
            _eraserUndoPushed = true;
        }

        // SideLength è già ≥ 1 per contratto di EraserStrokeEventArgs
        var side = e.SideLength;
        var imgW = _document.Width;
        var imgH = _document.Height;
        var xMin = Math.Clamp(e.ImageX, 0, imgW);
        var yMin = Math.Clamp(e.ImageY, 0, imgH);
        var xMax = Math.Clamp(e.ImageX + side, 0, imgW);
        var yMax = Math.Clamp(e.ImageY + side, 0, imgH);
        if (xMin >= xMax || yMin >= yMax) return;

        _document.ProcessPixelRows(accessor =>
        {
            for (var py = yMin; py < yMax; py++)
            {
                var row = accessor.GetRowSpan(py);
                for (var px = xMin; px < xMax; px++)
                {
                    if (row[px].A != 0)
                        row[px].A = 0;
                }
            }
        });

        _eraserDirtyMinX = Math.Min(_eraserDirtyMinX, xMin);
        _eraserDirtyMaxX = Math.Max(_eraserDirtyMaxX, xMax);
        _eraserDirtyMinY = Math.Min(_eraserDirtyMinY, yMin);
        _eraserDirtyMaxY = Math.Max(_eraserDirtyMaxY, yMax);

        var now = Environment.TickCount64;
        if (now - _eraserLastFlushTicks >= 16 && HasDirtyEraserRegion())
        {
            Editor.UpdateBitmapRegion(_document, _eraserDirtyMinX, _eraserDirtyMinY, _eraserDirtyMaxX, _eraserDirtyMaxY);
            _eraserLastFlushTicks = now;
            ClearDirtyEraserRegion();
        }
    }

    private void OnEraserStrokeEnded(object? sender, EventArgs e)
    {
        if (_document is not null && HasDirtyEraserRegion())
            Editor.UpdateBitmapRegion(_document, _eraserDirtyMinX, _eraserDirtyMinY, _eraserDirtyMaxX, _eraserDirtyMaxY);

        ClearDirtyEraserRegion();
        _eraserUndoPushed = false;
    }

    private bool HasDirtyEraserRegion() =>
        _eraserDirtyMinX < _eraserDirtyMaxX && _eraserDirtyMinY < _eraserDirtyMaxY;

    private void ClearDirtyEraserRegion()
    {
        _eraserDirtyMinX = int.MaxValue;
        _eraserDirtyMaxX = int.MinValue;
        _eraserDirtyMinY = int.MaxValue;
        _eraserDirtyMaxY = int.MinValue;
    }

    // ─── Atlas pulito (clone griglia + manual paste) ─────────────────────────

    private Image<Rgba32>?   _pasteSource;          // copia readonly dell'atlas originale
    private Image<Rgba32>?   _pasteBuffer;          // contenuto attualmente in clipboard (da incollare)
    private bool             _pasteModeActive;
    private bool             _viewingPasteSource;   // true = vista sorgente, false = vista destinazione
    private AxisAlignedBox?  _pasteLastSelection;

    private void EnterPasteMode()
    {
        ClearFloatingPasteSession();
        if (_document is null) { SetStatus("Apri prima un'immagine."); return; }
        if (_cells.Count == 0)
        {
            SetStatus("Dividi prima la griglia (auto o manuale) per creare le celle di destinazione.");
            return;
        }
        try
        {
            PushUndo();

            // Salva l'originale come sorgente di copia, sostituisce _document con un atlas vuoto
            _pasteSource?.Dispose();
            _pasteSource = _document.Clone();
            _document.Dispose();
            _document = new Image<Rgba32>(_pasteSource.Width, _pasteSource.Height, new Rgba32(0, 0, 0, 0));
            _pasteBuffer?.Dispose();
            _pasteBuffer = null;
            _pasteLastSelection = null;
            _pasteModeActive = true;

            // Default: vista destinazione, click su cella per incollare
            SwitchPasteView(toSource: false);

            UpdatePasteBufferStatus();
            SetStatus($"Atlas pulito creato: {_document.Width}×{_document.Height} px con {_cells.Count} celle vuote. Vai alla Sorgente per copiare i frame.");
        }
        catch (Exception ex)
        {
            SetStatus($"Errore creazione atlas pulito: {ex.Message}");
        }
    }

    private void SwitchPasteView(bool toSource)
    {
        if (!_pasteModeActive) return;
        _viewingPasteSource = toSource;

        if (toSource)
        {
            // Mostra l'atlas originale, abilita selezione rettangolo
            Editor.SetSourceImage(_pasteSource);
            Editor.IsSelectionMode = true;
            Editor.IsCellClickMode = false;
            Editor.SpriteCells = [];                 // niente overlay celle sulla sorgente
            SetManualSpriteCropMode(false);          // evita conflitto con il crop manuale
            SetStatus("Vista SORGENTE: trascina un rettangolo per selezionare uno sprite, poi 'Copia'.");
        }
        else
        {
            // Mostra l'atlas pulito (destinazione), abilita click-su-cella per incollare
            Editor.SetSourceImage(_document);
            Editor.IsSelectionMode = false;
            Editor.IsCellClickMode = true;
            Editor.SpriteCells = _cells;             // mostra griglia di destinazione
            SetManualSpriteCropMode(false);
            SetStatus(_pasteBuffer is null
                ? "Vista DESTINAZIONE: copia prima qualcosa dalla sorgente."
                : $"Vista DESTINAZIONE: clicca su una cella per incollare il buffer ({_pasteBuffer.Width}×{_pasteBuffer.Height} px, centrato).");
        }
    }

    private void CopySourceSelectionToBuffer()
    {
        if (!_pasteModeActive) return;
        if (_pasteSource is null) { SetStatus("Sorgente non disponibile."); return; }
        if (_pasteLastSelection is null || _pasteLastSelection.Value.IsEmpty)
        {
            SetStatus("Nessuna selezione: passa alla Sorgente e trascina un rettangolo.");
            return;
        }
        try
        {
            var box = _pasteLastSelection.Value;
            var crop = AtlasCropper.Crop(_pasteSource, in box);
            if (crop.Width < 1 || crop.Height < 1) { crop.Dispose(); SetStatus("Selezione vuota."); return; }
            _pasteBuffer?.Dispose();
            _pasteBuffer = crop;
            UpdatePasteBufferStatus();
            SetStatus($"Buffer riempito: {crop.Width}×{crop.Height} px. Vai alla Destinazione e clicca una cella per incollare.");
        }
        catch (Exception ex)
        {
            SetStatus($"Errore copia: {ex.Message}");
        }
    }

    private void OnCellPasteClicked(int idx)
    {
        if (!_pasteModeActive || _viewingPasteSource) return;
        if (_pasteBuffer is null)
        {
            SetStatus("Buffer vuoto: copia prima qualcosa dalla sorgente.");
            return;
        }
        if (_document is null || idx < 0 || idx >= _cells.Count) return;

        try
        {
            PushUndo();
            var cell = _cells[idx].BoundsInAtlas;
            // Centratura nel rettangolo cella
            var destX = cell.MinX + (cell.Width  - _pasteBuffer.Width)  / 2;
            var destY = cell.MinY + (cell.Height - _pasteBuffer.Height) / 2;

            // Pulisce la cella prima di incollare (così paste successive non sommano residui)
            ImageUtils.ClearRect(_document, cell.MinX, cell.MinY, cell.Width, cell.Height);
            _document.Mutate(ctx => ctx.DrawImage(_pasteBuffer, new SixLabors.ImageSharp.Point(destX, destY), 1f));

            RefreshView();
            SetStatus($"Incollato in cella {idx} a ({destX}, {destY}). Buffer ancora valido — puoi incollarlo in altre celle.");
        }
        catch (Exception ex)
        {
            SetStatus($"Errore incolla: {ex.Message}");
        }
    }

    private void ExitPasteMode()
    {
        _pasteSource?.Dispose(); _pasteSource = null;
        _pasteBuffer?.Dispose(); _pasteBuffer = null;
        _pasteLastSelection = null;
        _pasteModeActive = false;
        _viewingPasteSource = false;
        Editor.IsSelectionMode = false;
        Editor.IsCellClickMode = false;
        // Ripristina la vista sull'atlas pulito che è ora _document
        RefreshView();
        SetStatus("Modalità atlas pulito disattivata. L'atlas pulito è ora il documento attivo (esportabile).");
    }

    private void UpdatePasteBufferStatus()
    {
        var status = _pasteBuffer is null
            ? "Buffer: vuoto"
            : $"Buffer: {_pasteBuffer.Width}×{_pasteBuffer.Height} px";
        SpriteStudioPanel.SetPasteState(_pasteModeActive, _viewingPasteSource, status);
    }

    // ─── Workbench allineamento frame ────────────────────────────────────────

    private FrameSheet? _frameSheet;
    private (int x, int y)? _frameDragInitialOffset;   // offset iniziale del frame all'inizio del drag

    private void EnterFrameAlignMode()
    {
        ClearFloatingPasteSession();
        if (_document is null) { SetStatus("Nessuna immagine aperta."); return; }
        if (_cells.Count == 0)
        {
            SetStatus("Nessuno sprite trovato: usa 'Rileva sprite' nella toolbar o 'Dividi a griglia' nel tab Dividi.");
            return;
        }
        try
        {
            var padding = Math.Clamp(InputParsing.ParseInt(_animationState.ExtractPadding, 0), 0, 128);
            _frameSheet?.Dispose();
            _frameSheet = FrameSheet.ExtractFromAtlas(_document, _cells, padding);

            // Costruisce frame Avalonia.Bitmap UNA SOLA VOLTA (l'atlas non viene più ricomposto sul drag)
            var renderFrames = new List<Controls.WorkbenchFrameRender>(_frameSheet.Frames.Count);
            try
            {
                foreach (var f in _frameSheet.Frames)
                {
                    var bmp = Imaging.Rgba32BitmapBridge.ToBitmap(f.Content)
                              ?? throw new InvalidOperationException("Conversione frame→Bitmap fallita");
                    renderFrames.Add(new Controls.WorkbenchFrameRender
                    {
                        Content  = bmp,
                        Cell     = f.Cell,
                        Padding  = f.Padding,
                        ContentW = f.Content.Width,
                        ContentH = f.Content.Height,
                        Offset   = new Avalonia.Point(f.Offset.X, f.Offset.Y),
                    });
                }
            }
            catch
            {
                foreach (var rf in renderFrames) rf.Dispose();
                throw;
            }

            // Entra in workbench: render diretto, niente Bitmap atlas
            Editor.Bitmap = null;
            Editor.SetWorkbenchFrames(renderFrames, selectedIndex: 0);
            Editor.IsFrameEditMode = true;
            Editor.SpriteCells = [];
            ClearSliceGrid();
            AnimationStudioPanel.SetWorkbenchActive(true, _frameSheet.Frames.Count, padding);
            OnFrameSelected(this, 0);
            SetStatus($"Workbench attivo: {_frameSheet.Frames.Count} frame estratti (padding {padding} px). Clic per selezionare, drag per spostare.");
        }
        catch (Exception ex)
        {
            SetStatus($"Errore entrando in workbench: {ex.Message}");
        }
    }

    private void CommitFrameAlignMode()
    {
        if (_frameSheet is null) return;
        try
        {
            PushUndo();
            var composed = _frameSheet.Compose();
            _cells = BuildCommittedCellsFromFrameSheet(_frameSheet);
            _document?.Dispose();
            _document = composed;
            ExitFrameAlignMode();
            RefreshCellList();
            RefreshView();
            SetStatus("Allineamento applicato all'atlas.");
        }
        catch (Exception ex)
        {
            SetStatus($"Errore commit allineamento: {ex.Message}");
        }
    }

    private List<SpriteCell> BuildCommittedCellsFromFrameSheet(FrameSheet frameSheet)
    {
        var updated = new List<SpriteCell>(frameSheet.Frames.Count);
        foreach (var frame in frameSheet.Frames)
        {
            var sourceCell = frame.Index >= 0 && frame.Index < _cells.Count ? _cells[frame.Index] : null;
            var id = sourceCell?.Id ?? $"c{frame.Index}";
            var pivotX = sourceCell?.PivotNdcX ?? 0.5;
            var pivotY = sourceCell?.PivotNdcY ?? 0.5;
            var contentBox = frame.OpaqueBoundsInContent();
            if (contentBox is null)
            {
                updated.Add(new SpriteCell(id, frame.Cell, pivotX, pivotY));
                continue;
            }

            var drawX = frame.Cell.MinX + frame.Offset.X - frame.Padding;
            var drawY = frame.Cell.MinY + frame.Offset.Y - frame.Padding;
            var box = contentBox.Value;
            var minX = Math.Clamp(drawX + box.MinX, 0, frameSheet.AtlasWidth);
            var minY = Math.Clamp(drawY + box.MinY, 0, frameSheet.AtlasHeight);
            var maxX = Math.Clamp(drawX + box.MaxX, 0, frameSheet.AtlasWidth);
            var maxY = Math.Clamp(drawY + box.MaxY, 0, frameSheet.AtlasHeight);

            var committedBounds = minX < maxX && minY < maxY
                ? new AxisAlignedBox(minX, minY, maxX, maxY)
                : frame.Cell;
            updated.Add(new SpriteCell(id, committedBounds, pivotX, pivotY));
        }

        return updated;
    }

    private void CancelFrameAlignMode()
    {
        if (_frameSheet is null) return;
        ExitFrameAlignMode();
        RefreshView();
        SetStatus("Workbench annullato — atlas invariato.");
    }

    private void ExitFrameAlignMode()
    {
        _frameSheet?.Dispose();
        _frameSheet = null;
        Editor.IsFrameEditMode = false;
        Editor.ClearWorkbenchFrames();      // dispone i bitmap dei frame
        AnimationStudioPanel.SetWorkbenchActive(false);
        AnimationStudioPanel.SetSelectedFrameInfo("Nessun frame selezionato.");
        Editor.SpriteCells = _cells;
    }

    private void AlignAllFramesCenter()
    {
        if (!EnsureFrameWorkbenchActive()) return;
        var snap = _animationState.SnapToGrid ? 8 : 0;
        _frameSheet.AutoCenterAll(opaqueCornerSnapMultiple: snap);
        ApplyWorkbenchToCanvas();
        SetStatus("Tutti i frame centrati.");
    }

    private void AlignAllFramesBaseline()
    {
        if (!EnsureFrameWorkbenchActive()) return;
        _frameSheet.AlignAllToBaseline();
        ApplyWorkbenchToCanvas();
        SetStatus("Tutti i frame allineati ai piedi.");
    }

    private void ResetAllFrames()
    {
        if (!EnsureFrameWorkbenchActive()) return;
        _frameSheet.ResetAll();
        ApplyWorkbenchToCanvas();
        SetStatus("Frame ripristinati alla posizione originale.");
    }

    private void AlignSelectedFrame(bool center)
    {
        if (!EnsureFrameWorkbenchActive()) return;
        var idx = Editor.SelectedFrameIndex;
        if (idx < 0 || idx >= _frameSheet.Frames.Count)
        {
            SetStatus("Seleziona prima un frame nel canvas.");
            return;
        }
        var f = _frameSheet.Frames[idx];
        var snap = _animationState.SnapToGrid ? 8 : 0;
        if (center) f.AutoCenter(alphaThreshold: 1, opaqueCornerSnapMultiple: snap); else f.AlignToBaseline();
        ApplyWorkbenchToCanvas();
        SetStatus($"Frame {idx} {(center ? "centrato" : "allineato ai piedi")}.");
    }

    private void ResetSelectedFrame()
    {
        if (!EnsureFrameWorkbenchActive()) return;
        var idx = Editor.SelectedFrameIndex;
        if (idx < 0 || idx >= _frameSheet.Frames.Count)
        {
            SetStatus("Seleziona prima un frame nel canvas.");
            return;
        }
        _frameSheet.Frames[idx].Reset();
        ApplyWorkbenchToCanvas();
        SetStatus($"Frame {idx} ripristinato.");
    }

    private void AlignSelectedFrameToLayoutCenter()
    {
        if (!EnsureFrameWorkbenchActive()) return;
        var idx = Editor.SelectedFrameIndex;
        if (idx < 0 || idx >= _frameSheet.Frames.Count)
        {
            SetStatus("Seleziona prima un frame nel canvas.");
            return;
        }

        var f = _frameSheet.Frames[idx];
        var aabb = f.OpaqueBoundsInContent();
        if (aabb is null)
        {
            SetStatus($"Frame {idx}: nessun pixel opaco da centrare.");
            return;
        }

        var bb = aabb.Value;
        var contentCx = bb.MinX + bb.Width / 2;
        var contentCy = bb.MinY + bb.Height / 2;
        var targetCx = _frameSheet.AtlasWidth / 2;
        var targetCy = _frameSheet.AtlasHeight / 2;

        var offsetX = targetCx - (f.Cell.MinX + contentCx - f.Padding);
        var offsetY = targetCy - (f.Cell.MinY + contentCy - f.Padding);
        f.Offset = new SixLabors.ImageSharp.Point(offsetX, offsetY);

        ApplyWorkbenchToCanvas();
        OnFrameSelected(this, idx);
        SetStatus($"Frame {idx} centrato nel layout globale.");
    }

    [MemberNotNullWhen(true, nameof(_frameSheet))]
    private bool EnsureFrameWorkbenchActive()
    {
        if (_frameSheet is not null) return true;
        EnterFrameAlignMode();
        if (_frameSheet is not null) return true;
        SetStatus("Impossibile avviare l'allineamento frame: verifica slicing e immagine.");
        return false;
    }

    private void OnFrameSelected(object? sender, int idx)
    {
        if (_frameSheet is null || idx < 0 || idx >= _frameSheet.Frames.Count) return;
        var f = _frameSheet.Frames[idx];
        AnimationStudioPanel.SetSelectedFrameInfo($"Frame {idx}: {f.Cell.Width}×{f.Cell.Height} px · offset ({f.Offset.X}, {f.Offset.Y})");
        _frameDragInitialOffset = (f.Offset.X, f.Offset.Y);
    }

    private void OnFrameDragged(object? sender, AiPixelScaler.Desktop.Controls.FrameDragEventArgs e)
    {
        if (_frameSheet is null || e.FrameIndex < 0 || e.FrameIndex >= _frameSheet.Frames.Count) return;
        if (_frameDragInitialOffset is null) _frameDragInitialOffset = (0, 0);
        var (ox, oy) = _frameDragInitialOffset.Value;
        var newX = ox + e.DxImage;
        var newY = oy + e.DyImage;

        // Snap a centro/baseline se abilitato e dentro raggio
        if (Editor.FrameSnapEnabled)
        {
            var f = _frameSheet.Frames[e.FrameIndex];
            var aabb = f.OpaqueBoundsInContent();
            if (aabb is not null)
            {
                var bb = aabb.Value;
                var contentCx = bb.MinX + bb.Width  / 2;
                var contentCy = bb.MinY + bb.Height / 2;
                var contentBottomIncl = bb.MaxY - 1;

                var radius = Editor.FrameSnapRadius;

                // snap X centro
                var targetOx = f.Cell.Width / 2 - contentCx + f.Padding;
                if (Math.Abs(newX - targetOx) <= radius) newX = targetOx;

                // snap Y centro
                var targetOyCenter = f.Cell.Height / 2 - contentCy + f.Padding;
                // snap Y baseline
                var targetOyBase   = f.Cell.Height - 1 - contentBottomIncl + f.Padding;
                var dCenter = Math.Abs(newY - targetOyCenter);
                var dBase   = Math.Abs(newY - targetOyBase);
                if (dCenter <= radius && dCenter <= dBase) newY = targetOyCenter;
                else if (dBase   <= radius)               newY = targetOyBase;
            }
        }

        _frameSheet.Frames[e.FrameIndex].Offset = new SixLabors.ImageSharp.Point(newX, newY);
        // FAST PATH: aggiorna solo l'offset del singolo frame nel renderer (no compose, no bitmap conversion)
        Editor.UpdateFrameOffset(e.FrameIndex, newX, newY);

        if (e.IsCommit)
        {
            _frameDragInitialOffset = null;
            var f = _frameSheet.Frames[e.FrameIndex];
            AnimationStudioPanel.SetSelectedFrameInfo($"Frame {e.FrameIndex}: {f.Cell.Width}×{f.Cell.Height} px · offset ({f.Offset.X}, {f.Offset.Y})");
        }
    }

    /// <summary>
    /// Sincronizza tutti gli offset del FrameSheet → render frames dell'editor.
    /// Costo: O(N) update (no atlas compose, no Bitmap conversion). Usato dopo
    /// operazioni batch (auto-center, baseline, reset).
    /// </summary>
    private void ApplyWorkbenchToCanvas(bool selectFirst = false)
    {
        if (_frameSheet is null) return;
        for (var i = 0; i < _frameSheet.Frames.Count; i++)
        {
            var f = _frameSheet.Frames[i];
            Editor.UpdateFrameOffset(i, f.Offset.X, f.Offset.Y);
        }
        if (selectFirst && _frameSheet.Frames.Count > 0)
            OnFrameSelected(this, 0);
    }

    // ─── Griglia (guida template + slicing) ─────────────────────────────────

    private Image<Rgba32>? _templateDocument;    private GridTemplateGenerator.Options BuildTemplateOptions()
    {
        var preset = PivotPresetFromIndex(_tilesetState.PivotIndex);
        var (ndcX, ndcY) = preset == GridTemplateGenerator.PivotPreset.Custom
            ? (_tilesetState.PivotCustomX, _tilesetState.PivotCustomY)
            : GridTemplateGenerator.PivotNdc(preset);

        return new GridTemplateGenerator.Options
        {
            Rows             = Math.Max(1, InputParsing.ParseInt(_tilesetState.Rows, 4)),
            Cols             = Math.Max(1, InputParsing.ParseInt(_tilesetState.Cols, 4)),
            CellWidth        = Math.Max(4, InputParsing.ParseInt(_tilesetState.CellW, 64)),
            CellHeight       = Math.Max(4, InputParsing.ParseInt(_tilesetState.CellH, 64)),
            ShowBorder       = _tilesetState.ShowBorder,
            BorderThickness  = 1,
            ShowPivotMarker  = _tilesetState.ShowPivot,
            ShowBaselineLine = _tilesetState.ShowBaseline,
            ShowCellIndex    = _tilesetState.ShowIndex,
            ShowCellTint     = _tilesetState.ShowTint,
            Pivot            = preset,
            PivotNdcX        = ndcX,
            PivotNdcY        = ndcY,
        };
    }
    private static GridTemplateGenerator.PivotPreset PivotPresetFromIndex(int index) => index switch
    {
        0 => GridTemplateGenerator.PivotPreset.TopLeft,
        1 => GridTemplateGenerator.PivotPreset.TopCenter,
        2 => GridTemplateGenerator.PivotPreset.TopRight,
        3 => GridTemplateGenerator.PivotPreset.MidLeft,
        4 => GridTemplateGenerator.PivotPreset.Center,
        5 => GridTemplateGenerator.PivotPreset.MidRight,
        6 => GridTemplateGenerator.PivotPreset.BottomLeft,
        7 => GridTemplateGenerator.PivotPreset.BottomCenter,
        8 => GridTemplateGenerator.PivotPreset.BottomRight,
        _ => GridTemplateGenerator.PivotPreset.Custom
    };

    private void RunGenerateTemplate()
    {
        try
        {
            var opts = BuildTemplateOptions();
            _templateDocument?.Dispose();
            _templateDocument = GridTemplateGenerator.Generate(opts);

            // Promuovi il template a documento di lavoro così tutti gli strumenti lavorano su di esso
            PushUndo();
            _document?.Dispose();
            _document = _templateDocument.Clone();
            _backup?.Dispose();
            _backup = _templateDocument.Clone();
            _cells.Clear();
            ClearSliceGrid();
            // Crea celle corrispondenti alla griglia del template
            _cells = GridSlicer.SliceExact(opts.Cols, opts.Rows, opts.CellWidth, opts.CellHeight).ToList();
            Editor.SpriteCells = _cells;
            RefreshCellList();
            Editor.SetSourceImage(_document);
            EmptyStateDim.IsVisible   = false;
            EmptyStatePanel.IsVisible = false;
            SetStatus($"Griglia generata: {_templateDocument.Width}×{_templateDocument.Height} px — " +
                      $"{opts.Cols}×{opts.Rows} celle {opts.CellWidth}×{opts.CellHeight} px.");
        }
        catch (Exception ex)
        {
            SetStatus($"Errore generazione griglia: {ex.Message}");
        }
    }

    private async Task ExportTemplateAsync()
    {
        if (_templateDocument is null)
        {
            SetStatus("Genera prima la griglia con «Genera griglia».");
            return;
        }
        try
        {
            var file = await StorageProvider.SaveFilePickerAsync(new FilePickerSaveOptions
            {
                Title = "Salva guida griglia come PNG",
                DefaultExtension = "png",
                FileTypeChoices = [new FilePickerFileType("PNG") { Patterns = ["*.png"] }],
                SuggestedFileName = $"griglia_guida_{_templateDocument.Width}x{_templateDocument.Height}.png"
            });
            if (file is null) return;

            await using var stream = await file.OpenWriteAsync();
            _templateDocument.Save(stream, new PngEncoder());
            SetStatus($"Guida PNG salvata ({_templateDocument.Width}×{_templateDocument.Height} px).");
        }
        catch (Exception ex)
        {
            SetStatus($"Errore salvataggio guida: {ex.Message}");
        }
    }

    // ─── Pannello destro collassabile ────────────────────────────────────────

    private void SetPanelCollapsed(bool collapse)
    {
        RightPanel.IsVisible      = !collapse;
        PanelSplitter.IsVisible   = !collapse;
        BtnExpandPanel.IsVisible  = collapse;
        MainBodyGrid.ColumnDefinitions[2].Width = collapse
            ? new GridLength(0)
            : new GridLength(4);
        MainBodyGrid.ColumnDefinitions[3].Width = collapse
            ? new GridLength(0)
            : new GridLength(280);
    }

    // ─── Tab Selezione libera ─────────────────────────────────────────────────

    /// <summary>
    /// Chiamato quando l'utente cambia tab nel pannello destro.
    /// Entra/esce dalla modalità canvas-selezione automaticamente.
    /// </summary>
    private void OnMainTabChanged(object? sender, Avalonia.Controls.SelectionChangedEventArgs e)
    {
        EnterSelectionCanvas(IsRoiSelectionModeRequested());
    }

    private void ToggleToolbarSelectionMode()
    {
        _toolbarSelectionModeEnabled = !_toolbarSelectionModeEnabled;
        if (_toolbarSelectionModeEnabled)
            SetStatus("Selezione ROI non distruttiva attivata dalla toolbar.");
        else
            SetStatus("Selezione ROI non distruttiva disattivata dalla toolbar.");
        EnterSelectionCanvas(IsRoiSelectionModeRequested());
    }

    private bool IsRoiSelectionModeRequested() =>
        _toolbarSelectionModeEnabled;

    private void ApplyEditorSelectionMode()
    {
        Editor.IsSelectionMode = _manualSpriteCropMode || IsRoiSelectionModeRequested();
    }

    private void EnterSelectionCanvas(bool enter)
    {
        if (enter)
        {
            // Disattiva modalità conflittuali
            SetManualSpriteCropMode(false);
            ChkPipette.IsChecked    = false;
            Editor.IsPipetteMode    = false;
            // Attiva strumento selezione
            ApplyEditorSelectionMode();
            // Mostra la selezione committed (se presente)
            Editor.SetCommittedSelection(_activeSelectionBox);
            UpdateSelectionInfo();
        }
        else
        {
            // Esci dalla modalità canvas
            ApplyEditorSelectionMode();
            Editor.SetCommittedSelection(null);
        }
    }

    private void SelectAll()
    {
        _activeSelectionBox = SelectionController.SelectAll(_document, SetStatus);
        if (_activeSelectionBox is not null)
        {
            Editor.SetCommittedSelection(_activeSelectionBox);
            UpdateSelectionInfo();
        }
    }

    private void ClearSelection()
    {
        ExitSelectionMode();
        SetStatus("Selezione rimossa.");
    }

    private async Task ExportSelectionAsync()
    {
        await SelectionController.ExportSelectionAsync(_document, _activeSelectionBox, StorageProvider, SetStatus);
    }

    private void CropToSelection()
    {
        SelectionController.CropToSelection(
            ref _document,
            _activeSelectionBox,
            () => PushUndo(),
            ClearCellsAfterDocumentReplace,
            RefreshView,
            SetStatus);
        ExitSelectionMode();
    }

    private void RemoveSelectedArea()
    {
        SelectionController.RemoveSelectedArea(_document, _activeSelectionBox, () => PushUndo(), RefreshView, SetStatus);
        ExitSelectionMode();
    }

    /// <summary>
    /// Azzera la selezione corrente e disattiva la toolbar selection mode.
    /// Da chiamare dopo ogni azione che consuma la selezione (Ritaglia, Cancella, Deseleziona).
    /// La modalità rimane attiva solo mentre l'utente sta disegnando una selezione.
    /// </summary>
    private void ExitSelectionMode()
    {
        _activeSelectionBox = null;
        Editor.SetCommittedSelection(null);
        UpdateSelectionInfo();
        if (_toolbarSelectionModeEnabled)
        {
            _toolbarSelectionModeEnabled = false;
            EnterSelectionCanvas(false);
        }
    }

    private void ClearCellsAfterDocumentReplace()
    {
        _cells.Clear();
        ClearSliceGrid();
        ClearSpriteCellList();
    }
    private void UpdateSelectionInfo()
    {
        if (_activeSelectionBox is null)
        {
            const string noSelectionText = "Nessuna selezione.\nTrascina sull'immagine per iniziare.";
            SpriteStudioPanel.SetSelectionInfo(noSelectionText, hasSelection: false);
            return;
        }
        var b = _activeSelectionBox.Value;
        // MaxX/MaxY sono esclusivi (half-open); ultimo pixel incluso è Max−1.
        var selectionText =
            $"Origine (↖): ({b.MinX}, {b.MinY}) px\n" +
            $"Dimensione: {b.Width} × {b.Height} px\n" +
            $"Ultimo pixel incluso (↘): ({b.MaxX - 1}, {b.MaxY - 1})";
        SpriteStudioPanel.SetSelectionInfo(selectionText, hasSelection: true);
    }

    private bool _importInProgress;

    private async Task RunImportFramesAsync()
    {
        if (_importInProgress) { SetStatus("Importazione già in corso…"); return; }
        _importInProgress = true;
        TilesetStudioPanel.GridSection.SetImportEnabled(false);
        AnimationStudioPanel.GridSection.SetImportEnabled(false);
        try
        {
            var cols  = Math.Max(1, InputParsing.ParseInt(_tilesetState.Cols, 4));
            var rows  = Math.Max(1, InputParsing.ParseInt(_tilesetState.Rows, 1));
            var cellW = Math.Max(4, InputParsing.ParseInt(_tilesetState.CellW, 64));
            var cellH = Math.Max(4, InputParsing.ParseInt(_tilesetState.CellH, 64));
            var total = cols * rows;

            var files = await StorageProvider.OpenFilePickerAsync(new FilePickerOpenOptions
            {
                Title = "Seleziona frame PNG (verranno ordinati alfabeticamente)",
                AllowMultiple = true,
                FileTypeFilter = [new FilePickerFileType("Immagini PNG") { Patterns = ["*.png", "*.PNG"] }]
            });
            if (files.Count == 0) return;

            var sorted = files.OrderBy(f => f.Name).Take(total).ToList();
            var atlas  = new Image<Rgba32>(cols * cellW, rows * cellH, new Rgba32(0, 0, 0, 0));
            var cells  = new List<SpriteCell>(total);

            for (var i = 0; i < sorted.Count; i++)
            {
                var col   = i % cols;
                var row   = i / cols;
                var cellX = col * cellW;
                var cellY = row * cellH;

                await using var stream = await sorted[i].OpenReadAsync();
                using var frame = await SixLabors.ImageSharp.Image.LoadAsync<Rgba32>(stream);

                var opaqueBox = CellCentering.FindOpaqueBox(frame);
                int dx, dy;
                if (opaqueBox is null)
                {
                    dx = (cellW - frame.Width)  / 2;
                    dy = (cellH - frame.Height) / 2;
                }
                else
                {
                    var ob = opaqueBox.Value;
                    dx = cellW / 2 - (ob.MinX + ob.Width  / 2);
                    dy = cellH / 2 - (ob.MinY + ob.Height / 2);
                }

                atlas.Mutate(ctx => ctx.DrawImage(frame, new SixLabors.ImageSharp.Point(cellX + dx, cellY + dy), 1f));
                cells.Add(new SpriteCell($"r{row}c{col}",
                    new AxisAlignedBox(cellX, cellY, cellX + cellW, cellY + cellH)));
            }

            // Celle vuote rimanenti
            for (var i = sorted.Count; i < total; i++)
            {
                var col   = i % cols;
                var row   = i / cols;
                var cellX = col * cellW;
                var cellY = row * cellH;
                cells.Add(new SpriteCell($"r{row}c{col}",
                    new AxisAlignedBox(cellX, cellY, cellX + cellW, cellY + cellH)));
            }

            PushUndo();
            _document?.Dispose();
            _document = atlas;
            _backup?.Dispose();
            _backup = atlas.Clone();
            _cells = cells;
            ClearSliceGrid();
            Editor.SpriteCells = _cells;
            RefreshCellList();
            RefreshView();
            EmptyStateDim.IsVisible   = false;
            EmptyStatePanel.IsVisible = false;
            SetStatus($"Importati {sorted.Count}/{total} frame — atlas {atlas.Width}×{atlas.Height} px " +
                      $"({cellW}×{cellH} px/cella).");
        }
        catch (Exception ex)
        {
            SetStatus($"Errore importazione frame: {ex.Message}");
        }
        finally
        {
            _importInProgress = false;
            TilesetStudioPanel.GridSection.SetImportEnabled(true);
            AnimationStudioPanel.GridSection.SetImportEnabled(true);
        }
    }

    protected override void OnUnloaded(Avalonia.Interactivity.RoutedEventArgs e)
    {
        ClearFloatingPasteSession();
        ClearUndoStack();
        _document?.Dispose();
        _document = null;
        _backup?.Dispose();
        _backup = null;
        _templateDocument?.Dispose();
        _templateDocument = null;
        _frameSheet?.Dispose();
        _frameSheet = null;
        _pasteSource?.Dispose();
        _pasteSource = null;
        _pasteBuffer?.Dispose();
        _pasteBuffer = null;
        _workspaceTabs.Dispose();
        base.OnUnloaded(e);
    }
}

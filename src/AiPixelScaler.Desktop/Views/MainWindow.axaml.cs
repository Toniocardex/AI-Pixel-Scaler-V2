using System;
using System.Collections.Generic;
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
using AiPixelScaler.Desktop.Imaging;
using AiPixelScaler.Desktop.Services;
using AiPixelScaler.Desktop.Utilities;
using AiPixelScaler.Desktop.ViewModels;
using Avalonia.Controls;
using Avalonia.Input;
using Avalonia.Input.Platform;
using Avalonia.Interactivity;
using Avalonia.Media.Imaging;
using Avalonia.Platform.Storage;
using SixLabors.ImageSharp;
using SixLabors.ImageSharp.Formats.Png;
using SixLabors.ImageSharp.PixelFormats;
using SixLabors.ImageSharp.Processing;

namespace AiPixelScaler.Desktop.Views;

public partial class MainWindow : Window
{
    private const int MaxUndo = 20;
    private const int SelectionCanvasTabIndex = 6;  // indice del tab "Selezione"

    private Image<Rgba32>? _document;
    private Image<Rgba32>? _backup;
    private List<SpriteCell> _cells = new();
    private bool _hasUserFile;
    private bool _cleanApplied;
    private readonly WorkspaceUndoCoordinator _undoCoordinator;
    private readonly FloatingPasteCoordinator _floatingPaste;
    private readonly PipelineViewModel _pipelineVm = new();
    private bool _isApplyingPipelinePreset;
    private readonly WorkflowShellViewModel _workflowShell = new();
    private readonly DelegateCommand _runQuickProcessCommand;
    private readonly DelegateCommand _applySafePresetCommand;
    private readonly DelegateCommand _applyAggressivePresetCommand;
    private readonly DelegateCommand _applyPipelineCommand;
    private readonly WorkspaceTabsController _workspaceTabs = new();
    private readonly UiPreferencesService _uiPreferences = new();
    private bool _workspaceTabSwitching;

    // ── Selezione canvas ─────────────────────────────────────────────────────
    private bool             _toolbarSelectionModeEnabled;
    private AxisAlignedBox?  _activeSelectionBox;    // ultima selezione confermata

    public MainWindow()
    {
        InitializeComponent();
        _runQuickProcessCommand = new DelegateCommand(RunQuickProcess, () => _document is not null);
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
        BtnPanelSandbox.Click += (_, _) => OpenSandbox();
        BtnPanelAnimPreview.Click += (_, _) => OpenAnimationPreview();
        BtnToolbarRilevaSprite.Click += (_, _) => RunCcl();
        BtnToolbarSelectionQuick.Click += (_, _) => ToggleToolbarSelectionMode();
        MiToolbarSelectionMode.Click += (_, _) => ToggleToolbarSelectionMode();
        MiToolbarCropToSelection.Click += (_, _) => CropToSelection();
        MiToolbarRemoveSelectedArea.Click += (_, _) => RemoveSelectedArea();
        MenuAnimPreview.Click += (_, _) => OpenAnimationPreview();
        EmptyStateOpen.Click += async (_, _) => await OpenImageAsync();

        // Contagocce inline accanto ai color picker
        BtnPickChroma.Click  += (_, _) => ActivatePipette(0);
        BtnPickEdge.Click    += (_, _) => ActivatePipette(1);
        BtnPickOutline.Click += (_, _) => ActivatePipette(2);

        // Canvas
        ChkWorldGrid.IsCheckedChanged += (_, _) =>
            Editor.ShowGrid = ChkWorldGrid.IsChecked == true;
        TxtWorldGridSize.ValueChanged += (_, _) =>
        {
            Editor.WorldGridSize = (int)(TxtWorldGridSize.Value ?? 16);
            // Auto-sync passo snap se sincronizzazione attiva
            if (ChkSnapSyncGrid.IsChecked == true)
                TxtSnapSize.Value = TxtWorldGridSize.Value;
        };

        ChkSnapGrid.IsCheckedChanged += (_, _) =>
        {
            var on = ChkSnapGrid.IsChecked == true;
            Editor.SnapToGrid     = on;
            TxtSnapSize.IsEnabled = on;
            // Quando si attiva la magnetica, sincronizza il passo con la griglia principale
            if (on && ChkSnapSyncGrid.IsChecked == true)
                TxtSnapSize.Value = TxtWorldGridSize.Value;
        };
        TxtSnapSize.ValueChanged += (_, _) =>
            Editor.SnapGridSize = (int)(TxtSnapSize.Value ?? 16);
        BtnCenterCanvas.Click += (_, _) => RunCenterCanvas();

        ChkEraser.IsCheckedChanged += (_, _) =>
        {
            var on = ChkEraser.IsChecked == true;
            Editor.IsEraserMode      = on;
            TxtEraserRadius.IsEnabled = on;
            // Modalità mutualmente esclusive
            if (on)
            {
                ChkPipette.IsChecked    = false;
                ChkSpriteCrop.IsChecked = false;
            }
        };
        TxtEraserRadius.ValueChanged += (_, _) =>
            Editor.EraserRadius = (int)(TxtEraserRadius.Value ?? 4);

        Editor.EraserStroke += OnEraserStroke;
        Editor.EraserStrokeEnded += OnEraserStrokeEnded;
        ChkPipette.IsCheckedChanged += (_, _) => UpdatePipetteMode();
        ChkSpriteCrop.IsCheckedChanged += (_, _) => UpdateSpriteSelectionMode();
        Editor.ImagePixelPicked += OnEditorImagePixelPicked;
        Editor.ImageSelectionCompleted += OnEditorImageSelectionCompleted;
        Editor.CommittedSelectionEdited += OnEditorCommittedSelectionEdited;
        Editor.FloatingOverlayMoved += OnEditorFloatingOverlayMoved;

        // Pivot
        SliderPivotX.ValueChanged += (_, _) => UpdatePivotLabels();
        SliderPivotY.ValueChanged += (_, _) => UpdatePivotLabels();

        // Passo 1 — Pulisci
        BtnQuickProcess.Click += (_, _) => _runQuickProcessCommand.Execute(null);
        BtnPresetSafe.Click += (_, _) => _applySafePresetCommand.Execute(null);
        BtnPresetAggressiveRecover.Click += (_, _) => _applyAggressivePresetCommand.Execute(null);
        BtnPipeApply.Click += (_, _) => _applyPipelineCommand.Execute(null);
        HookPipelinePresetResetOnManualChanges();
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
        BtnEdgeBfs.Click   += (_, _) => RunEdgeBackground();
        BtnDenoise.Click   += (_, _) => RunDenoise();
        BtnNnResize.Click  += (_, _) => RunNearestResize();

        // Passo 2 — Dividi
        BtnGridSlice.Click        += (_, _) => RunGridSlice();
        BtnSaveSelectedFrame.Click += async (_, _) => await SaveSelectedFrameAsync();

        // Passo 3 — Allinea
        BtnGlobal.Click         += (_, _) => RunGlobalScan();
        BtnBaselineAlign.Click  += (_, _) => RunBaselineAlignment();

        // Passo 4 — Pulizia AI
        BtnAiCleanupAll.Click += (_, _) => RunAiCleanupWizard();
        BtnDefringe.Click     += (_, _) => RunDefringe();
        BtnMedianFilter.Click += (_, _) => RunMedianFilter();
        BtnPaletteReduce.Click += (_, _) => RunPaletteReduce();
        BtnMirrorH.Click      += (_, _) => RunMirror(horizontal: true);
        BtnMirrorV.Click      += (_, _) => RunMirror(horizontal: false);
        BtnPadToMultiple.Click += (_, _) => RunPadToMultiple();
        BtnMakeTileable.Click += (_, _) => RunMakeTileable();
        ChkTilePreview.IsCheckedChanged += (_, _) =>
            Editor.IsTilePreviewMode = ChkTilePreview.IsChecked == true;

        // Atlas pulito (clone griglia + manual paste)
        BtnPasteEnter.Click  += (_, _) => EnterPasteMode();
        BtnPasteExit.Click   += (_, _) => ExitPasteMode();
        BtnPasteCopy.Click   += (_, _) => CopySourceSelectionToBuffer();
        BtnPasteDest.Click   += (_, _) => SwitchPasteView(toSource: false);
        BtnPasteSrc.Click    += (_, _) => SwitchPasteView(toSource: true);
        Editor.CellClicked   += (_, idx) => OnCellPasteClicked(idx);

        // Pannello destro — comprimi/espandi
        BtnCollapsePanel.Click += (_, _) => SetPanelCollapsed(true);
        BtnExpandPanel.Click   += (_, _) => SetPanelCollapsed(false);

        // Tab Selezione libera
        MainTabs.SelectionChanged += OnMainTabChanged;
        ChkShowAdvancedTabs.IsCheckedChanged += (_, _) =>
        {
            var show = ChkShowAdvancedTabs.IsChecked == true;
            if (_workspaceTabs.ActiveTab is { } activeTab)
                activeTab.IsAdvancedMode = show;
            _uiPreferences.SaveShowAdvancedTabs(show);
            if (!show && (ReferenceEquals(MainTabs.SelectedItem, TabStylize) ||
                          ReferenceEquals(MainTabs.SelectedItem, TabTemplate) ||
                          ReferenceEquals(MainTabs.SelectedItem, TabSelection)))
            {
                MainTabs.SelectedIndex = 0;
            }
            TabStylize.IsVisible = show;
            TabTemplate.IsVisible = show;
            TabSelection.IsVisible = show;
            SetStatus(show
                ? "Tab avanzate visibili."
                : "Tab avanzate nascoste (layout semplificato).");
        };
        BtnWorkspaceOpen.Click += async (_, _) => await OpenImageAsync();
        BtnWorkspaceCopyToClipboard.Click += async (_, _) => await TryCopyImageToClipboardAsync();
        BtnWorkspacePasteFromClipboard.Click += async (_, _) => await TryPasteFromClipboardAsync();
        BtnWorkspaceExportPng.Click += async (_, _) => await ExportPngAsync();
        BtnWorkspaceExportJson.Click += async (_, _) => await ExportJsonAsync();
        BtnWorkspaceGuideAction.Click += async (_, _) => await RunWorkspaceGuideActionAsync();
        BtnWorkflowPrimaryAction.Click += async (_, _) => await RunWorkspaceGuideActionAsync();
        BtnWorkflowNextStep.Click += async (_, _) => await AdvanceWorkflowStepAsync();
        BtnStepImporta.Click += (_, _) => ExecuteSelectStepCommand(WorkflowShellViewModel.WorkflowStep.Importa);
        BtnStepPulisci.Click += (_, _) => ExecuteSelectStepCommand(WorkflowShellViewModel.WorkflowStep.Pulisci);
        BtnStepSliceAllinea.Click += (_, _) => ExecuteSelectStepCommand(WorkflowShellViewModel.WorkflowStep.SliceAllinea);
        BtnStepEsporta.Click += (_, _) => ExecuteSelectStepCommand(WorkflowShellViewModel.WorkflowStep.Esporta);
        BtnGoAllinea.Click += (_, _) => MainTabs.SelectedIndex = 2;
        BtnGoStilizza.Click += (_, _) =>
        {
            ChkShowAdvancedTabs.IsChecked = true;
            MainTabs.SelectedIndex = 3;
        };
        BtnGoTemplate.Click += (_, _) =>
        {
            ChkShowAdvancedTabs.IsChecked = true;
            MainTabs.SelectedIndex = 5;
        };
        BtnSelectAll.Click       += (_, _) => SelectAll();
        BtnClearSelection.Click  += (_, _) => ClearSelection();
        BtnExportSelection.Click += async (_, _) => await ExportSelectionAsync();
        BtnCropToSelection.Click += (_, _) => CropToSelection();
        ChkExportCustomCellSize.IsCheckedChanged += (_, _) =>
        {
            var enabled = ChkExportCustomCellSize.IsChecked == true;
            TxtExportCellW.IsEnabled = enabled;
            TxtExportCellH.IsEnabled = enabled;
            UpdateExportCellMinRequiredHint();
        };
        TxtExportCellW.TextChanged += (_, _) => UpdateExportCellMinRequiredHint();
        TxtExportCellH.TextChanged += (_, _) => UpdateExportCellMinRequiredHint();
        ChkSnapSyncGrid.IsCheckedChanged += (_, _) =>
        {
            if (ChkSnapSyncGrid.IsChecked == true && ChkSnapGrid.IsChecked == true)
                TxtSnapSize.Value = TxtWorldGridSize.Value;
        };

        if (_uiPreferences.TryLoadShowAdvancedTabs(out var showAdvancedSaved))
            ChkShowAdvancedTabs.IsChecked = showAdvancedSaved;

        // Crop & POT (asset singolo)
        BtnApplyCropPot.Click    += (_, _) => RunCropPipeline();
        BtnCenterInCells.Click   += (_, _) => RunCenterInCells();
        BtnSnapCellsToGrid.Click += (_, _) => RunSnapCellsToGrid();

        // Workbench allineamento frame
        BtnAlignEnter.Click       += (_, _) => EnterFrameAlignMode();
        BtnAlignApply.Click       += (_, _) => CommitFrameAlignMode();
        BtnAlignCancel.Click      += (_, _) => CancelFrameAlignMode();
        BtnAlignAllCenter.Click   += (_, _) => AlignAllFramesCenter();
        BtnAlignAllBaseline.Click += (_, _) => AlignAllFramesBaseline();
        BtnAlignAllReset.Click    += (_, _) => ResetAllFrames();
        BtnAlignSelCenter.Click   += (_, _) => AlignSelectedFrame(center: true);
        BtnAlignSelBaseline.Click += (_, _) => AlignSelectedFrame(center: false);
        BtnAlignSelLayoutCenter.Click += (_, _) => AlignSelectedFrameToLayoutCenter();
        BtnAlignSelReset.Click    += (_, _) => ResetSelectedFrame();
        Editor.FrameSelected      += OnFrameSelected;
        Editor.FrameDragged       += OnFrameDragged;

        // Scheda Griglia (guida / slicing)
        BtnGenerateTemplate.Click += (_, _) => RunGenerateTemplate();
        BtnExportTemplate.Click   += async (_, _) => await ExportTemplateAsync();
        BtnImportFrames.Click     += async (_, _) => await RunImportFramesAsync();
        InitTemplateTab();

        // Passo 5 — Esporta
        BtnExportPng.Click       += async (_, _) => await ExportPngAsync();
        BtnExportJson.Click      += async (_, _) => await ExportJsonAsync();
        BtnExportTiled.Click     += async (_, _) => await ExportTiledMapJsonAsync();
        BtnExportFramesZip.Click += async (_, _) => await ExportFramesZipAsync();

        InitializeWorkspaceTabs();
        UpdatePivotLabels();
        UpdateUndoUi();
        UpdateWorkspaceGuidance();
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
            ChkShowAdvancedTabs.IsChecked == true,
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
        ChkShowAdvancedTabs.IsChecked = state.IsAdvancedMode;
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
            ChkSpriteCrop.IsChecked = false;
            _toolbarSelectionModeEnabled = false;
            Editor.IsSelectionMode = false;
        }
        Editor.IsPipetteMode = on;
        PipetteHintBar.IsVisible = on;
        TxtPipetteHint.Text = on
            ? "Clicca su un pixel dell'immagine per campionare il colore. Trascina oltre 4px per annullare. Tasto centrale o Ctrl+trascina: pan."
            : "";
    }

    private void UpdateSpriteSelectionMode()
    {
        var on = ChkSpriteCrop.IsChecked == true;
        if (on)
            ChkPipette.IsChecked = false;
        // In modalità ROI non distruttiva la selezione resta attiva
        Editor.IsSelectionMode = on || IsRoiSelectionModeRequested();
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
            CellList.ItemsSource = null;
            ChkSpriteCrop.IsChecked = false;
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
        var p = _document[e.X, e.Y];
        var hx = RgbaToHex(p);
        switch (CmbPipetteTarget.SelectedIndex)
        {
            case 0:
                TxtPipeChromaHex.Text = hx;
                break;
            case 1:
                TxtEdgeKeyHex.Text = hx;
                break;
            case 2:
                TxtPipeOutlineHex.Text = hx;
                break;
        }
        SetStatus($"Pipetta: {hx}  pixel ({e.X},{e.Y})  A={p.A}");
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

    private void UpdatePivotLabels()
    {
        LblPivotX.Text = $"{SliderPivotX.Value:F2}";
        LblPivotY.Text = $"{SliderPivotY.Value:F2}";
    }

    private void SetStatus(string message) =>
        TxtStatus.Text = string.IsNullOrEmpty(message) ? "" : $"[{DateTime.Now:HH:mm:ss}] {message}";

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
        CellList.ItemsSource = null;
        TxtGlobal.Text = "—  Esegui prima il passo 2";
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
        CellList.ItemsSource = null;
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
            TxtQuickColorsBefore.Text = "-";
            TxtQuickColorsAfter.Text = "-";
            TxtQuickPalette.Text = string.Empty;
            return;
        }

        var count = PixelArtValidation.CountUniqueColors(_document);
        TxtQuickColorsBefore.Text = count.ToString(CultureInfo.InvariantCulture);
        if (string.IsNullOrWhiteSpace(TxtQuickColorsAfter.Text) || TxtQuickColorsAfter.Text == "-")
            TxtQuickColorsAfter.Text = count.ToString(CultureInfo.InvariantCulture);

        var previewPalette = PaletteExtractor.Extract(_document, new PaletteExtractor.Options(Colors: 16));
        TxtQuickPalette.Text = string.Join(" ", previewPalette.Select(ToHexRgb));
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
        ExpPipelineTechnicalDetails.IsExpanded = false;
        UpdatePipelinePresetBadge();
        if (ChkPresetApplyNow.IsChecked == true)
            RunPixelPipeline();
    }

    private void ApplyAggressivePresetToControls()
    {
        _isApplyingPipelinePreset = true;
        try
        {
            _pipelineVm.ApplyAggressiveRecoverPreset();
            ApplyPipelineFormStateToControls(_pipelineVm.ToFormState());
        }
        finally
        {
            _isApplyingPipelinePreset = false;
        }
        SetStatus("Preset Aggressivo+Recupero impostato.");
        ExpPipelineTechnicalDetails.IsExpanded = false;
        UpdatePipelinePresetBadge();
        if (ChkPresetApplyNow.IsChecked == true)
            RunPixelPipeline();
    }

    private void RefreshCellList()
    {
        CellList.ItemsSource = _cells.Select(c =>
            $"{c.Id}  [{c.BoundsInAtlas.MinX},{c.BoundsInAtlas.MinY} → {c.BoundsInAtlas.Width}×{c.BoundsInAtlas.Height}]").ToList();
    }

    private async Task SaveSelectedFrameAsync()
    {
        if (_document is null)          { SetStatus("Nessuna immagine aperta."); return; }
        var idx = CellList.SelectedIndex;
        if (idx < 0 || idx >= _cells.Count)
        {
            SetStatus("Seleziona prima un frame dalla lista.");
            return;
        }

        var cell = _cells[idx];
        try
        {
            var suggested = $"{cell.Id}_{cell.BoundsInAtlas.Width}x{cell.BoundsInAtlas.Height}.png";
            var file = await StorageProvider.SaveFilePickerAsync(new FilePickerSaveOptions
            {
                Title            = $"Salva frame {cell.Id}",
                DefaultExtension = "png",
                FileTypeChoices  = [new FilePickerFileType("PNG") { Patterns = ["*.png"] }],
                SuggestedFileName = suggested,
            });
            if (file is null) return;

            using var crop = AtlasCropper.Crop(_document, cell.BoundsInAtlas);
            await using var stream = await file.OpenWriteAsync();
            crop.Save(stream, new PngEncoder());

            SetStatus($"Frame {cell.Id} salvato — {cell.BoundsInAtlas.Width}×{cell.BoundsInAtlas.Height} px.");
        }
        catch (Exception ex)
        {
            SetStatus($"Errore salvataggio frame: {ex.Message}");
        }
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
        if (string.IsNullOrWhiteSpace(formState.PaletteId))
            formState = formState with { PaletteId = TryResolvePaletteIdFromMetadata(formState.PaletteMetadataPath) ?? string.Empty };
        return _pipelineVm.TryBuildOptionsFromFormState(formState, includeOutline, out options, out error);
    }

    private void ExecutePipeline(PixelArtPipeline.Options options, string label)
    {
        if (_document is null) return;
        PushUndo();
        var result = PipelineExecutionService.RunInPlace(_document, options, label, ToHexRgb);
        TxtPipelineLastRun.Text = result.LastRunText;
        if (!result.Succeeded)
        {
            SetStatus(result.StatusText);
            return;
        }

        TxtQuickColorsBefore.Text = result.ColorsBeforeText;
        TxtQuickColorsAfter.Text = result.ColorsAfterText;
        TxtQuickPalette.Text = result.PaletteText;
        _cleanApplied = true;
        ClearSliceGrid();
        _cells.Clear();
        CellList.ItemsSource = null;
        RefreshView();
        SetStatus(result.StatusText);
    }

    private PipelineViewModel.PipelineFormState ReadPipelineFormStateFromControls()
    {
        return new PipelineViewModel.PipelineFormState(
            EnableChroma: IsChecked(ChkPipeChroma),
            EnableChromaSnapRgb: IsChecked(ChkPipeChromaSnapRgb),
            ChromaHex: TextOrDefault(TxtPipeChromaHex, "#00FF00"),
            ChromaTolerance: TextOrDefault(TxtPipeChromaTol, "0"),
            EnableAdvancedCleaner: IsChecked(ChkPipeAdvancedCleaner),
            BilateralSigmaSpatial: TextOrDefault(TxtPipeBilateralSpatial, "1.25"),
            BilateralSigmaRange: TextOrDefault(TxtPipeBilateralRange, "0.085"),
            BilateralPasses: TextOrDefault(TxtPipeBilateralPasses, "1"),
            EnablePixelGridEnforce: IsChecked(ChkPipePixelGridEnforce),
            NativeWidth: TextOrDefault(TxtPipeNativeW, "64"),
            NativeHeight: TextOrDefault(TxtPipeNativeH, "64"),
            EnablePaletteSnap: IsChecked(ChkPipePaletteSnap),
            PaletteId: TextOrDefault(TxtPipePaletteId, string.Empty),
            PaletteMetadataPath: TextOrDefault(TxtPipePaletteMetadataPath, string.Empty),
            EnableQuantize: IsChecked(ChkPipeQuant),
            MaxColors: TextOrDefault(TxtPipeQuantLevels, "16"),
            QuantizerIndex: CmbPipeQuantMethod.SelectedIndex,
            EnableMajorityDenoise: IsChecked(ChkPipeMajorityDenoise),
            MinIsland: TextOrDefault(TxtMinIsland, "2"),
            EnableOutline: IsChecked(ChkPipeOutline),
            OutlineHex: TextOrDefault(TxtPipeOutlineHex, "#000000"),
            EnableAlphaThreshold: IsChecked(ChkAlphaThreshold),
            AlphaThreshold: TextOrDefault(TxtAlphaThreshold, "128"));
    }

    private void ApplyPipelineFormStateToControls(PipelineViewModel.PipelineFormState formState)
    {
        SetChecked(ChkPipeChroma, formState.EnableChroma);
        SetChecked(ChkPipeChromaSnapRgb, formState.EnableChromaSnapRgb);
        SetText(TxtPipeChromaHex, formState.ChromaHex);
        SetText(TxtPipeChromaTol, formState.ChromaTolerance);
        SetChecked(ChkPipeAdvancedCleaner, formState.EnableAdvancedCleaner);
        SetText(TxtPipeBilateralSpatial, formState.BilateralSigmaSpatial);
        SetText(TxtPipeBilateralRange, formState.BilateralSigmaRange);
        SetText(TxtPipeBilateralPasses, formState.BilateralPasses);
        SetChecked(ChkPipePixelGridEnforce, formState.EnablePixelGridEnforce);
        SetText(TxtPipeNativeW, formState.NativeWidth);
        SetText(TxtPipeNativeH, formState.NativeHeight);
        SetChecked(ChkPipePaletteSnap, formState.EnablePaletteSnap);
        SetText(TxtPipePaletteId, formState.PaletteId);
        SetText(TxtPipePaletteMetadataPath, formState.PaletteMetadataPath);
        SetChecked(ChkPipeQuant, formState.EnableQuantize);
        SetText(TxtPipeQuantLevels, formState.MaxColors);
        CmbPipeQuantMethod.SelectedIndex = formState.QuantizerIndex;
        SetChecked(ChkPipeMajorityDenoise, formState.EnableMajorityDenoise);
        SetText(TxtMinIsland, formState.MinIsland);
        SetChecked(ChkPipeOutline, formState.EnableOutline);
        SetText(TxtPipeOutlineHex, formState.OutlineHex);
        SetChecked(ChkAlphaThreshold, formState.EnableAlphaThreshold);
        SetText(TxtAlphaThreshold, formState.AlphaThreshold);
    }

    private void HookPipelinePresetResetOnManualChanges()
    {
        foreach (var checkBox in new[]
                 {
                     ChkPipeChroma, ChkPipeChromaSnapRgb, ChkPipeQuant, ChkPipeMajorityDenoise,
                     ChkPipeOutline, ChkAlphaThreshold, ChkPipeAdvancedCleaner, ChkPipePixelGridEnforce,
                     ChkPipePaletteSnap
                 })
        {
            checkBox.IsCheckedChanged += (_, _) => ResetPresetOnManualPipelineEdit();
        }

        foreach (var textBox in new[]
                 {
                     TxtPipeChromaHex, TxtPipeChromaTol, TxtPipeBilateralSpatial, TxtPipeBilateralRange,
                     TxtPipeBilateralPasses, TxtPipeNativeW, TxtPipeNativeH, TxtPipePaletteId,
                     TxtPipePaletteMetadataPath, TxtPipeQuantLevels, TxtMinIsland, TxtPipeOutlineHex,
                     TxtAlphaThreshold
                 })
        {
            textBox.TextChanged += (_, _) => ResetPresetOnManualPipelineEdit();
        }

        CmbPipeQuantMethod.SelectionChanged += (_, _) => ResetPresetOnManualPipelineEdit();
    }

    private static bool IsChecked(Avalonia.Controls.CheckBox checkBox) => checkBox.IsChecked == true;

    private static void SetChecked(Avalonia.Controls.CheckBox checkBox, bool value) => checkBox.IsChecked = value;

    private static string TextOrDefault(Avalonia.Controls.TextBox textBox, string fallback) => textBox.Text ?? fallback;

    private static void SetText(Avalonia.Controls.TextBox textBox, string value) => textBox.Text = value;

    private static string? TryResolvePaletteIdFromMetadata(string? metadataPath)
    {
        if (string.IsNullOrWhiteSpace(metadataPath))
            return null;
        var path = metadataPath.Trim();
        if (!File.Exists(path))
            return null;
        try
        {
            var json = File.ReadAllText(path);
            var meta = JsonExport.Deserialize(json);
            return string.IsNullOrWhiteSpace(meta?.PaletteId) ? null : meta.PaletteId.Trim();
        }
        catch
        {
            return null;
        }
    }

    private void ResetPresetOnManualPipelineEdit()
    {
        if (_isApplyingPipelinePreset) return;
        _pipelineVm.ActivePreset = PipelineViewModel.PresetKind.None;
        ExpPipelineTechnicalDetails.IsExpanded = true;
        UpdatePipelinePresetBadge();
    }

    private void UpdatePipelinePresetBadge()
    {
        TxtPipelinePresetBadge.Text = _pipelineVm.ActivePreset switch
        {
            PipelineViewModel.PresetKind.Safe => "Preset attivo: Sicuro",
            PipelineViewModel.PresetKind.AggressiveRecover => "Preset attivo: Aggressivo + Recupero",
            _ => "Preset attivo: Personalizzato"
        };
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
            if (clearCells) { ClearSliceGrid(); _cells.Clear(); CellList.ItemsSource = null; }
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
            CellList.ItemsSource = null;
            RefreshView();
            SetStatus(successMsg);
        }
        catch (Exception ex) { SetStatus($"{errorPrefix}: {ex.Message}"); }
    }

    // ─────────────────────────────────────────────────────────────────────────

    private void RunNearestResize()
    {
        var tw = Math.Max(1, InputParsing.ParseInt(TxtNnW.Text, 64));
        var th = Math.Max(1, InputParsing.ParseInt(TxtNnH.Text, 64));
        RunReplaceTransform(
            src => NearestNeighborResize.Resize(src, tw, th, 0, 0),
            $"Immagine ridimensionata a {tw}×{th} px.",
            "Errore ridimensionamento");
    }

    private void RunEdgeBackground()
    {
        if (!InputParsing.TryParseHexRgb(TxtEdgeKeyHex.Text, out var key))
        {
            SetStatus("Edge BFS: key hex non valida.");
            return;
        }
        var tol = Math.Max(0, InputParsing.ParseInt(TxtEdgeTol.Text, 8));
        RunTransform(
            img => EdgeBackgroundFill.ApplyInPlace(img, key, tol),
            "Sfondo rimosso dal bordo dell'immagine.",
            clearCells: true,
            "Errore edge BFS");
    }

    private async Task ExportTiledMapJsonAsync()
    {
        if (_document is null) return;
        try
        {
            var cells = _cells.Count > 0 ? _cells : new List<SpriteCell> { new("full", new AxisAlignedBox(0, 0, _document.Width, _document.Height)) };
            var tileW = cells[0].BoundsInAtlas.Width;
            var tileH = cells[0].BoundsInAtlas.Height;
            var mapCols = _document.Width / Math.Max(1, tileW);
            var mapRows = _document.Height / Math.Max(1, tileH);
            var json = TiledMapJson.BuildFromCells(mapCols, mapRows, tileW, tileH, cells, "atlas.png");
            var file = await StorageProvider.SaveFilePickerAsync(new FilePickerSaveOptions
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
            SetStatus($"Mappa Tiled salvata ({cells.Count} tile, {mapCols}×{mapRows}). Posiziona 'atlas.png' nella stessa cartella.");
        }
        catch (Exception ex)
        {
            SetStatus($"Errore export Tiled: {ex.Message}");
        }
    }

    private async Task ExportFramesZipAsync()
    {
        if (_document is null) return;
        var list = new List<(string name, Image<Rgba32> img)>();
        try
        {
            if (_cells.Count == 0)
            {
                var copy = _document.Clone();
                list.Add(("frame0.png", copy));
            }
            else
            {
                for (var i = 0; i < _cells.Count; i++)
                {
                    var c = _cells[i];
                    var id = string.IsNullOrEmpty(c.Id) ? "cell" : SanitizeFileSegment(c.Id);
                    var name = $"{i:000}_{id}.png";
                    var crop = AtlasCropper.Crop(_document, c.BoundsInAtlas);
                    if (crop.Width == 0) continue;
                    list.Add((name, crop));
                }
            }
            if (list.Count == 0)
            {
                SetStatus("Nessun frame da esportare nello ZIP.");
                return;
            }
            var file = await StorageProvider.SaveFilePickerAsync(new FilePickerSaveOptions
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
            SetStatus($"ZIP creato: {list.Count} frame.");
        }
        catch (Exception ex)
        {
            SetStatus($"Errore export ZIP: {ex.Message}");
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
        BtnPasteEnter.IsVisible = true;
        PasteActiveBar.IsVisible = false;
        Editor.IsCellClickMode = false;
        UpdatePasteBufferStatus();

        ExitFrameAlignMode();

        _lastUserRoi = null;
        _frameDragInitialOffset = null;

        _toolbarSelectionModeEnabled = false;
        ChkPipette.IsChecked = false;
        ChkEraser.IsChecked = false;
        ChkSpriteCrop.IsChecked = false;
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
        var clipboard = TopLevel.GetTopLevel(this)?.Clipboard;
        if (clipboard is null)
        {
            SetStatus("Appunti non disponibili.");
            return;
        }

        if (_document is null)
        {
            SetStatus("Apri prima un'immagine.");
            return;
        }

        Image<Rgba32>? cropped = null;
        Bitmap? bmp = null;
        try
        {
            Image<Rgba32> pixelsSource = _document;
            string detail;

            if (_activeSelectionBox is not null)
            {
                var box = _activeSelectionBox.Value;
                var minX = Math.Clamp(box.MinX, 0, _document.Width);
                var maxX = Math.Clamp(box.MaxX, 0, _document.Width);
                var minY = Math.Clamp(box.MinY, 0, _document.Height);
                var maxY = Math.Clamp(box.MaxY, 0, _document.Height);
                if (minX >= maxX || minY >= maxY)
                {
                    SetStatus("Selezione non valida per la copia.");
                    return;
                }

                var clamped = new AxisAlignedBox(minX, minY, maxX, maxY);
                cropped = AtlasCropper.Crop(_document, in clamped);
                pixelsSource = cropped;
                detail = $"{cropped.Width}×{cropped.Height} px (selezione ROI)";
            }
            else
                detail = $"{_document.Width}×{_document.Height} px";

            bmp = Rgba32BitmapBridge.ToBitmap(pixelsSource);
            if (bmp is null)
            {
                SetStatus("Impossibile preparare l'immagine per gli appunti.");
                return;
            }

            await ClipboardBitmapInterop.SetBitmapAndFlushAsync(clipboard, bmp).ConfigureAwait(true);
            SetStatus($"Copiato negli appunti: {detail}.");
        }
        catch (Exception ex)
        {
            SetStatus($"Copia negli appunti: {ex.Message}");
        }
        finally
        {
            cropped?.Dispose();
            bmp?.Dispose();
        }
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
            CellList.ItemsSource = null;
            // TxtAabb rimosso
            TxtGlobal.Text = "—  Esegui prima il passo 2";
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
            _workspaceTabs.MarkActiveClean();
            RefreshWorkspaceChrome();
            SetStatus($"Immagine aperta: {_document.Width}×{_document.Height} px. Scheda «Immagine» o «Sprite».");
        }
        catch (Exception ex)
        {
            SetStatus($"Errore apertura immagine: {ex.Message}");
        }
    }

    private void RunDenoise()
    {
        var minA = Math.Max(1, InputParsing.ParseInt(TxtMinIsland.Text, 2));
        RunTransform(
            img => IslandDenoise.ApplyInPlace(img, new IslandDenoise.Options(1, minA)),
            $"Pixel isolati rimossi (soglia: {minA} px).",
            clearCells: true,
            "Errore denoise");
    }

    private void RunGridSlice()
    {
        if (_document is null) return;
        try
        {
            var rows = Math.Max(1, InputParsing.ParseInt(TxtRows.Text, 2));
            var cols = Math.Max(1, InputParsing.ParseInt(TxtCols.Text, 2));
            PushUndo();
            _cells = GridSlicer.Slice(_document.Width, _document.Height, rows, cols).ToList();
            Editor.SliceGridRows = rows;
            Editor.SliceGridCols = cols;
            Editor.SpriteCells = [];
            RefreshCellList();
            UpdateWorkspaceGuidance();
            SetStatus($"Diviso in griglia {rows}×{cols}: trovati {_cells.Count} sprite.");
        }
        catch (Exception ex)
        {
            SetStatus($"Errore grid slice: {ex.Message}");
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
            WorkflowShellViewModel.WorkflowStep.Pulisci => 1,
            WorkflowShellViewModel.WorkflowStep.SliceAllinea => 2,
            WorkflowShellViewModel.WorkflowStep.Esporta => 0,
            _ => 0
        };
        UpdateWorkflowShell();
    }

    private void UpdateWorkspaceGuidance()
    {
        if (_document is null)
        {
            _workflowShell.UpdateFromReadiness(hasDocument: false, cleanApplied: false, hasCells: false);
            SetWorkspaceBadge("BLOCCATO", "#3f1f24", "#6a2f39", "#ffd9df");
            TxtWorkspaceDependencyStatus.Text = "Manca immagine sorgente. Apri un file per iniziare.";
            BtnWorkspaceGuideAction.Content = "Step 1 · Importa";
            UpdateWorkflowShell();
            return;
        }

        if (!_cleanApplied)
        {
            _workflowShell.UpdateFromReadiness(hasDocument: true, cleanApplied: false, hasCells: false);
            SetWorkspaceBadge("IN CORSO", "#203047", "#2e4a6f", "#d4e7ff");
            TxtWorkspaceDependencyStatus.Text = "Immagine caricata. Applica una pulizia rapida prima del slicing.";
            BtnWorkspaceGuideAction.Content = "Step 2 · Pulisci";
            UpdateWorkflowShell();
            return;
        }

        if (_cells.Count == 0)
        {
            _workflowShell.UpdateFromReadiness(hasDocument: true, cleanApplied: true, hasCells: false);
            SetWorkspaceBadge("ATTENZIONE", "#3d3318", "#6d5922", "#ffe8b2");
            TxtWorkspaceDependencyStatus.Text = "Manca slicing: rileva o crea celle sprite prima dell'export frame-based.";
            BtnWorkspaceGuideAction.Content = "Step 3 · Slice/Allinea";
            UpdateWorkflowShell();
            return;
        }

        _workflowShell.UpdateFromReadiness(hasDocument: true, cleanApplied: true, hasCells: true);
        SetWorkspaceBadge("PRONTO", "#173927", "#266344", "#c9f5dd");
        TxtWorkspaceDependencyStatus.Text = $"Slicing pronto: {_cells.Count} celle disponibili. Puoi esportare subito.";
        BtnWorkspaceGuideAction.Content = "Step 4 · Esporta";
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

    private void SetWorkspaceBadge(string text, string bgHex, string borderHex, string fgHex)
    {
        TxtWorkspaceDependencyBadge.Text = text;
        WorkspaceDependencyBadge.Background = new Avalonia.Media.SolidColorBrush(Avalonia.Media.Color.Parse(bgHex));
        WorkspaceDependencyBadge.BorderBrush = new Avalonia.Media.SolidColorBrush(Avalonia.Media.Color.Parse(borderHex));
        TxtWorkspaceDependencyBadge.Foreground = new Avalonia.Media.SolidColorBrush(Avalonia.Media.Color.Parse(fgHex));
    }

    private void RunCcl()
    {
        if (_document is null) return;
        try
        {
            PushUndo();
            _cells = CclAutoSlicer.Slice(_document).ToList();
            ClearSliceGrid();
            RefreshCellList();
            Editor.SpriteCells = _cells;
            UpdateWorkspaceGuidance();
            SetStatus($"Rilevamento automatico completato: trovati {_cells.Count} sprite.");
        }
        catch (Exception ex)
        {
            SetStatus($"Errore CCL: {ex.Message}");
        }
    }

    private void RunGlobalScan()
    {
        if (_document is null || _cells.Count == 0)
        {
            TxtGlobal.Text = "—  Esegui prima il passo 2 (dividi in sprite)";
            SetStatus("Nessun sprite trovato: usa 'Rileva sprite' nella toolbar o 'Dividi a griglia' nel tab Dividi.");
            return;
        }
        try
        {
            // Statistiche complete (max / mediana / p90 / outlier) sulle dimensioni cella
            var cellBoxes = _cells.Select(c => c.BoundsInAtlas).ToList();
            var stats = FrameStatistics.Compute(cellBoxes);
            var warning = FrameStatistics.FormatOutlierWarning(stats);

            TxtGlobal.Text =
                $"Max: {stats.MaxW}×{stats.MaxH} · Mediana: {stats.MedianW}×{stats.MedianH} · " +
                $"P90: {stats.Percentile90W}×{stats.Percentile90H}" +
                (warning is null ? "" : $"\n{warning}");
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
            var policy = CmbNormalizePolicy.SelectedIndex switch
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
            TxtGlobal.Text = $"Normalizzato: ogni cella {result.Atlas.Width / _cells.Count}×{result.Atlas.Height} px";
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
            var snap = ChkAlignCenterSnapGrid.IsChecked == true ? 8 : 0;
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
            var mode = CmbCropMode.SelectedIndex switch
            {
                1 => CropPipeline.CropMode.TrimToContentPadded,
                2 => CropPipeline.CropMode.UserRoi,
                _ => CropPipeline.CropMode.TrimToContent,
            };
            var pot = CmbCropPot.SelectedIndex switch
            {
                1 => CropPipeline.PotPolicy.PerAxis,
                2 => CropPipeline.PotPolicy.Square,
                _ => CropPipeline.PotPolicy.None,
            };
            var alpha    = (byte)Math.Clamp((int)(TxtCropAlpha.Value   ?? 1m), 1, 255);
            var padding  = Math.Clamp((int)(TxtCropPadding.Value ?? 4m), 0, 64);

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
            CellList.ItemsSource = null;
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

            // Legge i parametri attuali dall'UI dei pannelli sottostanti
            InputParsing.TryParseHexRgb(TxtEdgeKeyHex.Text, out var bgKey);
            var bgTol = Math.Max(0, InputParsing.ParseInt(TxtEdgeTol.Text, 8));
            var alphaThr = (byte)Math.Clamp(InputParsing.ParseInt(TxtAlphaThreshold.Text, 128), 0, 255);
            var defOpaque = (byte)Math.Clamp(InputParsing.ParseInt(TxtDefringeOpaque.Text, 250), 1, 255);
            var minIsland = Math.Max(1, InputParsing.ParseInt(TxtMinIsland.Text, 4));
            var palColors = Math.Clamp(InputParsing.ParseInt(TxtPaletteColors.Text, 16), 2, 64);

            var report = AiCleanupWizard.Apply(_document, new AiCleanupWizard.Options
            {
                RemoveBgColor   = true,
                BgKey           = bgKey,
                BgTolerance     = bgTol,
                DefringeEdges   = true,
                DefringeOpaque  = defOpaque,
                BinarizeAlpha   = true,
                AlphaThreshold  = alphaThr,
                DenoiseSpike    = true,
                DenoiseIslands  = true,
                IslandMinSize   = minIsland,
                ReducePalette   = ChkWizardPaletteReduce.IsChecked == true,
                PaletteColors   = palColors,
                PaletteDither   = ChkPaletteDither.IsChecked == true,
            });

            ClearSliceGrid();
            _cells.Clear();
            CellList.ItemsSource = null;
            RefreshView();
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
            var opaque = (byte)Math.Clamp(InputParsing.ParseInt(TxtDefringeOpaque.Text, 250), 1, 255);
            // Pre-flight: defringe agisce solo su pixel semi-trasparenti (0 < α < opaque).
            // Se non ce ne sono, è no-op → avvisa l'utente esplicitamente.
            var semiCount = ImageUtils.CountSemiTransparent(_document, opaque);
            if (semiCount == 0)
            {
                SetStatus($"Defringe: nessun pixel semi-trasparente (con α tra 1 e {opaque - 1}). " +
                          "Il filtro è no-op su immagini completamente opache.");
                return;
            }
            PushUndo();
            Defringe.FromOpaqueNeighbors(_document, opaque);
            RefreshView();
            SetStatus($"Defringe applicato: {semiCount:N0} pixel di edge ricolorati (soglia opaca {opaque}).");
        }
        catch (Exception ex)
        {
            SetStatus($"Errore defringe: {ex.Message}");
        }
    }

    private void RunMedianFilter() =>
        RunTransform(MedianFilter.ApplyInPlace, "Filtro mediano 3×3 applicato.",
                     clearCells: false, "Errore median filter");

    private void RunPaletteReduce()
    {
        if (_document is null) { SetStatus("Nessuna immagine aperta."); return; }
        try
        {
            var presetIdx = CmbPalettePreset.SelectedIndex;
            IReadOnlyList<Rgba32> palette;
            string label;

            if (presetIdx <= 0) // Auto AI (K-Means)
            {
                var n = Math.Clamp(InputParsing.ParseInt(TxtPaletteColors.Text, 16), 2, 64);
                palette = PaletteExtractor.Extract(_document, new PaletteExtractor.Options(Colors: n));
                if (palette.Count == 0) { SetStatus("Nessun colore opaco trovato."); return; }
                label = $"Auto AI {palette.Count}";
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
            var dither = ChkPaletteDither.IsChecked == true
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

    private void RunMakeTileable()
    {
        var blend = Math.Clamp(InputParsing.ParseInt(TxtSeamlessBlend.Text, 4), 1, 16);
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
            var m = Math.Max(2, InputParsing.ParseInt(TxtPadMultiple.Text, 16));
            PushUndo();
            var padded = AutoPad.PadToMultiple(_document, m);
            _document.Dispose();
            _document = padded;
            ClearSliceGrid();
            _cells.Clear();
            CellList.ItemsSource = null;
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
        if (_document is null) return;
        try
        {
            var useCustomCell = ChkExportCustomCellSize.IsChecked == true;
            var keepUniformCell = ChkExportKeepCellSize.IsChecked == true || useCustomCell;
            int? customW = null;
            int? customH = null;
            if (useCustomCell)
            {
                customW = Math.Max(1, InputParsing.ParseInt(TxtExportCellW.Text, 208));
                customH = Math.Max(1, InputParsing.ParseInt(TxtExportCellH.Text, 256));
            }
            using var layout = ExportLayoutBuilder.Build(
                _document,
                _cells,
                SliderPivotX.Value,
                SliderPivotY.Value,
                keepUniformCell,
                customW,
                customH);
            if (layout is null)
            {
                SetStatus("Niente da esportare.");
                return;
            }

            var file = await StorageProvider.SaveFilePickerAsync(new FilePickerSaveOptions
            {
                Title = "Salva atlas PNG",
                DefaultExtension = "png",
                FileTypeChoices = [new FilePickerFileType("PNG") { Patterns = ["*.png"] }],
                SuggestedFileName = "atlas.png"
            });
            if (file is null) return;
            await using (var s = await file.OpenWriteAsync())
            {
                if (ChkExportAtlasIndexedPng.IsChecked == true)
                    IndexedPngExporter.SaveWithWuQuantize(layout.Pack.Atlas, s);
                else
                    layout.Pack.Atlas.Save(s, new PngEncoder());
            }
            var mode = ChkExportAtlasIndexedPng.IsChecked == true ? " (palette 8-bit)" : "";
            var layoutMode = useCustomCell
                ? $" · uniforme custom {customW}×{customH}"
                : keepUniformCell ? " · celle uniformi (auto)" : " · compatto";
            SetStatus($"Atlas PNG salvato{mode}{layoutMode} ({layout.Pack.Placements.Count} sprite).");
        }
        catch (Exception ex)
        {
            SetStatus($"Errore export PNG: {ex.Message}");
        }
    }

    private async Task ExportJsonAsync()
    {
        if (_document is null) return;
        var items = new List<(string id, Image<Rgba32> img)>();
        try
        {
            var useCustomCell = ChkExportCustomCellSize.IsChecked == true;
            var keepUniformCell = ChkExportKeepCellSize.IsChecked == true || useCustomCell;
            int? customW = null;
            int? customH = null;
            if (useCustomCell)
            {
                customW = Math.Max(1, InputParsing.ParseInt(TxtExportCellW.Text, 208));
                customH = Math.Max(1, InputParsing.ParseInt(TxtExportCellH.Text, 256));
            }
            using var layout = ExportLayoutBuilder.Build(
                _document,
                _cells,
                SliderPivotX.Value,
                SliderPivotY.Value,
                keepUniformCell,
                customW,
                customH);
            if (layout is null)
            {
                SetStatus("Niente da esportare.");
                return;
            }

            var meta = new SpriteSheetMetadata
            {
                PaletteId = string.IsNullOrWhiteSpace(TxtPipePaletteId.Text) ? null : TxtPipePaletteId.Text.Trim(),
                Cells = layout.Entries.ToList()
            };
            var json = JsonExport.Serialize(meta);

            var file = await StorageProvider.SaveFilePickerAsync(new FilePickerSaveOptions
            {
                Title = "Salva metadati JSON",
                DefaultExtension = "json",
                FileTypeChoices = [new FilePickerFileType("JSON") { Patterns = ["*.json"] }],
                SuggestedFileName = "spritesheet.json"
            });
            if (file is null) return;
            await using (var s = await file.OpenWriteAsync())
            {
                using var w = new StreamWriter(s);
                await w.WriteAsync(json);
            }
            SetStatus($"JSON salvato ({layout.Entries.Count} celle).");
        }
        catch (Exception ex)
        {
            SetStatus($"Errore export JSON: {ex.Message}");
        }
        finally
        {
            foreach (var t in items) t.img.Dispose();
        }
    }

    // ─── Gomma ───────────────────────────────────────────────────────────────

    private bool _eraserUndoPushed;
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

        var r    = e.Radius;
        var imgW = _document.Width;
        var imgH = _document.Height;

        _document.ProcessPixelRows(accessor =>
        {
            for (var dy = -r; dy <= r; dy++)
            {
                var py = e.ImageY + dy;
                if (py < 0 || py >= imgH) continue;
                var row = accessor.GetRowSpan(py);
                for (var dx = -r; dx <= r; dx++)
                {
                    if (dx * dx + dy * dy > r * r) continue;
                    var px = e.ImageX + dx;
                    if (px < 0 || px >= imgW) continue;
                    if (row[px].A != 0)
                        row[px].A = 0;
                }
            }
        });

        var yMin = Math.Max(0, e.ImageY - r);
        var yMax = Math.Min(imgH, e.ImageY + r + 1);
        _eraserDirtyMinY = Math.Min(_eraserDirtyMinY, yMin);
        _eraserDirtyMaxY = Math.Max(_eraserDirtyMaxY, yMax);

        var now = Environment.TickCount64;
        if (now - _eraserLastFlushTicks >= 16 && _eraserDirtyMinY < _eraserDirtyMaxY)
        {
            Editor.UpdateBitmapRegion(_document, _eraserDirtyMinY, _eraserDirtyMaxY);
            _eraserLastFlushTicks = now;
            _eraserDirtyMinY = int.MaxValue;
            _eraserDirtyMaxY = int.MinValue;
        }
    }

    private void OnEraserStrokeEnded(object? sender, EventArgs e)
    {
        if (_document is not null && _eraserDirtyMinY < _eraserDirtyMaxY)
            Editor.UpdateBitmapRegion(_document, _eraserDirtyMinY, _eraserDirtyMaxY);

        _eraserDirtyMinY = int.MaxValue;
        _eraserDirtyMaxY = int.MinValue;
        _eraserUndoPushed = false;
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

            BtnPasteEnter.IsVisible = false;
            PasteActiveBar.IsVisible = true;

            // Default: vista destinazione, click su cella per incollare
            BtnPasteDest.IsChecked = true;
            BtnPasteSrc.IsChecked  = false;
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
        BtnPasteDest.IsChecked = !toSource;
        BtnPasteSrc.IsChecked  =  toSource;

        if (toSource)
        {
            // Mostra l'atlas originale, abilita selezione rettangolo
            Editor.SetSourceImage(_pasteSource);
            Editor.IsSelectionMode = true;
            Editor.IsCellClickMode = false;
            Editor.SpriteCells = [];                 // niente overlay celle sulla sorgente
            ChkSpriteCrop.IsChecked = false;         // evita conflitto con il crop manuale
            SetStatus("Vista SORGENTE: trascina un rettangolo per selezionare uno sprite, poi 'Copia'.");
        }
        else
        {
            // Mostra l'atlas pulito (destinazione), abilita click-su-cella per incollare
            Editor.SetSourceImage(_document);
            Editor.IsSelectionMode = false;
            Editor.IsCellClickMode = true;
            Editor.SpriteCells = _cells;             // mostra griglia di destinazione
            ChkSpriteCrop.IsChecked = false;
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
            _document.Mutate(ctx => ctx.DrawImage(_pasteBuffer, new Point(destX, destY), 1f));

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
        BtnPasteEnter.IsVisible = true;
        PasteActiveBar.IsVisible = false;
        Editor.IsSelectionMode = false;
        Editor.IsCellClickMode = false;
        // Ripristina la vista sull'atlas pulito che è ora _document
        RefreshView();
        SetStatus("Modalità atlas pulito disattivata. L'atlas pulito è ora il documento attivo (esportabile).");
    }

    private void UpdatePasteBufferStatus()
    {
        TxtPasteBufferStatus.Text = _pasteBuffer is null
            ? "Buffer: vuoto"
            : $"Buffer: {_pasteBuffer.Width}×{_pasteBuffer.Height} px";
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
            var padding = (int)(TxtAlignPadding.Value ?? 0m);
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
            BtnAlignEnter.IsVisible = false;
            AlignActiveBar.IsVisible = true;
            Editor.SpriteCells = [];
            ClearSliceGrid();
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
        BtnAlignEnter.IsVisible = true;
        AlignActiveBar.IsVisible = false;
        TxtAlignSelected.Text = "Nessuno selezionato";
        Editor.SpriteCells = _cells;
    }

    private void AlignAllFramesCenter()
    {
        if (!EnsureFrameWorkbenchActive()) return;
        var snap = ChkAlignCenterSnapGrid.IsChecked == true ? 8 : 0;
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
        var snap = ChkAlignCenterSnapGrid.IsChecked == true ? 8 : 0;
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
        TxtAlignSelected.Text = $"Frame {idx}: {f.Cell.Width}×{f.Cell.Height} px · offset ({f.Offset.X}, {f.Offset.Y})";
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
            TxtAlignSelected.Text = $"Frame {e.FrameIndex}: {f.Cell.Width}×{f.Cell.Height} px · offset ({f.Offset.X}, {f.Offset.Y})";
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

    private Image<Rgba32>? _templateDocument;

    private void InitTemplateTab()
    {
        // Preset dimensioni cella
        TplPreset16 .Click += (_, _) => SetTilePreset(16,  16);
        TplPreset24 .Click += (_, _) => SetTilePreset(24,  24);
        TplPreset32 .Click += (_, _) => SetTilePreset(32,  32);
        TplPreset48 .Click += (_, _) => SetTilePreset(48,  48);
        TplPreset64 .Click += (_, _) => SetTilePreset(64,  64);
        TplPreset128.Click += (_, _) => SetTilePreset(128, 128);
        TplPreset256.Click += (_, _) => SetTilePreset(256, 256);

        // Aggiorna info-label al cambiare di cols/rows/w/h
        void refresh(object? s, EventArgs e) => UpdateTplInfoLabel();
        TplCols.ValueChanged  += refresh;
        TplRows.ValueChanged  += refresh;
        TplCellW.ValueChanged += refresh;
        TplCellH.ValueChanged += refresh;

        // Selettore pivot 3×3 → aggiorna etichetta + mostra custom XY se necessario
        void pivotChanged(object? s, EventArgs e) => UpdateTplPivotLabel();
        TplPivotTL.IsCheckedChanged += pivotChanged;
        TplPivotTC.IsCheckedChanged += pivotChanged;
        TplPivotTR.IsCheckedChanged += pivotChanged;
        TplPivotML.IsCheckedChanged += pivotChanged;
        TplPivotCC.IsCheckedChanged += pivotChanged;
        TplPivotMR.IsCheckedChanged += pivotChanged;
        TplPivotBL.IsCheckedChanged += pivotChanged;
        TplPivotBC.IsCheckedChanged += pivotChanged;
        TplPivotBR.IsCheckedChanged += pivotChanged;

        UpdateTplInfoLabel();
        UpdateTplPivotLabel();
    }

    private void SetTilePreset(int w, int h)
    {
        TplCellW.Value = w;
        TplCellH.Value = h;
    }

    private void UpdateTplInfoLabel()
    {
        var cols = (int)(TplCols.Value ?? 4);
        var rows = (int)(TplRows.Value ?? 4);
        var cw   = (int)(TplCellW.Value ?? 64);
        var ch   = (int)(TplCellH.Value ?? 64);
        TplInfoLabel.Text = $"{cols * rows} frame · {cols * cw}×{rows * ch} px";
    }

    private GridTemplateGenerator.PivotPreset ReadTplPivotPreset()
    {
        if (TplPivotTL.IsChecked == true) return GridTemplateGenerator.PivotPreset.TopLeft;
        if (TplPivotTC.IsChecked == true) return GridTemplateGenerator.PivotPreset.TopCenter;
        if (TplPivotTR.IsChecked == true) return GridTemplateGenerator.PivotPreset.TopRight;
        if (TplPivotML.IsChecked == true) return GridTemplateGenerator.PivotPreset.MidLeft;
        if (TplPivotCC.IsChecked == true) return GridTemplateGenerator.PivotPreset.Center;
        if (TplPivotMR.IsChecked == true) return GridTemplateGenerator.PivotPreset.MidRight;
        if (TplPivotBL.IsChecked == true) return GridTemplateGenerator.PivotPreset.BottomLeft;
        if (TplPivotBC.IsChecked == true) return GridTemplateGenerator.PivotPreset.BottomCenter;
        if (TplPivotBR.IsChecked == true) return GridTemplateGenerator.PivotPreset.BottomRight;
        return GridTemplateGenerator.PivotPreset.Custom;
    }

    private void UpdateTplPivotLabel()
    {
        var preset = ReadTplPivotPreset();
        var (nx, ny) = GridTemplateGenerator.PivotNdc(preset);

        TplPivotLabel.Text  = preset.ToString().Replace("Mid", "Mid ").Replace("Bottom", "Bottom ");
        TplPivotCoords.Text = $"x={nx:F2} · y={ny:F2}";
        TplCustomXYPanel.IsVisible = preset == GridTemplateGenerator.PivotPreset.Custom;
    }

    private GridTemplateGenerator.Options BuildTemplateOptions()
    {
        var preset = ReadTplPivotPreset();
        var (ndcX, ndcY) = preset == GridTemplateGenerator.PivotPreset.Custom
            ? ((double)(TplPivotX.Value ?? 0.5m), (double)(TplPivotY.Value ?? 0.5m))
            : GridTemplateGenerator.PivotNdc(preset);

        return new GridTemplateGenerator.Options
        {
            Rows             = (int)(TplRows.Value  ?? 4),
            Cols             = (int)(TplCols.Value  ?? 4),
            CellWidth        = (int)(TplCellW.Value ?? 64),
            CellHeight       = (int)(TplCellH.Value ?? 64),
            ShowBorder       = TplShowBorder.IsChecked   == true,
            BorderThickness  = 1,
            ShowPivotMarker  = TplShowPivot.IsChecked    == true,
            ShowBaselineLine = TplShowBaseline.IsChecked == true,
            ShowCellIndex    = TplShowIndex.IsChecked    == true,
            ShowCellTint     = TplShowTint.IsChecked     == true,
            Pivot            = preset,
            PivotNdcX        = ndcX,
            PivotNdcY        = ndcY,
        };
    }

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
        MainTabs.SelectedIndex == SelectionCanvasTabIndex || _toolbarSelectionModeEnabled;

    private void ApplyEditorSelectionMode()
    {
        Editor.IsSelectionMode = ChkSpriteCrop.IsChecked == true || IsRoiSelectionModeRequested();
    }

    private void EnterSelectionCanvas(bool enter)
    {
        if (enter)
        {
            // Disattiva modalità conflittuali
            ChkSpriteCrop.IsChecked = false;
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
        if (_document is null) { SetStatus("Nessuna immagine aperta."); return; }
        _activeSelectionBox = new AxisAlignedBox(0, 0, _document.Width, _document.Height);
        Editor.SetCommittedSelection(_activeSelectionBox);
        UpdateSelectionInfo();
        SetStatus($"Selezione intera immagine: {_document.Width}×{_document.Height} px.");
    }

    private void ClearSelection()
    {
        _activeSelectionBox = null;
        Editor.SetCommittedSelection(null);
        UpdateSelectionInfo();
        SetStatus("Selezione rimossa.");
    }

    private async Task ExportSelectionAsync()
    {
        if (_document is null)         { SetStatus("Nessuna immagine aperta."); return; }
        if (_activeSelectionBox is null){ SetStatus("Nessuna selezione attiva. Trascina sull'immagine."); return; }
        try
        {
            var box       = _activeSelectionBox.Value;
            var suggested = $"selezione_{box.Width}x{box.Height}.png";
            var file = await StorageProvider.SaveFilePickerAsync(new FilePickerSaveOptions
            {
                Title            = "Salva area selezionata come PNG",
                DefaultExtension = "png",
                FileTypeChoices  = [new FilePickerFileType("PNG") { Patterns = ["*.png"] }],
                SuggestedFileName = suggested,
            });
            if (file is null) return;

            using var crop = AtlasCropper.Crop(_document, box);
            await using var stream = await file.OpenWriteAsync();
            crop.Save(stream, new SixLabors.ImageSharp.Formats.Png.PngEncoder());
            SetStatus($"Area esportata: {box.Width}×{box.Height} px — ({box.MinX},{box.MinY}).");
        }
        catch (Exception ex)
        {
            SetStatus($"Errore esportazione selezione: {ex.Message}");
        }
    }

    private void CropToSelection()
    {
        if (_document is null)          { SetStatus("Nessuna immagine aperta."); return; }
        if (_activeSelectionBox is null) { SetStatus("Nessuna selezione attiva."); return; }
        try
        {
            PushUndo();
            var box     = _activeSelectionBox.Value;
            var cropped = AtlasCropper.Crop(_document, box);
            _document.Dispose();
            _document = cropped;
            _cells.Clear();
            ClearSliceGrid();
            CellList.ItemsSource = null;
            _activeSelectionBox  = null;
            Editor.SetCommittedSelection(null);
            UpdateSelectionInfo();
            RefreshView();
            SetStatus($"Immagine ritagliata alla selezione: {_document.Width}×{_document.Height} px. (Ctrl+Z per annullare)");
        }
        catch (Exception ex)
        {
            SetStatus($"Errore ritaglio selezione: {ex.Message}");
        }
    }

    private void RemoveSelectedArea()
    {
        if (_document is null) { SetStatus("Nessuna immagine aperta."); return; }
        if (_activeSelectionBox is null) { SetStatus("Nessuna selezione attiva da rimuovere."); return; }
        try
        {
            var box = _activeSelectionBox.Value;
            var minX = Math.Clamp(box.MinX, 0, _document.Width);
            var maxX = Math.Clamp(box.MaxX, 0, _document.Width);
            var minY = Math.Clamp(box.MinY, 0, _document.Height);
            var maxY = Math.Clamp(box.MaxY, 0, _document.Height);
            if (minX >= maxX || minY >= maxY)
            {
                SetStatus("Area selezionata non valida.");
                return;
            }

            PushUndo();
            _document.ProcessPixelRows(accessor =>
            {
                for (var y = minY; y < maxY; y++)
                {
                    var row = accessor.GetRowSpan(y);
                    for (var x = minX; x < maxX; x++)
                        row[x] = new Rgba32(0, 0, 0, 0);
                }
            });

            RefreshView();
            SetStatus($"Area selezionata rimossa: {maxX - minX}×{maxY - minY} px.");
        }
        catch (Exception ex)
        {
            SetStatus($"Errore rimozione area selezionata: {ex.Message}");
        }
    }

    private void UpdateSelectionInfo()
    {
        // I controlli potrebbero non essere ancora inizializzati durante il caricamento
        if (TxtSelectionInfo is null) return;

        if (_activeSelectionBox is null)
        {
            TxtSelectionInfo.Text        = "Nessuna selezione.\nTrascina sull'immagine per iniziare.";
            BtnExportSelection.IsEnabled = false;
            BtnCropToSelection.IsEnabled = false;
            return;
        }
        var b = _activeSelectionBox.Value;
        // MaxX/MaxY sono esclusivi (half-open); ultimo pixel incluso è Max−1.
        TxtSelectionInfo.Text =
            $"Origine (↖): ({b.MinX}, {b.MinY}) px\n" +
            $"Dimensione: {b.Width} × {b.Height} px\n" +
            $"Ultimo pixel incluso (↘): ({b.MaxX - 1}, {b.MaxY - 1})";
        BtnExportSelection.IsEnabled = true;
        BtnCropToSelection.IsEnabled = true;
    }

    private bool _importInProgress;

    private async Task RunImportFramesAsync()
    {
        if (_importInProgress) { SetStatus("Importazione già in corso…"); return; }
        _importInProgress = true;
        BtnImportFrames.IsEnabled = false;
        try
        {
            var cols  = (int)(TplCols.Value  ?? 4);
            var rows  = (int)(TplRows.Value  ?? 1);
            var cellW = (int)(TplCellW.Value ?? 64);
            var cellH = (int)(TplCellH.Value ?? 64);
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

                atlas.Mutate(ctx => ctx.DrawImage(frame, new Point(cellX + dx, cellY + dy), 1f));
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
            SetStatus($"Importati {sorted.Count}/{total} frame — atlas {atlas.Width}×{atlas.Height} px. " +
                      "Usa i tab 3-4 per rifinire, poi Laboratorio > Animazione per la preview.");
        }
        catch (Exception ex)
        {
            SetStatus($"Errore importazione frame: {ex.Message}");
        }
        finally
        {
            _importInProgress = false;
            BtnImportFrames.IsEnabled = true;
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

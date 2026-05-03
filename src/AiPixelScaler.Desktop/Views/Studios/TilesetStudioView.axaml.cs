using System;
using AiPixelScaler.Core.Pipeline.Templates;
using Avalonia.Controls;

namespace AiPixelScaler.Desktop.Views.Studios;

public partial class TilesetStudioView : UserControl
{
    public event EventHandler<TilesetStudioAction>? ActionRequested;

    private bool _syncing;

    // Espone la sezione griglia condivisa in modo che MainWindow possa iscriversi all'evento
    public GridSectionView GridSection => GridPanel;

    public TilesetStudioView()
    {
        InitializeComponent();

        BtnTilesetOpen.Click          += (_, _) => Request(TilesetStudioAction.OpenImage);
        BtnTilesetApplyPalette.Click  += (_, _) => Request(TilesetStudioAction.ApplyPalette);
        BtnTilesetMakeTileable.Click  += (_, _) => Request(TilesetStudioAction.MakeTileable);
        BtnTilesetPadToMultiple.Click += (_, _) => Request(TilesetStudioAction.PadToMultiple);
        BtnTilesetExportTiled.Click   += (_, _) => Request(TilesetStudioAction.ExportTiledJson);
        BtnTilesetApplyCropPot.Click  += (_, _) => Request(TilesetStudioAction.ApplyCropPot);
        BtnTilesetSnapCells.Click     += (_, _) => Request(TilesetStudioAction.SnapCellsToGrid);

        ChkTilesetTilePreview.IsCheckedChanged += (_, _) =>
        {
            if (!_syncing)
                Request(TilesetStudioAction.ToggleTilePreview);
        };
    }

    // ── Proprietà ────────────────────────────────────────────────────────────

    public bool IsTilePreviewEnabled
    {
        get => ChkTilesetTilePreview.IsChecked == true;
        set
        {
            _syncing = true;
            try   { ChkTilesetTilePreview.IsChecked = value; }
            finally { _syncing = false; }
        }
    }

    // Deleghe alla GridSection
    public void SetImportEnabled(bool enabled) => GridPanel.SetImportEnabled(enabled);
    public GridTemplateGenerator.PivotPreset GetPivotPreset() => GridPanel.GetPivotPreset();

    // ── Stato ────────────────────────────────────────────────────────────────

    public TilesetState GetTilesetState()
    {
        var grid = GridPanel.GetGridState();
        return new TilesetState(
            PalettePresetIndex: CmbTilesetPalettePreset.SelectedIndex,
            PaletteColors:      TxtTilesetPaletteColors.Text ?? string.Empty,
            DitherEnabled:      ChkTilesetDither.IsChecked == true,
            SeamlessBlend:      TxtTilesetBlend.Text ?? string.Empty,
            PadMultiple:        TxtTilesetPadMultiple.Text ?? string.Empty,
            CellW:              grid.CellW,
            CellH:              grid.CellH,
            Cols:               grid.Cols,
            Rows:               grid.Rows,
            PivotIndex:         grid.PivotIndex,
            PivotCustomX:       grid.PivotCustomX,
            PivotCustomY:       grid.PivotCustomY,
            ShowBorder:         grid.ShowBorder,
            ShowPivot:          grid.ShowPivot,
            ShowBaseline:       grid.ShowBaseline,
            ShowIndex:          grid.ShowIndex,
            ShowTint:           grid.ShowTint,
            CropModeIndex:      CmbTilesetCropMode.SelectedIndex,
            CropAlpha:          TxtTilesetCropAlpha.Text ?? string.Empty,
            CropPadding:        TxtTilesetCropPadding.Text ?? string.Empty,
            CropPotIndex:       CmbTilesetCropPot.SelectedIndex);
    }

    public void SetTilesetState(TilesetState state)
    {
        _syncing = true;
        try
        {
            CmbTilesetPalettePreset.SelectedIndex = state.PalettePresetIndex;
            TxtTilesetPaletteColors.Text  = state.PaletteColors;
            ChkTilesetDither.IsChecked    = state.DitherEnabled;
            TxtTilesetBlend.Text          = state.SeamlessBlend;
            TxtTilesetPadMultiple.Text    = state.PadMultiple;
            CmbTilesetCropMode.SelectedIndex = state.CropModeIndex;
            TxtTilesetCropAlpha.Text      = state.CropAlpha;
            TxtTilesetCropPadding.Text    = state.CropPadding;
            CmbTilesetCropPot.SelectedIndex  = state.CropPotIndex;
        }
        finally
        {
            _syncing = false;
        }

        GridPanel.SetGridState(new GridState(
            CellW:        state.CellW,
            CellH:        state.CellH,
            Cols:         state.Cols,
            Rows:         state.Rows,
            PivotIndex:   state.PivotIndex,
            PivotCustomX: state.PivotCustomX,
            PivotCustomY: state.PivotCustomY,
            ShowBorder:   state.ShowBorder,
            ShowPivot:    state.ShowPivot,
            ShowBaseline: state.ShowBaseline,
            ShowIndex:    state.ShowIndex,
            ShowTint:     state.ShowTint));
    }

    // ── Privati ──────────────────────────────────────────────────────────────

    private void Request(TilesetStudioAction action) => ActionRequested?.Invoke(this, action);
}

public sealed record TilesetState(
    int    PalettePresetIndex,
    string PaletteColors,
    bool   DitherEnabled,
    string SeamlessBlend,
    string PadMultiple,
    string CellW,
    string CellH,
    string Cols,
    string Rows,
    int    PivotIndex,
    double PivotCustomX,
    double PivotCustomY,
    bool   ShowBorder,
    bool   ShowPivot,
    bool   ShowBaseline,
    bool   ShowIndex,
    bool   ShowTint,
    int    CropModeIndex,
    string CropAlpha,
    string CropPadding,
    int    CropPotIndex);

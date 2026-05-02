using System;
using AiPixelScaler.Core.Pipeline.Templates;
using Avalonia.Controls;

namespace AiPixelScaler.Desktop.Views.Studios;

public partial class TilesetStudioView : UserControl
{
    public event EventHandler<TilesetStudioAction>? ActionRequested;

    private readonly RadioButton[] _pivotButtons;
    private bool _syncing;

    public TilesetStudioView()
    {
        InitializeComponent();

        _pivotButtons =
        [
            TplTilePivotTL,
            TplTilePivotTC,
            TplTilePivotTR,
            TplTilePivotML,
            TplTilePivotCC,
            TplTilePivotMR,
            TplTilePivotBL,
            TplTilePivotBC,
            TplTilePivotBR
        ];

        BtnTilesetOpen.Click += (_, _) => Request(TilesetStudioAction.OpenImage);
        BtnTilesetApplyPalette.Click += (_, _) => Request(TilesetStudioAction.ApplyPalette);
        BtnTilesetMakeTileable.Click += (_, _) => Request(TilesetStudioAction.MakeTileable);
        BtnTilesetPadToMultiple.Click += (_, _) => Request(TilesetStudioAction.PadToMultiple);
        BtnTilesetGenerateTemplate.Click += (_, _) => Request(TilesetStudioAction.GenerateTemplate);
        BtnTilesetExportTemplate.Click += (_, _) => Request(TilesetStudioAction.ExportTemplatePng);
        BtnTilesetImportFrames.Click += (_, _) => Request(TilesetStudioAction.ImportFrames);
        BtnTilesetExportTiled.Click += (_, _) => Request(TilesetStudioAction.ExportTiledJson);
        BtnTilesetApplyCropPot.Click += (_, _) => Request(TilesetStudioAction.ApplyCropPot);
        BtnTilesetSnapCells.Click += (_, _) => Request(TilesetStudioAction.SnapCellsToGrid);

        ChkTilesetTilePreview.IsCheckedChanged += (_, _) =>
        {
            if (!_syncing)
                Request(TilesetStudioAction.ToggleTilePreview);
        };

        BtnTplPreset16.Click += (_, _) => SetPreset(16, 16);
        BtnTplPreset24.Click += (_, _) => SetPreset(24, 24);
        BtnTplPreset32.Click += (_, _) => SetPreset(32, 32);
        BtnTplPreset48.Click += (_, _) => SetPreset(48, 48);
        BtnTplPreset64.Click += (_, _) => SetPreset(64, 64);
        BtnTplPreset128.Click += (_, _) => SetPreset(128, 128);
        BtnTplPreset256.Click += (_, _) => SetPreset(256, 256);

        TplTileCols.ValueChanged += (_, _) => UpdateInfoLabel();
        TplTileRows.ValueChanged += (_, _) => UpdateInfoLabel();
        TplTileCellW.ValueChanged += (_, _) => UpdateInfoLabel();
        TplTileCellH.ValueChanged += (_, _) => UpdateInfoLabel();
        TplTilePivotX.ValueChanged += (_, _) => UpdatePivotLabel();
        TplTilePivotY.ValueChanged += (_, _) => UpdatePivotLabel();

        foreach (var button in _pivotButtons)
            button.IsCheckedChanged += (_, _) => UpdatePivotLabel();

        UpdateInfoLabel();
        UpdatePivotLabel();
    }

    public bool IsTilePreviewEnabled
    {
        get => ChkTilesetTilePreview.IsChecked == true;
        set
        {
            _syncing = true;
            try
            {
                ChkTilesetTilePreview.IsChecked = value;
            }
            finally
            {
                _syncing = false;
            }
        }
    }

    public void SetImportEnabled(bool enabled) => BtnTilesetImportFrames.IsEnabled = enabled;

    public GridTemplateGenerator.PivotPreset GetPivotPreset() => ReadPivotPreset();

    public TilesetState GetTilesetState() => new(
        CmbTilesetPalettePreset.SelectedIndex,
        TxtTilesetPaletteColors.Text ?? string.Empty,
        ChkTilesetDither.IsChecked == true,
        TxtTilesetBlend.Text ?? string.Empty,
        TxtTilesetPadMultiple.Text ?? string.Empty,
        NumericText(TplTileCellW),
        NumericText(TplTileCellH),
        NumericText(TplTileCols),
        NumericText(TplTileRows),
        CurrentPivotIndex(),
        (double)(TplTilePivotX.Value ?? 0.5m),
        (double)(TplTilePivotY.Value ?? 0.5m),
        TplTileShowBorder.IsChecked == true,
        TplTileShowPivot.IsChecked == true,
        TplTileShowBaseline.IsChecked == true,
        TplTileShowIndex.IsChecked == true,
        TplTileShowTint.IsChecked == true,
        CmbTilesetCropMode.SelectedIndex,
        TxtTilesetCropAlpha.Text ?? string.Empty,
        TxtTilesetCropPadding.Text ?? string.Empty,
        CmbTilesetCropPot.SelectedIndex);

    public void SetTilesetState(TilesetState state)
    {
        _syncing = true;
        try
        {
            CmbTilesetPalettePreset.SelectedIndex = state.PalettePresetIndex;
            TxtTilesetPaletteColors.Text = state.PaletteColors;
            ChkTilesetDither.IsChecked = state.DitherEnabled;
            TxtTilesetBlend.Text = state.SeamlessBlend;
            TxtTilesetPadMultiple.Text = state.PadMultiple;
            SetNumeric(TplTileCellW, state.CellW, 64);
            SetNumeric(TplTileCellH, state.CellH, 64);
            SetNumeric(TplTileCols, state.Cols, 4);
            SetNumeric(TplTileRows, state.Rows, 4);
            SetPivotByIndex(state.PivotIndex);
            TplTilePivotX.Value = (decimal)state.PivotCustomX;
            TplTilePivotY.Value = (decimal)state.PivotCustomY;
            TplTileShowBorder.IsChecked = state.ShowBorder;
            TplTileShowPivot.IsChecked = state.ShowPivot;
            TplTileShowBaseline.IsChecked = state.ShowBaseline;
            TplTileShowIndex.IsChecked = state.ShowIndex;
            TplTileShowTint.IsChecked = state.ShowTint;
            CmbTilesetCropMode.SelectedIndex = state.CropModeIndex;
            TxtTilesetCropAlpha.Text = state.CropAlpha;
            TxtTilesetCropPadding.Text = state.CropPadding;
            CmbTilesetCropPot.SelectedIndex = state.CropPotIndex;
        }
        finally
        {
            _syncing = false;
        }

        UpdateInfoLabel();
        UpdatePivotLabel();
    }

    private void SetPreset(int w, int h)
    {
        TplTileCellW.Value = w;
        TplTileCellH.Value = h;
        UpdateInfoLabel();
    }

    private void UpdateInfoLabel()
    {
        var cols = (int)(TplTileCols.Value ?? 4m);
        var rows = (int)(TplTileRows.Value ?? 4m);
        var cw = (int)(TplTileCellW.Value ?? 64m);
        var ch = (int)(TplTileCellH.Value ?? 64m);
        TplTileInfoLabel.Text = $"{cols * rows} frame · {cols * cw}×{rows * ch} px";
    }

    private GridTemplateGenerator.PivotPreset ReadPivotPreset() => CurrentPivotIndex() switch
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

    private void UpdatePivotLabel()
    {
        var preset = ReadPivotPreset();
        var (nx, ny) = preset == GridTemplateGenerator.PivotPreset.Custom
            ? ((double)(TplTilePivotX.Value ?? 0.5m), (double)(TplTilePivotY.Value ?? 0.5m))
            : GridTemplateGenerator.PivotNdc(preset);

        TplTilePivotLabel.Text = FormatPivotName(preset);
        TplTilePivotCoords.Text = $"x={nx:F2} · y={ny:F2}";
        TplTileCustomXYPanel.IsVisible = preset == GridTemplateGenerator.PivotPreset.Custom;
    }

    private void SetPivotByIndex(int index)
    {
        if (index < 0 || index >= _pivotButtons.Length)
            index = 4;

        for (var i = 0; i < _pivotButtons.Length; i++)
            _pivotButtons[i].IsChecked = i == index;
    }

    private int CurrentPivotIndex()
    {
        for (var i = 0; i < _pivotButtons.Length; i++)
        {
            if (_pivotButtons[i].IsChecked == true)
                return i;
        }
        return 4;
    }

    private static string NumericText(NumericUpDown input) =>
        ((int)(input.Value ?? 0m)).ToString();

    private static void SetNumeric(NumericUpDown input, string value, int fallback)
    {
        input.Value = int.TryParse(value, out var parsed) ? parsed : fallback;
    }

    private static string FormatPivotName(GridTemplateGenerator.PivotPreset preset) =>
        preset.ToString()
            .Replace("Top", "Top ")
            .Replace("Mid", "Mid ")
            .Replace("Bottom", "Bottom ")
            .Replace("Left", "Left")
            .Replace("Right", "Right")
            .Trim();

    private void Request(TilesetStudioAction action) => ActionRequested?.Invoke(this, action);
}

public sealed record TilesetState(
    int PalettePresetIndex,
    string PaletteColors,
    bool DitherEnabled,
    string SeamlessBlend,
    string PadMultiple,
    string CellW,
    string CellH,
    string Cols,
    string Rows,
    int PivotIndex,
    double PivotCustomX,
    double PivotCustomY,
    bool ShowBorder,
    bool ShowPivot,
    bool ShowBaseline,
    bool ShowIndex,
    bool ShowTint,
    int CropModeIndex,
    string CropAlpha,
    string CropPadding,
    int CropPotIndex);

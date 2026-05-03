using System;
using AiPixelScaler.Core.Pipeline.Templates;
using Avalonia.Controls;

namespace AiPixelScaler.Desktop.Views.Studios;

public partial class GridSectionView : UserControl
{
    public event EventHandler<GridSectionAction>? ActionRequested;

    private readonly RadioButton[] _pivotButtons;

    public GridSectionView()
    {
        InitializeComponent();

        _pivotButtons =
        [
            TplTilePivotTL, TplTilePivotTC, TplTilePivotTR,
            TplTilePivotML, TplTilePivotCC, TplTilePivotMR,
            TplTilePivotBL, TplTilePivotBC, TplTilePivotBR
        ];

        BtnGridGenerate.Click       += (_, _) => Request(GridSectionAction.GenerateTemplate);
        BtnGridExportTemplate.Click += (_, _) => Request(GridSectionAction.ExportTemplatePng);
        BtnGridImportFrames.Click   += (_, _) => Request(GridSectionAction.ImportFrames);

        BtnTplPreset16.Click  += (_, _) => SetPreset(16, 16);
        BtnTplPreset24.Click  += (_, _) => SetPreset(24, 24);
        BtnTplPreset32.Click  += (_, _) => SetPreset(32, 32);
        BtnTplPreset48.Click  += (_, _) => SetPreset(48, 48);
        BtnTplPreset64.Click  += (_, _) => SetPreset(64, 64);
        BtnTplPreset128.Click += (_, _) => SetPreset(128, 128);
        BtnTplPreset256.Click += (_, _) => SetPreset(256, 256);

        TplTileCols.ValueChanged  += (_, _) => UpdateInfoLabel();
        TplTileRows.ValueChanged  += (_, _) => UpdateInfoLabel();
        TplTileCellW.ValueChanged += (_, _) => UpdateInfoLabel();
        TplTileCellH.ValueChanged += (_, _) => UpdateInfoLabel();
        TplTilePivotX.ValueChanged += (_, _) => UpdatePivotLabel();
        TplTilePivotY.ValueChanged += (_, _) => UpdatePivotLabel();

        foreach (var btn in _pivotButtons)
            btn.IsCheckedChanged += (_, _) => UpdatePivotLabel();

        UpdateInfoLabel();
        UpdatePivotLabel();
    }

    // ── Public API ───────────────────────────────────────────────────────────

    public void SetImportEnabled(bool enabled) => BtnGridImportFrames.IsEnabled = enabled;

    public GridTemplateGenerator.PivotPreset GetPivotPreset() => ReadPivotPreset();

    public GridState GetGridState() => new(
        CellW:        NumericText(TplTileCellW),
        CellH:        NumericText(TplTileCellH),
        Cols:         NumericText(TplTileCols),
        Rows:         NumericText(TplTileRows),
        PivotIndex:   CurrentPivotIndex(),
        PivotCustomX: (double)(TplTilePivotX.Value ?? 0.5m),
        PivotCustomY: (double)(TplTilePivotY.Value ?? 0.5m),
        ShowBorder:   TplTileShowBorder.IsChecked   == true,
        ShowPivot:    TplTileShowPivot.IsChecked    == true,
        ShowBaseline: TplTileShowBaseline.IsChecked == true,
        ShowIndex:    TplTileShowIndex.IsChecked    == true,
        ShowTint:     TplTileShowTint.IsChecked     == true);

    public void SetGridState(GridState state)
    {
        SetNumeric(TplTileCellW, state.CellW, 64);
        SetNumeric(TplTileCellH, state.CellH, 64);
        SetNumeric(TplTileCols,  state.Cols,  4);
        SetNumeric(TplTileRows,  state.Rows,  4);
        SetPivotByIndex(state.PivotIndex);
        TplTilePivotX.Value = (decimal)state.PivotCustomX;
        TplTilePivotY.Value = (decimal)state.PivotCustomY;
        TplTileShowBorder.IsChecked   = state.ShowBorder;
        TplTileShowPivot.IsChecked    = state.ShowPivot;
        TplTileShowBaseline.IsChecked = state.ShowBaseline;
        TplTileShowIndex.IsChecked    = state.ShowIndex;
        TplTileShowTint.IsChecked     = state.ShowTint;
        UpdateInfoLabel();
        UpdatePivotLabel();
    }

    // ── Private ──────────────────────────────────────────────────────────────

    private void SetPreset(int w, int h)
    {
        TplTileCellW.Value = w;
        TplTileCellH.Value = h;
        UpdateInfoLabel();
    }

    private void UpdateInfoLabel()
    {
        var cols = (int)(TplTileCols.Value  ?? 4m);
        var rows = (int)(TplTileRows.Value  ?? 4m);
        var cw   = (int)(TplTileCellW.Value ?? 64m);
        var ch   = (int)(TplTileCellH.Value ?? 64m);
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

        TplTilePivotLabel.Text   = FormatPivotName(preset);
        TplTilePivotCoords.Text  = $"x={nx:F2} · y={ny:F2}";
        TplTileCustomXYPanel.IsVisible = preset == GridTemplateGenerator.PivotPreset.Custom;
    }

    private void SetPivotByIndex(int index)
    {
        if (index < 0 || index >= _pivotButtons.Length) index = 4;
        for (var i = 0; i < _pivotButtons.Length; i++)
            _pivotButtons[i].IsChecked = i == index;
    }

    private int CurrentPivotIndex()
    {
        for (var i = 0; i < _pivotButtons.Length; i++)
            if (_pivotButtons[i].IsChecked == true) return i;
        return 4;
    }

    private static string NumericText(NumericUpDown input) =>
        ((int)(input.Value ?? 0m)).ToString();

    private static void SetNumeric(NumericUpDown input, string value, int fallback) =>
        input.Value = int.TryParse(value, out var parsed) ? parsed : fallback;

    private static string FormatPivotName(GridTemplateGenerator.PivotPreset preset) =>
        preset.ToString()
              .Replace("Top",    "Top ")
              .Replace("Mid",    "Mid ")
              .Replace("Bottom", "Bottom ")
              .Trim();

    private void Request(GridSectionAction action) => ActionRequested?.Invoke(this, action);
}

public sealed record GridState(
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
    bool   ShowTint);

using System;
using Avalonia.Controls;

namespace AiPixelScaler.Desktop.Views.Studios;

public partial class AnimationStudioView : UserControl
{
    public event EventHandler<AnimationStudioAction>? ActionRequested;

    // Espone la sezione griglia condivisa in modo che MainWindow possa iscriversi all'evento
    public GridSectionView GridSection => GridPanel;

    public AnimationStudioView()
    {
        InitializeComponent();

        BtnAnimPreview.Click          += (_, _) => Request(AnimationStudioAction.OpenAnimationPreview);
        BtnAnimSandbox.Click          += (_, _) => Request(AnimationStudioAction.OpenSandbox);
        BtnAnimEnter.Click            += (_, _) => Request(AnimationStudioAction.EnterFrameWorkbench);
        BtnAnimCommit.Click           += (_, _) => Request(AnimationStudioAction.CommitFrameWorkbench);
        BtnAnimCancel.Click           += (_, _) => Request(AnimationStudioAction.CancelFrameWorkbench);
        BtnAnimAlignAllCenter.Click   += (_, _) => Request(AnimationStudioAction.AlignAllCenter);
        BtnAnimAlignAllBaseline.Click += (_, _) => Request(AnimationStudioAction.AlignAllBaseline);
        BtnAnimResetAll.Click         += (_, _) => Request(AnimationStudioAction.ResetAll);
        BtnAnimSelCenter.Click        += (_, _) => Request(AnimationStudioAction.AlignSelectedCenter);
        BtnAnimSelBaseline.Click      += (_, _) => Request(AnimationStudioAction.AlignSelectedBaseline);
        BtnAnimSelLayoutCenter.Click  += (_, _) => Request(AnimationStudioAction.AlignSelectedLayoutCenter);
        BtnAnimSelReset.Click         += (_, _) => Request(AnimationStudioAction.ResetSelected);
        BtnAnimGlobalScan.Click       += (_, _) => Request(AnimationStudioAction.RunGlobalScan);
        BtnAnimBaselineAlign.Click    += (_, _) => Request(AnimationStudioAction.RunBaselineAlignment);
        BtnAnimCenterInCells.Click    += (_, _) => Request(AnimationStudioAction.RunCenterInCells);
        BtnAnimImportVideo.Click      += (_, _) => Request(AnimationStudioAction.ImportFromVideo);
        BtnAnimExportZip.Click        += (_, _) => Request(AnimationStudioAction.ExportFramesZip);

        SliderAnimPivotX.ValueChanged += (_, _) => LblAnimPivotX.Text = $"{SliderAnimPivotX.Value:F2}";
        SliderAnimPivotY.ValueChanged += (_, _) => LblAnimPivotY.Text = $"{SliderAnimPivotY.Value:F2}";
    }

    // ── Stato in entrata / uscita ────────────────────────────────────────────

    public AnimationState GetAnimationState() => new(
        SnapToGrid:          ChkAnimSnapGrid.IsChecked == true,
        ExtractPadding:      ((int)(NumAnimPadding.Value ?? 0m)).ToString(),
        NormalizePolicyIndex: CmbAnimNormalizePolicy.SelectedIndex,
        PivotX:              SliderAnimPivotX.Value,
        PivotY:              SliderAnimPivotY.Value);

    public void SetAnimationState(AnimationState state)
    {
        ChkAnimSnapGrid.IsChecked = state.SnapToGrid;
        NumAnimPadding.Value = int.TryParse(state.ExtractPadding, out var pad) ? pad : 0;
        CmbAnimNormalizePolicy.SelectedIndex = state.NormalizePolicyIndex;
        SliderAnimPivotX.Value = state.PivotX;
        SliderAnimPivotY.Value = state.PivotY;
        LblAnimPivotX.Text = $"{state.PivotX:F2}";
        LblAnimPivotY.Text = $"{state.PivotY:F2}";
    }

    // ── API pubblica chiamata da MainWindow ──────────────────────────────────

    /// <summary>Aggiorna il testo di stato del workbench e mostra/nasconde la barra attiva.</summary>
    public void SetWorkbenchActive(bool active, int frameCount = 0, int padPx = 0)
    {
        BtnAnimEnter.IsVisible           = !active;
        AnimWorkbenchActiveBar.IsVisible = active;
        TxtAnimWorkbenchStatus.IsVisible = active;

        TxtAnimWorkbenchStatus.Text = active
            ? $"Workbench attivo: {frameCount} frame estratti (padding {padPx} px). Clic per selezionare, drag per spostare."
            : "Workbench non attivo.";
    }

    /// <summary>Aggiorna la label del frame selezionato nel workbench.</summary>
    public void SetSelectedFrameInfo(string text) =>
        TxtAnimSelectedInfo.Text = text;

    /// <summary>Aggiorna il risultato dell'analisi globale celle.</summary>
    public void SetGlobalScanResult(string text) =>
        TxtAnimGlobalResult.Text = text;

    // ── Privati ──────────────────────────────────────────────────────────────

    private void Request(AnimationStudioAction action) => ActionRequested?.Invoke(this, action);
}

public sealed record AnimationState(
    bool   SnapToGrid,
    string ExtractPadding,
    int    NormalizePolicyIndex,
    double PivotX,
    double PivotY);

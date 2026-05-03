using System;
using System.Collections.Generic;
using Avalonia.Controls;
using Avalonia.Input;
using Avalonia.Media;
using SixLabors.ImageSharp.PixelFormats;

namespace AiPixelScaler.Desktop.Views.Studios;

public partial class SpriteStudioView : UserControl
{
    public event EventHandler<SpriteStudioAction>? ActionRequested;
    private bool _syncing;

    public SpriteStudioView()
    {
        InitializeComponent();

        BtnSpriteOpen.Click += (_, _) => Request(SpriteStudioAction.OpenImage);
        BtnSpriteDefault.Click += (_, _) => Request(SpriteStudioAction.ApplyDefaultPreset);
        BtnSpriteSafe.Click += (_, _) => Request(SpriteStudioAction.ApplySafePreset);
        BtnSpriteAggressive.Click += (_, _) => Request(SpriteStudioAction.ApplyAggressivePreset);
        BtnSpriteCleanup.Click += (_, _) => Request(SpriteStudioAction.RunCleanup);
        BtnSpriteOneClick.Click += (_, _) => Request(SpriteStudioAction.RunOneClickCleanup);
        BtnSpritePipelineApply.Click += (_, _) => Request(SpriteStudioAction.ApplyPipeline);
        BtnSpriteDefringe.Click += (_, _) => Request(SpriteStudioAction.ApplyDefringe);
        BtnSpriteMedian.Click += (_, _) => Request(SpriteStudioAction.ApplyMedian);
        BtnSpriteBackgroundIsolation.Click += (_, _) =>
        {
            var action = BtnBgModeGlobal.IsChecked == true
                ? SpriteStudioAction.ApplyGlobalChromaKey
                : SpriteStudioAction.ApplyBackgroundIsolation;
            Request(action);
        };

        // Toggle modalità: esclusione mutua + aggiornamento hint + visibilità Sobel
        BtnBgModeFlood.IsCheckedChanged += (_, _) =>
        {
            if (BtnBgModeFlood.IsChecked == true)
            {
                BtnBgModeGlobal.IsChecked = false;
                UpdateBgModeHint();
            }
            else if (BtnBgModeGlobal.IsChecked != true)
            {
                // Impedisce di deselezionare entrambi
                BtnBgModeFlood.IsChecked = true;
            }
        };
        BtnBgModeGlobal.IsCheckedChanged += (_, _) =>
        {
            if (BtnBgModeGlobal.IsChecked == true)
            {
                BtnBgModeFlood.IsChecked = false;
                UpdateBgModeHint();
            }
            else if (BtnBgModeFlood.IsChecked != true)
            {
                BtnBgModeGlobal.IsChecked = true;
            }
        };
        BtnSpriteIslandCleanup.Click += (_, _) => Request(SpriteStudioAction.ApplyIslandCleanup);
        BtnMajorityDenoise.Click += (_, _) => Request(SpriteStudioAction.ApplyMajorityDenoise);
        BtnSpriteSelect.Click += (_, _) => Request(SpriteStudioAction.SelectArea);
        BtnSpriteSelectAll.Click += (_, _) => Request(SpriteStudioAction.SelectAll);
        BtnSpriteClearSelection.Click += (_, _) => Request(SpriteStudioAction.ClearSelection);
        BtnSpriteExportSelection.Click += (_, _) => Request(SpriteStudioAction.ExportSelection);
        BtnSpriteCrop.Click += (_, _) => Request(SpriteStudioAction.CropSelection);
        BtnSpriteRemove.Click += (_, _) => Request(SpriteStudioAction.RemoveSelection);
        ChkSpriteManualCrop.IsCheckedChanged += (_, _) =>
        {
            if (!_syncing)
                Request(SpriteStudioAction.ToggleManualCrop);
        };
        BtnSpriteDetect.Click += (_, _) => Request(SpriteStudioAction.DetectSprites);
        BtnSpriteGridSlice.Click += (_, _) => Request(SpriteStudioAction.GridSlice);
        BtnSpriteSaveSelectedFrame.Click += (_, _) => Request(SpriteStudioAction.SaveSelectedFrame);
        BtnSpriteExportFramesZip.Click += (_, _) => Request(SpriteStudioAction.ExportAllFramesZip);
        BtnSpriteFloatingPaste.Click += (_, _) => Request(SpriteStudioAction.OpenFloatingPaste);
        BtnSpritePasteDest.Click += (_, _) => Request(SpriteStudioAction.PasteDestination);
        BtnSpritePasteSrc.Click += (_, _) => Request(SpriteStudioAction.PasteSource);
        BtnSpritePasteCopy.Click += (_, _) => Request(SpriteStudioAction.CopyPasteSelection);
        BtnSpritePasteExit.Click += (_, _) => Request(SpriteStudioAction.ExitFloatingPaste);
        BtnMorphErode.Click  += (_, _) => Request(SpriteStudioAction.MorphologyErode);
        BtnMorphDilate.Click += (_, _) => Request(SpriteStudioAction.MorphologyDilate);
        BtnMorphOpen.Click   += (_, _) => Request(SpriteStudioAction.MorphologyOpen);
        BtnMorphClose.Click  += (_, _) => Request(SpriteStudioAction.MorphologyClose);
        BtnRemoveIslands.Click  += (_, _) => Request(SpriteStudioAction.RemoveIsolatedIslands);
        BtnRemoveOutliers.Click += (_, _) => Request(SpriteStudioAction.RemoveColorOutliers);
        BtnSpriteQuantizeApply.Click += (_, _) => Request(SpriteStudioAction.ApplyQuantize);
        BtnSpriteAnalyzePalette.Click += (_, _) => Request(SpriteStudioAction.AnalyzePalette);
        BtnSpriteResizeNearest.Click += (_, _) => Request(SpriteStudioAction.ResizeNearest);
        BtnSpriteMirrorH.Click += (_, _) => Request(SpriteStudioAction.MirrorHorizontal);
        BtnSpriteMirrorV.Click += (_, _) => Request(SpriteStudioAction.MirrorVertical);
        BtnSpriteExportPng.Click += (_, _) => Request(SpriteStudioAction.ExportPng);
        BtnSpriteExportJson.Click += (_, _) => Request(SpriteStudioAction.ExportJson);
        BtnSpriteBgPipette.Click += (_, _) => Request(SpriteStudioAction.ActivatePipetteForBackground);
    }

    public void SetSelectionInfo(string text, bool hasSelection)
    {
        TxtSpriteSelectionInfo.Text = text;
        BtnSpriteExportSelection.IsEnabled = hasSelection;
        BtnSpriteCrop.IsEnabled = hasSelection;
        BtnSpriteRemove.IsEnabled = hasSelection;
    }

    public string GridRowsText
    {
        get => TxtSpriteRows.Text ?? string.Empty;
        set => TxtSpriteRows.Text = value;
    }

    public string GridColsText
    {
        get => TxtSpriteCols.Text ?? string.Empty;
        set => TxtSpriteCols.Text = value;
    }

    public bool IsManualCropEnabled
    {
        get => ChkSpriteManualCrop.IsChecked == true;
        set
        {
            _syncing = true;
            try
            {
                ChkSpriteManualCrop.IsChecked = value;
            }
            finally
            {
                _syncing = false;
            }
        }
    }

    public int SelectedCellIndex => SpriteCellList.SelectedIndex;

    public string ResizeWidthText
    {
        get => TxtSpriteResizeW.Text ?? string.Empty;
        set => TxtSpriteResizeW.Text = value;
    }

    public string ResizeHeightText
    {
        get => TxtSpriteResizeH.Text ?? string.Empty;
        set => TxtSpriteResizeH.Text = value;
    }

    public void SetCells(IReadOnlyList<string> cells)
    {
        SpriteCellList.ItemsSource = cells;
        BtnSpriteSaveSelectedFrame.IsEnabled = cells.Count > 0;
        BtnSpriteExportFramesZip.IsEnabled = cells.Count > 0;
        TxtSpriteCellsSummary.Text = cells.Count == 0 ? "Nessun frame rilevato." : $"{cells.Count} frame/celle disponibili.";
    }

    public void SetPasteState(bool active, bool sourceView, string bufferStatus)
    {
        SpritePasteActiveBar.IsVisible = active;
        BtnSpriteFloatingPaste.IsVisible = !active;
        BtnSpritePasteDest.IsChecked = active && !sourceView;
        BtnSpritePasteSrc.IsChecked = active && sourceView;
        TxtSpritePasteBufferStatus.Text = bufferStatus;
    }

    public SpriteCleanupState GetCleanupState() => new(
        TxtSpriteBackgroundHex.Text ?? string.Empty,
        TxtSpriteBackgroundTol.Text ?? string.Empty,
        TxtSpriteBackgroundEdge.Text ?? string.Empty,
        ChkSpriteAlphaThreshold.IsChecked == true,
        TxtSpriteAlphaThreshold.Text ?? string.Empty,
        TxtSpriteDefringeOpaque.Text ?? string.Empty,
        ChkSpriteOutline.IsChecked == true,
        TxtSpriteOutlineHex.Text ?? string.Empty,
        TxtSpriteMinIsland.Text ?? string.Empty,
        ChkSpriteMajorityDenoise.IsChecked == true,
        TxtSpriteMajorityMinNeighbors.Text ?? string.Empty,
        ChkSpriteQuantize.IsChecked == true,
        TxtSpriteQuantizeColors.Text ?? string.Empty,
        CmbSpriteQuantizeMethod.SelectedIndex);

    public void SetCleanupState(SpriteCleanupState state)
    {
        _syncing = true;
        try
        {
            TxtSpriteBackgroundHex.Text = state.BackgroundHex;
            TxtSpriteBackgroundTol.Text = state.BackgroundTolerance;
            TxtSpriteBackgroundEdge.Text = state.BackgroundEdgeThreshold;
            ChkSpriteAlphaThreshold.IsChecked = state.EnableAlphaThreshold;
            TxtSpriteAlphaThreshold.Text = state.AlphaThreshold;
            TxtSpriteDefringeOpaque.Text = state.DefringeOpaque;
            ChkSpriteOutline.IsChecked = state.EnableOutline;
            TxtSpriteOutlineHex.Text = state.OutlineHex;
            TxtSpriteMinIsland.Text = state.MinIsland;
            ChkSpriteMajorityDenoise.IsChecked = state.EnableMajorityDenoise;
            TxtSpriteMajorityMinNeighbors.Text = state.MajorityMinNeighbors;
            ChkSpriteQuantize.IsChecked = state.EnableQuantize;
            TxtSpriteQuantizeColors.Text = state.QuantizeColors;
            CmbSpriteQuantizeMethod.SelectedIndex = state.QuantizeMethodIndex;
        }
        finally
        {
            _syncing = false;
        }
    }

    /// <summary>
    /// Mostra o nasconde la badge del preset attivo sopra la sezione Filtri Sprite.
    /// Passa <c>null</c> per nasconderla (stato Personalizzato / nessun preset).
    /// </summary>
    public void SetPresetBadge(string? presetName)
    {
        if (presetName is null)
        {
            PresetBadgeBorder.IsVisible = false;
        }
        else
        {
            TxtPresetBadge.Text = presetName;
            PresetBadgeBorder.IsVisible = true;
        }
    }

    /// <summary>
    /// Popola il pannello swatch con i colori della palette.
    /// Ogni swatch è cliccabile: imposta il testo del TextBox sfondo e spara <see cref="SpriteStudioAction.PaletteColorPickedAsBackground"/>.
    /// Passa lista vuota per nascondere il pannello.
    /// </summary>
    public void SetPalette(IReadOnlyList<Rgba32> palette)
    {
        PaletteSwatchPanel.Children.Clear();

        if (palette.Count == 0)
        {
            TxtPaletteInfo.IsVisible = false;
            PaletteSwatchPanel.IsVisible = false;
            return;
        }

        var cap = Math.Min(palette.Count, 256);
        var extra = palette.Count > 256 ? $" (visualizzati {cap})" : string.Empty;
        TxtPaletteInfo.Text = $"{palette.Count} colori{extra} — clicca per impostare come sfondo";
        TxtPaletteInfo.IsVisible = true;

        for (var i = 0; i < cap; i++)
        {
            var c = palette[i];
            var hex = $"#{c.R:X2}{c.G:X2}{c.B:X2}";

            var border = new Border
            {
                Width = 16,
                Height = 16,
                Margin = new Avalonia.Thickness(1),
                CornerRadius = new Avalonia.CornerRadius(2),
                Background = new SolidColorBrush(new Avalonia.Media.Color(255, c.R, c.G, c.B)),
                Cursor = new Cursor(StandardCursorType.Hand),
            };
            ToolTip.SetTip(border, hex);

            var capturedHex = hex;
            border.PointerPressed += (_, _) =>
            {
                TxtSpriteBackgroundHex.Text = capturedHex;
                Request(SpriteStudioAction.PaletteColorPickedAsBackground);
            };

            PaletteSwatchPanel.Children.Add(border);
        }

        PaletteSwatchPanel.IsVisible = true;
    }

    /// <summary>Iterazioni per le operazioni morfologiche (Erode/Dilate/Open/Close).</summary>
    public string MorphIterationsText => TxtMorphIterations.Text ?? "1";

    /// <summary>Dimensione minima cluster da mantenere per RemoveIsolatedIslands.</summary>
    public string AnomalyMinIslandText => TxtAnomalyMinIsland.Text ?? "4";

    /// <summary>Soglia vicini identici per il Majority Denoise standalone.</summary>
    public string MajorityMinNeighborsText => TxtSpriteMajorityMinNeighbors.Text ?? "1";

    /// <summary>Tolleranza RGB per RemoveColorOutliers.</summary>
    public string AnomalyOutlierTolText => TxtAnomalyOutlierTol.Text ?? "20";

    /// <summary>
    /// True se la modalità selezionata è Global Chroma-Key, false se Flood (bordo).
    /// </summary>
    public bool IsGlobalChromaKeyMode => BtnBgModeGlobal.IsChecked == true;

    private void UpdateBgModeHint()
    {
        if (BtnBgModeGlobal.IsChecked == true)
        {
            TxtBgModeHint.Text =
                "Global Chroma-Key: rimuove ogni pixel che corrisponde al colore chiave, " +
                "anche isole interne non connesse al bordo. " +
                "Usa tolleranza bassa (5-10) e applica dopo Quantize per evitare falsi positivi. " +
                "Soglia Sobel ignorata.";
            GridSobelControls.Opacity = 0.4;
        }
        else
        {
            TxtBgModeHint.Text =
                "Flood fill dal bordo + punto ◉. " +
                "Se rimangono residui interni: usa ◉ direttamente sul pixel residuo poi 'Rimuovi sfondo'.";
            GridSobelControls.Opacity = 1.0;
        }
    }

    private void Request(SpriteStudioAction action) => ActionRequested?.Invoke(this, action);
}

public sealed record SpriteCleanupState(
    string BackgroundHex,
    string BackgroundTolerance,
    string BackgroundEdgeThreshold,
    bool EnableAlphaThreshold,
    string AlphaThreshold,
    string DefringeOpaque,
    bool EnableOutline,
    string OutlineHex,
    string MinIsland,
    bool EnableMajorityDenoise,
    string MajorityMinNeighbors,
    bool EnableQuantize,
    string QuantizeColors,
    int QuantizeMethodIndex);

using System;
using System.Globalization;
using AiPixelScaler.Core.Pipeline.Imaging;
using AiPixelScaler.Desktop.Utilities;
using SixLabors.ImageSharp.PixelFormats;

namespace AiPixelScaler.Desktop.ViewModels;

public sealed class PipelineViewModel
{
    public enum PresetKind { None, Safe, AggressiveRecover }
    public sealed record PipelineFormState(
        bool EnableChroma,
        bool EnableChromaSnapRgb,
        string ChromaHex,
        string ChromaTolerance,
        bool EnableAdvancedCleaner,
        string BilateralSigmaSpatial,
        string BilateralSigmaRange,
        string BilateralPasses,
        bool EnablePixelGridEnforce,
        string NativeWidth,
        string NativeHeight,
        bool EnablePaletteSnap,
        string PaletteId,
        string PaletteMetadataPath,
        bool EnableQuantize,
        string MaxColors,
        int QuantizerIndex,
        bool EnableMajorityDenoise,
        string MinIsland,
        bool EnableOutline,
        string OutlineHex,
        bool EnableAlphaThreshold,
        string AlphaThreshold);

    public bool EnableChroma { get; set; }
    public bool ChromaSnapRgb { get; set; }
    public string ChromaHex { get; set; } = "#00FF00";
    public int ChromaTolerance { get; set; }

    public bool EnableQuantize { get; set; }
    public int MaxColors { get; set; } = 16;
    public PixelArtProcessor.QuantizerKind Quantizer { get; set; } = PixelArtProcessor.QuantizerKind.KMeansOklab;
    public bool EnableAdvancedCleaner { get; set; }
    public double BilateralSigmaSpatial { get; set; } = 1.25;
    public double BilateralSigmaRange { get; set; } = 0.085;
    public int BilateralPasses { get; set; } = 1;
    public bool EnablePixelGridEnforce { get; set; }
    public int NativeWidth { get; set; } = 64;
    public int NativeHeight { get; set; } = 64;
    public bool EnablePaletteSnap { get; set; }
    public string PaletteId { get; set; } = string.Empty;
    public string PaletteMetadataPath { get; set; } = string.Empty;

    public bool EnableMajorityDenoise { get; set; }
    public int MajorityMinSameNeighbors { get; set; } = 2;
    public int? IslandMinArea { get; set; }

    public bool EnableOutline { get; set; }
    public string OutlineHex { get; set; } = "#000000";
    public byte? AlphaThreshold { get; set; }

    public bool EnableRecoverFill { get; set; }
    public int RecoverIterations { get; set; } = 2;
    public PresetKind ActivePreset { get; set; } = PresetKind.None;

    public void ApplySafePreset()
    {
        EnableChroma = true;
        ChromaSnapRgb = false;
        ChromaTolerance = 6;
        EnableAdvancedCleaner = false;
        EnablePixelGridEnforce = false;
        EnablePaletteSnap = false;
        PaletteId = string.Empty;
        PaletteMetadataPath = string.Empty;
        EnableQuantize = true;
        MaxColors = 32;
        Quantizer = PixelArtProcessor.QuantizerKind.KMeansOklab;
        EnableMajorityDenoise = true;
        IslandMinArea = 2;
        AlphaThreshold = 112;
        EnableRecoverFill = false;
        ActivePreset = PresetKind.Safe;
    }

    public void ApplyAggressiveRecoverPreset()
    {
        EnableChroma = true;
        ChromaSnapRgb = true;
        ChromaTolerance = 18;
        EnableAdvancedCleaner = true;
        BilateralSigmaSpatial = 1.25;
        BilateralSigmaRange = 0.085;
        BilateralPasses = 1;
        EnablePixelGridEnforce = true;
        NativeWidth = 64;
        NativeHeight = 64;
        EnablePaletteSnap = false;
        PaletteId = string.Empty;
        PaletteMetadataPath = string.Empty;
        EnableQuantize = true;
        MaxColors = 20;
        Quantizer = PixelArtProcessor.QuantizerKind.Wu;
        EnableMajorityDenoise = true;
        IslandMinArea = 3;
        AlphaThreshold = 144;
        EnableRecoverFill = true;
        ActivePreset = PresetKind.AggressiveRecover;
    }

    public PixelArtPipeline.Options BuildOptions(Rgba32 chroma, Rgba32 outline, bool includeOutline)
    {
        return new PixelArtPipeline.Options(
            EnableChroma: EnableChroma,
            ChromaSnapRgb: ChromaSnapRgb,
            ChromaColor: chroma,
            ChromaTolerance: Math.Max(0, ChromaTolerance),
            EnableAdvancedCleaner: EnableAdvancedCleaner,
            BilateralSigmaSpatial: Math.Clamp(BilateralSigmaSpatial, 0.5, 6.0),
            BilateralSigmaRange: Math.Clamp(BilateralSigmaRange, 0.01, 0.35),
            BilateralPasses: Math.Clamp(BilateralPasses, 1, 3),
            EnablePixelGridEnforce: EnablePixelGridEnforce,
            NativeWidth: Math.Max(1, NativeWidth),
            NativeHeight: Math.Max(1, NativeHeight),
            EnablePaletteSnap: EnablePaletteSnap,
            PaletteId: string.IsNullOrWhiteSpace(PaletteId) ? null : PaletteId.Trim(),
            EnableQuantize: EnableQuantize,
            MaxColors: Math.Clamp(MaxColors, 2, 256),
            Quantizer: Quantizer,
            EnableMajorityDenoise: EnableMajorityDenoise,
            MajorityMinSameNeighbors: Math.Max(1, MajorityMinSameNeighbors),
            IslandMinArea: IslandMinArea is > 0 ? IslandMinArea : null,
            EnableOutline: includeOutline && EnableOutline,
            OutlineColor: outline,
            AlphaThreshold: AlphaThreshold,
            EnableRecoverFill: EnableRecoverFill,
            RecoverIterations: Math.Max(1, RecoverIterations),
            RequantizeAfterRecover: true);
    }

    public PipelineFormState ToFormState()
    {
        return new PipelineFormState(
            EnableChroma: EnableChroma,
            EnableChromaSnapRgb: ChromaSnapRgb,
            ChromaHex: InputParsing.NormalizeHexRgb(ChromaHex, "#00FF00"),
            ChromaTolerance: Math.Max(0, ChromaTolerance).ToString(CultureInfo.InvariantCulture),
            EnableAdvancedCleaner: EnableAdvancedCleaner,
            BilateralSigmaSpatial: Math.Clamp(BilateralSigmaSpatial, 0.5, 6.0).ToString(CultureInfo.InvariantCulture),
            BilateralSigmaRange: Math.Clamp(BilateralSigmaRange, 0.01, 0.35).ToString(CultureInfo.InvariantCulture),
            BilateralPasses: Math.Clamp(BilateralPasses, 1, 3).ToString(CultureInfo.InvariantCulture),
            EnablePixelGridEnforce: EnablePixelGridEnforce,
            NativeWidth: Math.Max(1, NativeWidth).ToString(CultureInfo.InvariantCulture),
            NativeHeight: Math.Max(1, NativeHeight).ToString(CultureInfo.InvariantCulture),
            EnablePaletteSnap: EnablePaletteSnap,
            PaletteId: (PaletteId ?? string.Empty).Trim(),
            PaletteMetadataPath: (PaletteMetadataPath ?? string.Empty).Trim(),
            EnableQuantize: EnableQuantize,
            MaxColors: Math.Clamp(MaxColors, 2, 256).ToString(CultureInfo.InvariantCulture),
            QuantizerIndex: Quantizer switch
            {
                PixelArtProcessor.QuantizerKind.Wu => 1,
                PixelArtProcessor.QuantizerKind.Octree => 2,
                _ => 0
            },
            EnableMajorityDenoise: EnableMajorityDenoise,
            MinIsland: (IslandMinArea ?? 0).ToString(CultureInfo.InvariantCulture),
            EnableOutline: EnableOutline,
            OutlineHex: InputParsing.NormalizeHexRgb(OutlineHex, "#000000"),
            EnableAlphaThreshold: AlphaThreshold.HasValue,
            AlphaThreshold: (AlphaThreshold ?? (byte)128).ToString(CultureInfo.InvariantCulture));
    }

    public bool TryBuildOptionsFromFormState(PipelineFormState formState, bool includeOutline, out PixelArtPipeline.Options options, out string error)
    {
        options = default!;
        error = string.Empty;

        var chromaEnabled = formState.EnableChroma || formState.EnableChromaSnapRgb;
        var quantEnabled = formState.EnableQuantize;
        var advancedEnabled = formState.EnableAdvancedCleaner;
        var majorityEnabled = formState.EnableMajorityDenoise;
        var alphaEnabled = formState.EnableAlphaThreshold;
        var outlineEnabled = includeOutline && formState.EnableOutline;

        if (!chromaEnabled && !quantEnabled && !advancedEnabled && !majorityEnabled && !alphaEnabled && !outlineEnabled && ActivePreset == PresetKind.None)
        {
            error = "Nessuna trasformazione selezionata: spunta almeno una checkbox sopra.";
            return false;
        }

        var chromaColor = new Rgba32(0, 255, 0, 255);
        if (chromaEnabled && !InputParsing.TryParseHexRgb(formState.ChromaHex, out chromaColor))
        {
            error = "Chroma: hex colore non valido (es. #00FF00).";
            return false;
        }

        var outlineColor = new Rgba32(0, 0, 0, 255);
        if (outlineEnabled && !InputParsing.TryParseHexRgb(formState.OutlineHex, out outlineColor))
        {
            error = "Outline: hex bordo non valido.";
            return false;
        }

        EnableChroma = chromaEnabled;
        ChromaSnapRgb = formState.EnableChromaSnapRgb;
        ChromaHex = InputParsing.NormalizeHexRgb(formState.ChromaHex, "#00FF00");
        ChromaTolerance = Math.Max(0, InputParsing.ParseInt(formState.ChromaTolerance, 0));
        EnableAdvancedCleaner = advancedEnabled;
        BilateralSigmaSpatial = ParseDouble(formState.BilateralSigmaSpatial, 1.25, 0.5, 6.0);
        BilateralSigmaRange = ParseDouble(formState.BilateralSigmaRange, 0.085, 0.01, 0.35);
        BilateralPasses = Math.Clamp(InputParsing.ParseInt(formState.BilateralPasses, 1), 1, 3);
        EnablePixelGridEnforce = formState.EnablePixelGridEnforce;
        NativeWidth = Math.Max(1, InputParsing.ParseInt(formState.NativeWidth, 64));
        NativeHeight = Math.Max(1, InputParsing.ParseInt(formState.NativeHeight, 64));
        EnablePaletteSnap = formState.EnablePaletteSnap;
        PaletteId = (formState.PaletteId ?? string.Empty).Trim();
        PaletteMetadataPath = (formState.PaletteMetadataPath ?? string.Empty).Trim();
        EnableQuantize = quantEnabled;
        MaxColors = Math.Clamp(InputParsing.ParseInt(formState.MaxColors, 16), 2, 256);
        Quantizer = formState.QuantizerIndex switch
        {
            1 => PixelArtProcessor.QuantizerKind.Wu,
            2 => PixelArtProcessor.QuantizerKind.Octree,
            _ => PixelArtProcessor.QuantizerKind.KMeansOklab,
        };
        EnableMajorityDenoise = majorityEnabled;
        var defaultMinIsland = ActivePreset == PresetKind.AggressiveRecover ? 3 : 2;
        IslandMinArea = majorityEnabled ? Math.Max(1, InputParsing.ParseInt(formState.MinIsland, defaultMinIsland)) : null;
        EnableOutline = outlineEnabled;
        OutlineHex = InputParsing.NormalizeHexRgb(formState.OutlineHex, "#000000");
        AlphaThreshold = alphaEnabled ? (byte)Math.Clamp(InputParsing.ParseInt(formState.AlphaThreshold, 128), 0, 255) : null;
        EnableRecoverFill = ActivePreset == PresetKind.AggressiveRecover;

        options = BuildOptions(chromaColor, outlineColor, includeOutline);
        return true;
    }

    private static double ParseDouble(string? s, double fallback, double min, double max)
    {
        if (string.IsNullOrWhiteSpace(s))
            return fallback;
        if (!double.TryParse(s.Trim(), NumberStyles.Float, CultureInfo.InvariantCulture, out var d))
            return fallback;
        return Math.Clamp(d, min, max);
    }

}

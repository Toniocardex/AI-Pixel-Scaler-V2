using System;
using System.Globalization;
using AiPixelScaler.Core.Pipeline.Imaging;
using AiPixelScaler.Desktop.Utilities;
using SixLabors.ImageSharp.PixelFormats;

namespace AiPixelScaler.Desktop.ViewModels;

public sealed class PipelineViewModel
{
    public enum PresetKind { None, Default, Safe, AggressiveRecover }
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

    private sealed record PipelineValidatedSettings(
        bool EnableChroma,
        bool ChromaSnapRgb,
        string ChromaHex,
        int ChromaTolerance,
        bool EnableAdvancedCleaner,
        double BilateralSigmaSpatial,
        double BilateralSigmaRange,
        int BilateralPasses,
        bool EnablePixelGridEnforce,
        int NativeWidth,
        int NativeHeight,
        bool EnablePaletteSnap,
        string PaletteId,
        string PaletteMetadataPath,
        bool EnableQuantize,
        int MaxColors,
        PixelArtProcessor.QuantizerKind Quantizer,
        bool EnableMajorityDenoise,
        int? IslandMinArea,
        bool EnableOutline,
        string OutlineHex,
        byte? AlphaThreshold,
        bool EnableRecoverFill,
        int RecoverIterations);

    public bool EnableChroma { get; set; }
    public bool ChromaSnapRgb { get; set; }
    public string ChromaHex { get; set; } = "#00FF00";
    public int ChromaTolerance { get; set; }

    public bool EnableQuantize { get; set; }
    public int MaxColors { get; set; } = 16;
    public PixelArtProcessor.QuantizerKind Quantizer { get; set; } = PixelArtProcessor.QuantizerKind.Wu;
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

    public void ApplyDefaultPreset()
    {
        EnableChroma = false;
        ChromaSnapRgb = false;
        ChromaTolerance = 0;
        EnableAdvancedCleaner = false;
        BilateralSigmaSpatial = 1.25;
        BilateralSigmaRange = 0.085;
        BilateralPasses = 1;
        EnablePixelGridEnforce = false;
        NativeWidth = 64;
        NativeHeight = 64;
        EnablePaletteSnap = false;
        PaletteId = string.Empty;
        PaletteMetadataPath = string.Empty;
        EnableQuantize = false;
        MaxColors = 16;
        Quantizer = PixelArtProcessor.QuantizerKind.Wu;
        EnableMajorityDenoise = false;
        IslandMinArea = 2;
        EnableOutline = false;
        OutlineHex = "#000000";
        AlphaThreshold = 128;
        EnableRecoverFill = false;
        RecoverIterations = 2;
        ActivePreset = PresetKind.Default;
    }

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
        EnableQuantize = false;
        MaxColors = 16;
        Quantizer = PixelArtProcessor.QuantizerKind.Wu;
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
        EnableQuantize = false;
        MaxColors = 16;
        Quantizer = PixelArtProcessor.QuantizerKind.Wu;
        EnableMajorityDenoise = true;
        IslandMinArea = 3;
        AlphaThreshold = 144;
        EnableRecoverFill = true;
        ActivePreset = PresetKind.AggressiveRecover;
    }

    public PixelArtPipeline.Options BuildOptions(Rgba32 chroma, Rgba32 outline, bool includeOutline)
    {
        var normalized = BuildValidatedSettings();
        return new PixelArtPipeline.Options(
            EnableChroma: normalized.EnableChroma,
            ChromaSnapRgb: normalized.ChromaSnapRgb,
            ChromaColor: chroma,
            ChromaTolerance: normalized.ChromaTolerance,
            EnableAdvancedCleaner: normalized.EnableAdvancedCleaner,
            BilateralSigmaSpatial: normalized.BilateralSigmaSpatial,
            BilateralSigmaRange: normalized.BilateralSigmaRange,
            BilateralPasses: normalized.BilateralPasses,
            EnablePixelGridEnforce: normalized.EnablePixelGridEnforce,
            NativeWidth: normalized.NativeWidth,
            NativeHeight: normalized.NativeHeight,
            EnablePaletteSnap: normalized.EnablePaletteSnap,
            PaletteId: string.IsNullOrWhiteSpace(normalized.PaletteId) ? null : normalized.PaletteId,
            EnableQuantize: normalized.EnableQuantize,
            MaxColors: normalized.MaxColors,
            Quantizer: normalized.Quantizer,
            EnableMajorityDenoise: normalized.EnableMajorityDenoise,
            MajorityMinSameNeighbors: Math.Max(1, MajorityMinSameNeighbors),
            IslandMinArea: normalized.IslandMinArea,
            EnableOutline: includeOutline && normalized.EnableOutline,
            OutlineColor: outline,
            AlphaThreshold: normalized.AlphaThreshold,
            EnableRecoverFill: normalized.EnableRecoverFill,
            RecoverIterations: normalized.RecoverIterations,
            RequantizeAfterRecover: true);
    }

    public PipelineFormState ToFormState()
    {
        var normalized = BuildValidatedSettings();
        return new PipelineFormState(
            EnableChroma: normalized.EnableChroma,
            EnableChromaSnapRgb: normalized.ChromaSnapRgb,
            ChromaHex: normalized.ChromaHex,
            ChromaTolerance: normalized.ChromaTolerance.ToString(CultureInfo.InvariantCulture),
            EnableAdvancedCleaner: normalized.EnableAdvancedCleaner,
            BilateralSigmaSpatial: normalized.BilateralSigmaSpatial.ToString(CultureInfo.InvariantCulture),
            BilateralSigmaRange: normalized.BilateralSigmaRange.ToString(CultureInfo.InvariantCulture),
            BilateralPasses: normalized.BilateralPasses.ToString(CultureInfo.InvariantCulture),
            EnablePixelGridEnforce: normalized.EnablePixelGridEnforce,
            NativeWidth: normalized.NativeWidth.ToString(CultureInfo.InvariantCulture),
            NativeHeight: normalized.NativeHeight.ToString(CultureInfo.InvariantCulture),
            EnablePaletteSnap: normalized.EnablePaletteSnap,
            PaletteId: normalized.PaletteId,
            PaletteMetadataPath: normalized.PaletteMetadataPath,
            EnableQuantize: normalized.EnableQuantize,
            MaxColors: normalized.MaxColors.ToString(CultureInfo.InvariantCulture),
            QuantizerIndex: normalized.Quantizer switch
            {
                PixelArtProcessor.QuantizerKind.Octree => 1,
                _ => 0
            },
            EnableMajorityDenoise: normalized.EnableMajorityDenoise,
            MinIsland: (normalized.IslandMinArea ?? 0).ToString(CultureInfo.InvariantCulture),
            EnableOutline: normalized.EnableOutline,
            OutlineHex: normalized.OutlineHex,
            EnableAlphaThreshold: normalized.AlphaThreshold.HasValue,
            AlphaThreshold: (normalized.AlphaThreshold ?? (byte)128).ToString(CultureInfo.InvariantCulture));
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
        var normalizedTolerance = ParseIntInRange(formState.ChromaTolerance, 0, 0, int.MaxValue);
        ChromaTolerance = normalizedTolerance;
        EnableAdvancedCleaner = advancedEnabled;
        BilateralSigmaSpatial = ParseDoubleInRange(formState.BilateralSigmaSpatial, 1.25, 0.5, 6.0);
        BilateralSigmaRange = ParseDoubleInRange(formState.BilateralSigmaRange, 0.085, 0.01, 0.35);
        BilateralPasses = ParseIntInRange(formState.BilateralPasses, 1, 1, 3);
        EnablePixelGridEnforce = formState.EnablePixelGridEnforce;
        NativeWidth = ParseIntInRange(formState.NativeWidth, 64, 1, int.MaxValue);
        NativeHeight = ParseIntInRange(formState.NativeHeight, 64, 1, int.MaxValue);
        EnablePaletteSnap = formState.EnablePaletteSnap;
        PaletteId = (formState.PaletteId ?? string.Empty).Trim();
        PaletteMetadataPath = (formState.PaletteMetadataPath ?? string.Empty).Trim();
        EnableQuantize = quantEnabled;
        MaxColors = ParseIntInRange(formState.MaxColors, 16, 2, 256);
        Quantizer = formState.QuantizerIndex switch
        {
            1 => PixelArtProcessor.QuantizerKind.Octree,
            _ => PixelArtProcessor.QuantizerKind.Wu,
        };
        EnableMajorityDenoise = majorityEnabled;
        var defaultMinIsland = ActivePreset == PresetKind.AggressiveRecover ? 3 : 2;
        IslandMinArea = majorityEnabled ? ParseIntInRange(formState.MinIsland, defaultMinIsland, 1, int.MaxValue) : null;
        EnableOutline = outlineEnabled;
        OutlineHex = InputParsing.NormalizeHexRgb(formState.OutlineHex, "#000000");
        AlphaThreshold = alphaEnabled ? (byte)ParseIntInRange(formState.AlphaThreshold, 128, 0, 255) : null;
        EnableRecoverFill = ActivePreset == PresetKind.AggressiveRecover;

        options = BuildOptions(chromaColor, outlineColor, includeOutline);
        return true;
    }

    private PipelineValidatedSettings BuildValidatedSettings()
    {
        return new PipelineValidatedSettings(
            EnableChroma: EnableChroma,
            ChromaSnapRgb: ChromaSnapRgb,
            ChromaHex: InputParsing.NormalizeHexRgb(ChromaHex, "#00FF00"),
            ChromaTolerance: ParseIntInRange(ChromaTolerance.ToString(CultureInfo.InvariantCulture), 0, 0, int.MaxValue),
            EnableAdvancedCleaner: EnableAdvancedCleaner,
            BilateralSigmaSpatial: ParseDoubleInRange(BilateralSigmaSpatial.ToString(CultureInfo.InvariantCulture), 1.25, 0.5, 6.0),
            BilateralSigmaRange: ParseDoubleInRange(BilateralSigmaRange.ToString(CultureInfo.InvariantCulture), 0.085, 0.01, 0.35),
            BilateralPasses: ParseIntInRange(BilateralPasses.ToString(CultureInfo.InvariantCulture), 1, 1, 3),
            EnablePixelGridEnforce: EnablePixelGridEnforce,
            NativeWidth: ParseIntInRange(NativeWidth.ToString(CultureInfo.InvariantCulture), 64, 1, int.MaxValue),
            NativeHeight: ParseIntInRange(NativeHeight.ToString(CultureInfo.InvariantCulture), 64, 1, int.MaxValue),
            EnablePaletteSnap: EnablePaletteSnap,
            PaletteId: (PaletteId ?? string.Empty).Trim(),
            PaletteMetadataPath: (PaletteMetadataPath ?? string.Empty).Trim(),
            EnableQuantize: EnableQuantize,
            MaxColors: ParseIntInRange(MaxColors.ToString(CultureInfo.InvariantCulture), 16, 2, 256),
            Quantizer: Quantizer,
            EnableMajorityDenoise: EnableMajorityDenoise,
            IslandMinArea: IslandMinArea is > 0 ? IslandMinArea : null,
            EnableOutline: EnableOutline,
            OutlineHex: InputParsing.NormalizeHexRgb(OutlineHex, "#000000"),
            AlphaThreshold: AlphaThreshold is null ? null : (byte)ParseIntInRange(AlphaThreshold.Value.ToString(CultureInfo.InvariantCulture), 128, 0, 255),
            EnableRecoverFill: EnableRecoverFill,
            RecoverIterations: ParseIntInRange(RecoverIterations.ToString(CultureInfo.InvariantCulture), 1, 1, int.MaxValue));
    }

    private static int ParseIntInRange(string? s, int fallback, int min, int max)
        => Math.Clamp(InputParsing.ParseInt(s, fallback), min, max);

    private static double ParseDoubleInRange(string? s, double fallback, double min, double max)
    {
        if (string.IsNullOrWhiteSpace(s))
            return fallback;
        if (!double.TryParse(s.Trim(), NumberStyles.Float, CultureInfo.InvariantCulture, out var d))
            return fallback;
        return Math.Clamp(d, min, max);
    }

}

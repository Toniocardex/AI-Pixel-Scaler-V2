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
        bool EnableQuantize,
        string MaxColors,
        int QuantizerIndex,
        bool EnableMajorityDenoise,
        string MinIsland,
        bool EnableOutline,
        string OutlineHex,
        bool EnableAlphaThreshold,
        string AlphaThreshold);

    public bool EnableChroma { get; set; } = true;
    public bool ChromaSnapRgb { get; set; }
    public string ChromaHex { get; set; } = "#00FF00";
    public int ChromaTolerance { get; set; }

    public bool EnableQuantize { get; set; } = true;
    public int MaxColors { get; set; } = 16;
    public PixelArtProcessor.QuantizerKind Quantizer { get; set; } = PixelArtProcessor.QuantizerKind.KMeansOklab;

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
        var majorityEnabled = formState.EnableMajorityDenoise;
        var alphaEnabled = formState.EnableAlphaThreshold;
        var outlineEnabled = includeOutline && formState.EnableOutline;

        if (!chromaEnabled && !quantEnabled && !majorityEnabled && !alphaEnabled && !outlineEnabled && ActivePreset == PresetKind.None)
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

}

using System;
using AiPixelScaler.Core.Pipeline.Imaging;
using SixLabors.ImageSharp.PixelFormats;

namespace AiPixelScaler.Desktop.ViewModels;

public sealed class PipelineViewModel
{
    public enum PresetKind { None, Safe, AggressiveRecover }

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
}

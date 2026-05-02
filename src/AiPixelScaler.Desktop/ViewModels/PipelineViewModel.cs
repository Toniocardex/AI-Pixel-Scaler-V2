using System;
using System.Globalization;
using AiPixelScaler.Core.Pipeline.Imaging;
using AiPixelScaler.Desktop.Utilities;
using SixLabors.ImageSharp.PixelFormats;

namespace AiPixelScaler.Desktop.ViewModels;

public sealed class PipelineViewModel
{
    public enum PresetKind { None, Default, Safe, Aggressive }
    public sealed record PipelineFormState(
        bool EnableBackgroundIsolation,
        bool EnableBackgroundSnapRgb,
        string BackgroundHex,
        string BackgroundTolerance,
        bool EnableQuantize,
        string MaxColors,
        int QuantizerIndex,
        bool EnableMajorityDenoise,
        string MinIsland,
        bool EnableOutline,
        string OutlineHex,
        bool EnableAlphaThreshold,
        string AlphaThreshold,
        string DefringeOpaque = "250");

    private sealed record PipelineValidatedSettings(
        bool EnableBackgroundIsolation,
        bool BackgroundSnapRgb,
        string BackgroundHex,
        int BackgroundTolerance,
        bool EnableQuantize,
        int MaxColors,
        PixelArtProcessor.QuantizerKind Quantizer,
        bool EnableMajorityDenoise,
        int? IslandMinArea,
        bool EnableOutline,
        string OutlineHex,
        byte? AlphaThreshold);

    public bool EnableBackgroundIsolation { get; set; }
    public bool BackgroundSnapRgb { get; set; }
    public string BackgroundHex { get; set; } = "#00FF00";
    public int BackgroundTolerance { get; set; }

    public bool EnableQuantize { get; set; }
    public int MaxColors { get; set; } = 16;
    public PixelArtProcessor.QuantizerKind Quantizer { get; set; } = PixelArtProcessor.QuantizerKind.Wu;

    public bool EnableMajorityDenoise { get; set; }
    public int MajorityMinSameNeighbors { get; set; } = 2;
    public int? IslandMinArea { get; set; }

    public bool EnableOutline { get; set; }
    public string OutlineHex { get; set; } = "#000000";
    public byte? AlphaThreshold { get; set; }

    public PresetKind ActivePreset { get; set; } = PresetKind.None;

    public void ApplyDefaultPreset()
    {
        EnableBackgroundIsolation = false;
        BackgroundSnapRgb = false;
        BackgroundTolerance = 0;
        EnableQuantize = false;
        MaxColors = 16;
        Quantizer = PixelArtProcessor.QuantizerKind.Wu;
        EnableMajorityDenoise = false;
        IslandMinArea = 2;
        EnableOutline = false;
        OutlineHex = "#000000";
        AlphaThreshold = 128;
        ActivePreset = PresetKind.Default;
    }

    public void ApplySafePreset()
    {
        EnableBackgroundIsolation = true;
        BackgroundSnapRgb = false;
        BackgroundTolerance = 6;
        EnableQuantize = false;
        MaxColors = 16;
        Quantizer = PixelArtProcessor.QuantizerKind.Wu;
        EnableMajorityDenoise = true;
        IslandMinArea = 2;
        AlphaThreshold = 112;
        ActivePreset = PresetKind.Safe;
    }

    public void ApplyAggressivePreset()
    {
        EnableBackgroundIsolation = true;
        BackgroundSnapRgb = true;
        BackgroundTolerance = 18;
        EnableQuantize = false;
        MaxColors = 16;
        Quantizer = PixelArtProcessor.QuantizerKind.Wu;
        EnableMajorityDenoise = true;
        IslandMinArea = 3;
        AlphaThreshold = 144;
        ActivePreset = PresetKind.Aggressive;
    }

    public PixelArtPipeline.Options BuildOptions(Rgba32 background, Rgba32 outline, bool includeOutline)
    {
        var normalized = BuildValidatedSettings();
        return new PixelArtPipeline.Options(
            EnableBackgroundIsolation: normalized.EnableBackgroundIsolation,
            BackgroundSnapRgb: normalized.BackgroundSnapRgb,
            BackgroundColor: background,
            BackgroundTolerance: normalized.BackgroundTolerance,
            EnableQuantize: normalized.EnableQuantize,
            MaxColors: normalized.MaxColors,
            Quantizer: normalized.Quantizer,
            EnableMajorityDenoise: normalized.EnableMajorityDenoise,
            MajorityMinSameNeighbors: Math.Max(1, MajorityMinSameNeighbors),
            IslandMinArea: normalized.IslandMinArea,
            EnableOutline: includeOutline && normalized.EnableOutline,
            OutlineColor: outline,
            AlphaThreshold: normalized.AlphaThreshold);
    }

    public PipelineFormState ToFormState()
    {
        var normalized = BuildValidatedSettings();
        return new PipelineFormState(
            EnableBackgroundIsolation: normalized.EnableBackgroundIsolation,
            EnableBackgroundSnapRgb: normalized.BackgroundSnapRgb,
            BackgroundHex: normalized.BackgroundHex,
            BackgroundTolerance: normalized.BackgroundTolerance.ToString(CultureInfo.InvariantCulture),
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
            AlphaThreshold: (normalized.AlphaThreshold ?? (byte)128).ToString(CultureInfo.InvariantCulture),
            DefringeOpaque: "250");
    }

    public bool TryBuildOptionsFromFormState(PipelineFormState formState, bool includeOutline, out PixelArtPipeline.Options options, out string error)
    {
        options = default!;
        error = string.Empty;

        var backgroundEnabled = formState.EnableBackgroundIsolation || formState.EnableBackgroundSnapRgb;
        var quantEnabled = formState.EnableQuantize;
        var majorityEnabled = formState.EnableMajorityDenoise;
        var alphaEnabled = formState.EnableAlphaThreshold;
        var outlineEnabled = includeOutline && formState.EnableOutline;

        if (!backgroundEnabled && !quantEnabled && !majorityEnabled && !alphaEnabled && !outlineEnabled && ActivePreset == PresetKind.None)
        {
            error = "Nessuna trasformazione selezionata: spunta almeno una checkbox sopra.";
            return false;
        }

        var backgroundColor = new Rgba32(0, 255, 0, 255);
        if (backgroundEnabled && !InputParsing.TryParseHexRgb(formState.BackgroundHex, out backgroundColor))
        {
            error = "Sfondo: hex colore non valido (es. #00FF00).";
            return false;
        }

        var outlineColor = new Rgba32(0, 0, 0, 255);
        if (outlineEnabled && !InputParsing.TryParseHexRgb(formState.OutlineHex, out outlineColor))
        {
            error = "Outline: hex bordo non valido.";
            return false;
        }

        EnableBackgroundIsolation = backgroundEnabled;
        BackgroundSnapRgb = formState.EnableBackgroundSnapRgb;
        BackgroundHex = InputParsing.NormalizeHexRgb(formState.BackgroundHex, "#00FF00");
        var normalizedTolerance = ParseIntInRange(formState.BackgroundTolerance, 0, 0, int.MaxValue);
        BackgroundTolerance = normalizedTolerance;
        EnableQuantize = quantEnabled;
        MaxColors = ParseIntInRange(formState.MaxColors, 16, 2, 256);
        Quantizer = formState.QuantizerIndex switch
        {
            1 => PixelArtProcessor.QuantizerKind.Octree,
            _ => PixelArtProcessor.QuantizerKind.Wu,
        };
        EnableMajorityDenoise = majorityEnabled;
        var defaultMinIsland = ActivePreset == PresetKind.Aggressive ? 3 : 2;
        IslandMinArea = majorityEnabled ? ParseIntInRange(formState.MinIsland, defaultMinIsland, 1, int.MaxValue) : null;
        EnableOutline = outlineEnabled;
        OutlineHex = InputParsing.NormalizeHexRgb(formState.OutlineHex, "#000000");
        AlphaThreshold = alphaEnabled ? (byte)ParseIntInRange(formState.AlphaThreshold, 128, 0, 255) : null;

        options = BuildOptions(backgroundColor, outlineColor, includeOutline);
        return true;
    }

    private PipelineValidatedSettings BuildValidatedSettings()
    {
        return new PipelineValidatedSettings(
            EnableBackgroundIsolation: EnableBackgroundIsolation,
            BackgroundSnapRgb: BackgroundSnapRgb,
            BackgroundHex: InputParsing.NormalizeHexRgb(BackgroundHex, "#00FF00"),
            BackgroundTolerance: ParseIntInRange(BackgroundTolerance.ToString(CultureInfo.InvariantCulture), 0, 0, int.MaxValue),
            EnableQuantize: EnableQuantize,
            MaxColors: ParseIntInRange(MaxColors.ToString(CultureInfo.InvariantCulture), 16, 2, 256),
            Quantizer: Quantizer,
            EnableMajorityDenoise: EnableMajorityDenoise,
            IslandMinArea: IslandMinArea is > 0 ? IslandMinArea : null,
            EnableOutline: EnableOutline,
            OutlineHex: InputParsing.NormalizeHexRgb(OutlineHex, "#000000"),
            AlphaThreshold: AlphaThreshold is null ? null : (byte)ParseIntInRange(AlphaThreshold.Value.ToString(CultureInfo.InvariantCulture), 128, 0, 255));
    }

    private static int ParseIntInRange(string? s, int fallback, int min, int max)
        => Math.Clamp(InputParsing.ParseInt(s, fallback), min, max);

}

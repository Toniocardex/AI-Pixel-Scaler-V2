using SixLabors.ImageSharp.PixelFormats;

namespace AiPixelScaler.Core.Pipeline.Imaging;

public static class PaletteIdResolver
{
    public static bool TryResolve(string? paletteId, out IReadOnlyList<Rgba32> palette)
    {
        palette = [];
        if (string.IsNullOrWhiteSpace(paletteId))
            return false;

        var key = paletteId.Trim().ToLowerInvariant();
        palette = key switch
        {
            "gameboy" or "gameboydmg" or "dmg" => PalettePresets.Get(PalettePresets.Preset.GameBoyDMG),
            "pico8" or "pico-8" => PalettePresets.Get(PalettePresets.Preset.Pico8),
            "nes" or "nes16" => PalettePresets.Get(PalettePresets.Preset.NES16),
            "cga" or "cga4" => PalettePresets.Get(PalettePresets.Preset.CGA4),
            "sweetie16" or "sweetie-16" => PalettePresets.Get(PalettePresets.Preset.Sweetie16),
            _ => []
        };

        return palette.Count > 0;
    }
}

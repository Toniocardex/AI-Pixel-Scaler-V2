using System.Globalization;
using SixLabors.ImageSharp.PixelFormats;

namespace AiPixelScaler.Core.Utilities;

public static class ColorParsing
{
    public static bool TryParseHexRgb(string? s, out Rgba32 color)
    {
        color = default;
        if (string.IsNullOrWhiteSpace(s)) return false;
        var t = s.Trim();
        if (t.StartsWith("#", StringComparison.Ordinal)) t = t[1..];
        if (t.Length != 6) return false;
        if (!uint.TryParse(t, NumberStyles.HexNumber, CultureInfo.InvariantCulture, out var v)) return false;
        color = new Rgba32((byte)(v >> 16), (byte)(v >> 8), (byte)v, 255);
        return true;
    }

    public static string NormalizeHexRgb(string? value, string fallback)
    {
        if (TryParseHexRgb(value, out var color))
            return $"#{color.R:X2}{color.G:X2}{color.B:X2}";
        return fallback;
    }
}

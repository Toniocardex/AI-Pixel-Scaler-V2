using AiPixelScaler.Core.Utilities;
using SixLabors.ImageSharp.PixelFormats;

namespace AiPixelScaler.Desktop.Utilities;

internal static class InputParsing
{
    public static int ParseInt(string? s, int fallback) => OptionParsing.ParseInt(s, fallback);

    public static bool TryParseHexRgb(string? s, out Rgba32 color) => ColorParsing.TryParseHexRgb(s, out color);

    public static string NormalizeHexRgb(string? value, string fallback) => ColorParsing.NormalizeHexRgb(value, fallback);
}

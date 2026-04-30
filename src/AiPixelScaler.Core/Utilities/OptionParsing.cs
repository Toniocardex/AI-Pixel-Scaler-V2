using System.Globalization;

namespace AiPixelScaler.Core.Utilities;

public static class OptionParsing
{
    public static int ParseInt(string? s, int fallback)
    {
        if (string.IsNullOrWhiteSpace(s)) return fallback;
        return int.TryParse(s.Trim(), out var n) ? n : fallback;
    }

    public static int? ParseFlagInt(string[] args, string flag)
    {
        for (var i = 0; i < args.Length - 1; i++)
        {
            if (!string.Equals(args[i], flag, StringComparison.OrdinalIgnoreCase)) continue;
            return int.TryParse(args[i + 1], NumberStyles.Integer, CultureInfo.InvariantCulture, out var n) ? n : null;
        }

        return null;
    }
}

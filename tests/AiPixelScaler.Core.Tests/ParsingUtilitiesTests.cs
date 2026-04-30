using AiPixelScaler.Core.Utilities;
using SixLabors.ImageSharp.PixelFormats;

namespace AiPixelScaler.Core.Tests;

public class ParsingUtilitiesTests
{
    [Fact]
    public void ColorParsing_TryParseHexRgb_accepts_hash_and_normalizes_case()
    {
        var ok = ColorParsing.TryParseHexRgb("#aBcD09", out var c);

        Assert.True(ok);
        Assert.Equal(new Rgba32(0xAB, 0xCD, 0x09, 255), c);
    }

    [Fact]
    public void ColorParsing_TryParseHexRgb_rejects_invalid_text()
    {
        Assert.False(ColorParsing.TryParseHexRgb("not-a-color", out _));
        Assert.False(ColorParsing.TryParseHexRgb("#12345", out _));
    }

    [Fact]
    public void ColorParsing_NormalizeHexRgb_falls_back_on_invalid()
    {
        var normalized = ColorParsing.NormalizeHexRgb("invalid", "#00FF00");

        Assert.Equal("#00FF00", normalized);
    }

    [Fact]
    public void OptionParsing_ParseInt_returns_fallback_for_invalid_or_empty()
    {
        Assert.Equal(7, OptionParsing.ParseInt(null, 7));
        Assert.Equal(7, OptionParsing.ParseInt("", 7));
        Assert.Equal(7, OptionParsing.ParseInt("abc", 7));
        Assert.Equal(42, OptionParsing.ParseInt("42", 7));
    }

    [Fact]
    public void OptionParsing_ParseFlagInt_parses_flag_value_case_insensitive()
    {
        var args = new[] { "--palette", "16" };
        var argsCase = new[] { "--PALETTE", "32" };

        Assert.Equal(16, OptionParsing.ParseFlagInt(args, "--palette"));
        Assert.Equal(32, OptionParsing.ParseFlagInt(argsCase, "--palette"));
        Assert.Null(OptionParsing.ParseFlagInt(new[] { "--palette", "x" }, "--palette"));
    }
}

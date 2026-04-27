using System.Globalization;
using AiPixelScaler.Core.Pipeline.Imaging;
using SixLabors.ImageSharp;
using SixLabors.ImageSharp.PixelFormats;

namespace AiPixelScaler.Desktop.Services;

internal static class PipelineExecutionService
{
    internal sealed record ExecutionResult(
        bool Succeeded,
        string LastRunText,
        string StatusText,
        string PaletteText,
        string ColorsBeforeText,
        string ColorsAfterText);

    public static ExecutionResult RunInPlace(
        Image<Rgba32> image,
        PixelArtPipeline.Options options,
        string label,
        Func<Rgba32, string> toHex)
    {
        var start = DateTime.UtcNow;
        try
        {
            var report = PixelArtPipeline.ApplyInPlace(image, options);
            var elapsed = (int)(DateTime.UtcNow - start).TotalMilliseconds;
            return new ExecutionResult(
                Succeeded: true,
                LastRunText: $"Ultima esecuzione: {label}, {elapsed} ms, colori {report.UniqueColorsBefore}→{report.UniqueColorsAfter}.",
                StatusText: $"{label} applicata: {string.Join(" → ", report.Steps)}.",
                PaletteText: string.Join(" ", report.Palette.Select(toHex)),
                ColorsBeforeText: report.UniqueColorsBefore.ToString(CultureInfo.InvariantCulture),
                ColorsAfterText: report.UniqueColorsAfter.ToString(CultureInfo.InvariantCulture));
        }
        catch (Exception ex)
        {
            return new ExecutionResult(
                Succeeded: false,
                LastRunText: $"Ultima esecuzione fallita: {label}.",
                StatusText: $"{label}: {ex.Message}",
                PaletteText: string.Empty,
                ColorsBeforeText: string.Empty,
                ColorsAfterText: string.Empty);
        }
    }
}

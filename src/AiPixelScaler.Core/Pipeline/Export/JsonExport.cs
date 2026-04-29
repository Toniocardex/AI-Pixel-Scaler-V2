using System.Text.Json;
using System.Text.Json.Serialization;

namespace AiPixelScaler.Core.Pipeline.Export;

public sealed class SpriteSheetMetadata
{
    public string? PaletteId { get; init; }
    public List<SpriteCellEntry> Cells { get; init; } = new();
}

public sealed class SpriteCellEntry
{
    public required string Id { get; init; }
    public int X { get; init; }
    public int Y { get; init; }
    public int Width { get; init; }
    public int Height { get; init; }
    public double PivotNdcX { get; init; }
    public double PivotNdcY { get; init; }
}

public static class JsonExport
{
    private static readonly JsonSerializerOptions Options = new()
    {
        WriteIndented = true,
        DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingNull
    };

    public static string Serialize(SpriteSheetMetadata metadata) =>
        JsonSerializer.Serialize(metadata, Options);

    public static SpriteSheetMetadata? Deserialize(string json) =>
        JsonSerializer.Deserialize<SpriteSheetMetadata>(json, Options);
}

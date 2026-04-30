using System;
using System.Collections.Generic;
using AiPixelScaler.Core.Pipeline.Slicing;
using SixLabors.ImageSharp;
using SixLabors.ImageSharp.PixelFormats;

namespace AiPixelScaler.Desktop.ViewModels;

public sealed class WorkspaceTabViewModel : IDisposable
{
    public required string Title { get; init; }
    public required Image<Rgba32> Document { get; init; }
    public required Image<Rgba32> Backup { get; init; }
    public required List<SpriteCell> Cells { get; init; }
    public required List<WorkspaceUndoSnapshot> UndoStack { get; init; }
    public required bool HasUserFile { get; init; }
    public required int GridRows { get; init; }
    public required int GridCols { get; init; }
    public required List<SpriteCell> SpriteOverlay { get; init; }
    public bool IsDirty { get; set; }
    public bool IsAdvancedMode { get; set; }

    public void Dispose()
    {
        Document.Dispose();
        Backup.Dispose();
        foreach (var s in UndoStack)
            s.Image.Dispose();
        UndoStack.Clear();
    }
}

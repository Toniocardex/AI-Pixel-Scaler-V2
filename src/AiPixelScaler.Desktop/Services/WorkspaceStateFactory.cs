using System;
using System.Collections.Generic;
using System.Linq;
using AiPixelScaler.Core.Pipeline.Slicing;
using AiPixelScaler.Desktop.ViewModels;
using SixLabors.ImageSharp;
using SixLabors.ImageSharp.PixelFormats;

namespace AiPixelScaler.Desktop.Services;

internal static class WorkspaceStateFactory
{
    internal static List<WorkspaceUndoSnapshot> CloneUndoStack(IEnumerable<WorkspaceUndoSnapshot> src) =>
        src.Select(s => WorkspaceUndoSnapshot.Capture(s.Image, s.Cells, s.GridRows, s.GridCols, s.SpriteOverlay)).ToList();

    internal static WorkspaceTabViewModel CaptureFromRuntime(
        string title,
        bool isDirty,
        Image<Rgba32>? document,
        Image<Rgba32>? backup,
        IReadOnlyList<SpriteCell> cells,
        IReadOnlyList<WorkspaceUndoSnapshot> undoStack,
        bool hasUserFile,
        bool isAdvancedMode,
        int gridRows,
        int gridCols,
        IReadOnlyList<SpriteCell> spriteOverlay,
        Func<Image<Rgba32>> createFallbackDocument)
    {
        using var fallbackDoc = createFallbackDocument();
        return new WorkspaceTabViewModel
        {
            Title = title,
            Document = (document ?? fallbackDoc).Clone(),
            Backup = (backup ?? document ?? fallbackDoc).Clone(),
            Cells = WorkspaceUndoSnapshot.CloneCells(cells),
            UndoStack = CloneUndoStack(undoStack),
            HasUserFile = hasUserFile,
            IsAdvancedMode = isAdvancedMode,
            GridRows = gridRows,
            GridCols = gridCols,
            SpriteOverlay = WorkspaceUndoSnapshot.CloneCells(spriteOverlay),
            IsDirty = isDirty
        };
    }

    internal static WorkspaceTabViewModel CreateWelcome(string title, Func<Image<Rgba32>> createWelcomeDocument)
    {
        using var welcome = createWelcomeDocument();
        return new WorkspaceTabViewModel
        {
            Title = title,
            Document = welcome.Clone(),
            Backup = welcome.Clone(),
            Cells = [],
            UndoStack = [],
            HasUserFile = false,
            IsAdvancedMode = false,
            GridRows = 0,
            GridCols = 0,
            SpriteOverlay = [],
            IsDirty = false
        };
    }

    internal static WorkspaceTabViewModel CreateFromImage(Image<Rgba32> image, string title)
    {
        return new WorkspaceTabViewModel
        {
            Title = title,
            Document = image.Clone(),
            Backup = image.Clone(),
            Cells = [],
            UndoStack = [],
            HasUserFile = true,
            IsAdvancedMode = false,
            GridRows = 0,
            GridCols = 0,
            SpriteOverlay = [],
            IsDirty = false
        };
    }
}

using System;
using System.Collections.Generic;
using AiPixelScaler.Core.Pipeline.Slicing;
using AiPixelScaler.Desktop.ViewModels;
using SixLabors.ImageSharp;
using SixLabors.ImageSharp.PixelFormats;

namespace AiPixelScaler.Desktop.Services;

internal static class WorkspaceRuntimeAdapter
{
    internal sealed class RuntimeSnapshot
    {
        public required Image<Rgba32> Document { get; init; }
        public required Image<Rgba32> Backup { get; init; }
        public required List<SpriteCell> Cells { get; init; }
        public required List<WorkspaceUndoSnapshot> UndoStack { get; init; }
        public required bool HasUserFile { get; init; }
        public required bool CleanApplied { get; init; }
        public required int GridRows { get; init; }
        public required int GridCols { get; init; }
        public required List<SpriteCell> SpriteOverlay { get; init; }
    }

    internal static WorkspaceTabViewModel CaptureTabState(
        string title,
        bool isDirty,
        Image<Rgba32>? document,
        Image<Rgba32>? backup,
        IReadOnlyList<SpriteCell> cells,
        IReadOnlyList<WorkspaceUndoSnapshot> undoStack,
        bool hasUserFile,
        bool cleanApplied,
        bool isAdvancedMode,
        int gridRows,
        int gridCols,
        IReadOnlyList<SpriteCell> spriteOverlay,
        Func<Image<Rgba32>> createFallbackDocument)
        => WorkspaceStateFactory.CaptureFromRuntime(
            title,
            isDirty,
            document,
            backup,
            cells,
            undoStack,
            hasUserFile,
            cleanApplied,
            isAdvancedMode,
            gridRows,
            gridCols,
            spriteOverlay,
            createFallbackDocument);

    internal static RuntimeSnapshot RestoreRuntimeSnapshot(WorkspaceTabViewModel tab) =>
        new()
        {
            Document = tab.Document.Clone(),
            Backup = tab.Backup.Clone(),
            Cells = WorkspaceUndoSnapshot.CloneCells(tab.Cells),
            UndoStack = WorkspaceStateFactory.CloneUndoStack(tab.UndoStack),
            HasUserFile = tab.HasUserFile,
            CleanApplied = tab.CleanApplied,
            GridRows = tab.GridRows,
            GridCols = tab.GridCols,
            SpriteOverlay = WorkspaceUndoSnapshot.CloneCells(tab.SpriteOverlay)
        };
}

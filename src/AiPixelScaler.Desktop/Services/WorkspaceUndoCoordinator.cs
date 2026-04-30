using System.Collections.Generic;
using AiPixelScaler.Core.Pipeline.Slicing;
using AiPixelScaler.Desktop.ViewModels;
using Avalonia.Controls;
using SixLabors.ImageSharp;
using SixLabors.ImageSharp.PixelFormats;

namespace AiPixelScaler.Desktop.Services;

internal sealed class WorkspaceUndoCoordinator
{
    private readonly List<WorkspaceUndoSnapshot> _stack = new();
    private readonly MenuItem _menuUndo;
    private readonly Button _btnUndo;
    private readonly int _maxUndo;
    private readonly System.Action _afterPush;

    public WorkspaceUndoCoordinator(MenuItem menuUndo, Button btnUndo, int maxUndo, System.Action afterPush)
    {
        _menuUndo = menuUndo;
        _btnUndo = btnUndo;
        _maxUndo = maxUndo;
        _afterPush = afterPush;
    }

    public IReadOnlyList<WorkspaceUndoSnapshot> Snapshots => _stack;

    public bool Push(
        Image<Rgba32>? document,
        IReadOnlyList<SpriteCell> cells,
        int gridRows,
        int gridCols,
        IReadOnlyList<SpriteCell> spriteOverlay)
    {
        if (document is null)
            return false;

        _stack.Add(WorkspaceUndoSnapshot.Capture(document, cells, gridRows, gridCols, spriteOverlay));
        _afterPush();

        while (_stack.Count > _maxUndo)
        {
            var drop = _stack[0];
            _stack.RemoveAt(0);
            drop.Image.Dispose();
        }

        UpdateUndoUi();
        return true;
    }

    public bool TryPop(out WorkspaceUndoSnapshot? snapshot)
    {
        if (_stack.Count == 0)
        {
            snapshot = null;
            return false;
        }

        snapshot = _stack[^1];
        _stack.RemoveAt(_stack.Count - 1);
        UpdateUndoUi();
        return true;
    }

    public void ClearAndDisposeAll()
    {
        foreach (var s in _stack)
            s.Image.Dispose();
        _stack.Clear();
        UpdateUndoUi();
    }

    /// <summary>Sostituisce lo stack con uno già clonato (es. ripristino tab).</summary>
    public void ImportReplacing(List<WorkspaceUndoSnapshot> incoming)
    {
        foreach (var s in _stack)
            s.Image.Dispose();
        _stack.Clear();
        _stack.AddRange(incoming);
        UpdateUndoUi();
    }

    public void UpdateUndoUi()
    {
        var can = _stack.Count > 0;
        _btnUndo.IsEnabled = can;
        _menuUndo.IsEnabled = can;
    }
}

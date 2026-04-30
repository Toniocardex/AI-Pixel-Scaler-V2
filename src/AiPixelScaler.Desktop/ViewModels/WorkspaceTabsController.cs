using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;

namespace AiPixelScaler.Desktop.ViewModels;

/// <summary>
/// Gestisce ciclo di vita e metadati UI dei workspace tab.
/// Non conosce la UI: espone header, stato attivo e contesto.
/// </summary>
public sealed class WorkspaceTabsController : IDisposable
{
    private readonly ObservableCollection<string> _headers = [];
    private readonly List<WorkspaceTabViewModel> _tabs = [];

    public ReadOnlyObservableCollection<string> Headers { get; }
    public int ActiveIndex { get; private set; } = -1;
    public bool CanClose => _tabs.Count > 1;
    public int Count => _tabs.Count;
    public string ContextText { get; private set; } = "Nessun workspace";

    public WorkspaceTabViewModel? ActiveTab =>
        ActiveIndex >= 0 && ActiveIndex < _tabs.Count ? _tabs[ActiveIndex] : null;

    public WorkspaceTabsController()
    {
        Headers = new ReadOnlyObservableCollection<string>(_headers);
    }

    public void Initialize(WorkspaceTabViewModel initial)
    {
        DisposeTabs();
        _tabs.Add(initial);
        ActiveIndex = 0;
        RefreshChrome();
    }

    public bool TryActivate(int index)
    {
        if (index < 0 || index >= _tabs.Count)
            return false;
        ActiveIndex = index;
        RefreshChrome();
        return true;
    }

    public void ReplaceActive(WorkspaceTabViewModel replacement)
    {
        if (ActiveIndex < 0 || ActiveIndex >= _tabs.Count)
            return;
        _tabs[ActiveIndex].Dispose();
        _tabs[ActiveIndex] = replacement;
        RefreshChrome();
    }

    public void AddAndActivate(WorkspaceTabViewModel state)
    {
        _tabs.Add(state);
        ActiveIndex = _tabs.Count - 1;
        RefreshChrome();
    }

    public bool TryCloseActive()
    {
        if (_tabs.Count <= 1 || ActiveIndex < 0 || ActiveIndex >= _tabs.Count)
            return false;
        _tabs[ActiveIndex].Dispose();
        _tabs.RemoveAt(ActiveIndex);
        ActiveIndex = Math.Clamp(ActiveIndex - 1, 0, _tabs.Count - 1);
        RefreshChrome();
        return true;
    }

    public void MarkActiveDirty(bool dirty = true)
    {
        var active = ActiveTab;
        if (active is null) return;
        active.IsDirty = dirty;
        RefreshChrome();
    }

    public void MarkActiveClean() => MarkActiveDirty(false);

    private static string DisplayTitle(WorkspaceTabViewModel ws) =>
        ws.IsDirty ? $"{ws.Title} *" : ws.Title;

    private void RefreshChrome()
    {
        _headers.Clear();
        foreach (var tab in _tabs)
            _headers.Add(DisplayTitle(tab));

        var active = ActiveTab;
        ContextText = active is null
            ? "Nessun workspace"
            : $"{active.Title} · {(active.IsDirty ? "Modificato" : "Pulito")}";
    }

    private void DisposeTabs()
    {
        foreach (var tab in _tabs)
            tab.Dispose();
        _tabs.Clear();
        _headers.Clear();
        ActiveIndex = -1;
    }

    public void Dispose() => DisposeTabs();
}

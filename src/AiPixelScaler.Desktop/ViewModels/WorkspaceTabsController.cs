using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;

namespace AiPixelScaler.Desktop.ViewModels;

/// <summary>
/// Gestisce ciclo di vita e metadati UI dei workspace tab.
/// Non conosce la UI: espone strip items, stato attivo e contesto.
/// </summary>
public sealed class WorkspaceTabsController : IDisposable
{
    private readonly ObservableCollection<WorkspaceTabStripItem> _stripItems = [];
    private readonly List<WorkspaceTabViewModel> _tabs = [];

    public ReadOnlyObservableCollection<WorkspaceTabStripItem> StripItems { get; }

    public int ActiveIndex { get; private set; } = -1;
    public bool CanClose => _tabs.Count > 1;
    public int Count => _tabs.Count;
    public string ContextText { get; private set; } = "Nessun workspace";

    public WorkspaceTabViewModel? ActiveTab =>
        ActiveIndex >= 0 && ActiveIndex < _tabs.Count ? _tabs[ActiveIndex] : null;

    public WorkspaceTabsController()
    {
        StripItems = new ReadOnlyObservableCollection<WorkspaceTabStripItem>(_stripItems);
    }

    public int IndexOfStripItem(WorkspaceTabStripItem item) => _stripItems.IndexOf(item);

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

    /// <summary>Chiude il tab all’indice dato. Aggiorna <see cref="ActiveIndex"/> e lo strip.</summary>
    public bool TryCloseAt(int index)
    {
        if (_tabs.Count <= 1 || index < 0 || index >= _tabs.Count)
            return false;

        _tabs[index].Dispose();
        _tabs.RemoveAt(index);

        if (index < ActiveIndex)
            ActiveIndex--;
        else if (index == ActiveIndex)
            ActiveIndex = Math.Clamp(index - 1, 0, _tabs.Count - 1);

        RefreshChrome();
        return true;
    }

    public bool TryCloseActive() => TryCloseAt(ActiveIndex);

    public void MarkActiveDirty(bool dirty = true)
    {
        var active = ActiveTab;
        if (active is null) return;
        active.IsDirty = dirty;
        RefreshChrome();
    }

    public void MarkActiveClean() => MarkActiveDirty(false);

    private void RefreshChrome()
    {
        var allowClose = _tabs.Count > 1;

        // Non usare Clear(): TabControl + SelectedIndex può crashare se si svuota la ItemsSource.
        for (var i = 0; i < _tabs.Count; i++)
        {
            var tab = _tabs[i];
            if (i < _stripItems.Count)
            {
                var strip = _stripItems[i];
                strip.AttachTab(tab);
                strip.SyncDisplay(allowClose);
            }
            else
            {
                var strip = new WorkspaceTabStripItem();
                strip.AttachTab(tab);
                strip.SyncDisplay(allowClose);
                _stripItems.Add(strip);
            }
        }

        while (_stripItems.Count > _tabs.Count)
            _stripItems.RemoveAt(_stripItems.Count - 1);

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
        while (_stripItems.Count > 0)
            _stripItems.RemoveAt(_stripItems.Count - 1);
        ActiveIndex = -1;
    }

    public void Dispose() => DisposeTabs();
}

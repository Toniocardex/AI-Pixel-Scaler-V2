using System.ComponentModel;
using System.Runtime.CompilerServices;

namespace AiPixelScaler.Desktop.ViewModels;

/// <summary>Voce strip workspace (titolo + stato X) per TabControl; aggiornata da <see cref="WorkspaceTabsController"/>.</summary>
public sealed class WorkspaceTabStripItem : INotifyPropertyChanged
{
    private WorkspaceTabViewModel _tab = null!;
    private string _displayTitle = "";
    private bool _closeButtonVisible;

    public WorkspaceTabViewModel Tab => _tab;

    public string DisplayTitle
    {
        get => _displayTitle;
        private set => SetField(ref _displayTitle, value);
    }

    public bool CloseButtonVisible
    {
        get => _closeButtonVisible;
        private set => SetField(ref _closeButtonVisible, value);
    }

    public event PropertyChangedEventHandler? PropertyChanged;

    internal void AttachTab(WorkspaceTabViewModel tab)
    {
        _tab = tab;
    }

    internal void SyncDisplay(bool allowClose)
    {
        DisplayTitle = Tab.IsDirty ? $"{Tab.Title} *" : Tab.Title;
        CloseButtonVisible = allowClose;
    }

    private void SetField<T>(ref T field, T value, [CallerMemberName] string? name = null)
    {
        if (Equals(field, value))
            return;
        field = value;
        PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(name));
    }
}

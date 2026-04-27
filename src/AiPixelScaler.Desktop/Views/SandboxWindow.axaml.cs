using Avalonia.Controls;

namespace AiPixelScaler.Desktop.Views;

public partial class SandboxWindow : Window
{
    public SandboxWindow()
    {
        InitializeComponent();
    }

    protected override void OnLoaded(Avalonia.Interactivity.RoutedEventArgs e)
    {
        base.OnLoaded(e);
        View?.Focus();
    }
}

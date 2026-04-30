using System.Threading.Tasks;
using AiPixelScaler.Desktop.Services;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Layout;
using Avalonia.Media;

namespace AiPixelScaler.Desktop.Views;

/// <summary>
/// Chiede come incollare un’immagine più grande del canvas (Mantieni / Adatta / Annulla).
/// </summary>
internal sealed class PasteOversizeDialog : Window
{
    private PasteOversizeDialog(int imageW, int imageH, int canvasW, int canvasH)
    {
        Title = "Immagine negli appunti";
        Width = 420;
        Height = 220;
        CanResize = false;
        WindowStartupLocation = WindowStartupLocation.CenterOwner;
        Background = new SolidColorBrush(Color.Parse("#13131c"));

        var msg = new TextBlock
        {
            Text = "L’immagine negli appunti è più grande del canvas attuale.\nCome vuoi incollarla?",
            Foreground = Brushes.White,
            TextWrapping = TextWrapping.Wrap,
            Margin = new Thickness(0, 0, 0, 12)
        };
        var dim = new TextBlock
        {
            Text = $"Appunti: {imageW}×{imageH} px · Canvas: {canvasW}×{canvasH} px",
            Foreground = new SolidColorBrush(Color.Parse("#a0a0b8")),
            FontSize = 12,
            Margin = new Thickness(0, 0, 0, 16)
        };

        var row = new StackPanel { Orientation = Orientation.Horizontal, Spacing = 10 };

        var btnFit = new Button { Content = "Adatta al canvas", MinWidth = 120 };
        btnFit.Click += (_, _) => Close(PasteOversizeChoice.FitToCanvas);

        var btnKeep = new Button { Content = "Mantieni dimensioni", MinWidth = 140 };
        btnKeep.Click += (_, _) => Close(PasteOversizeChoice.MaintainPixelSize);

        var btnCancel = new Button { Content = "Annulla", MinWidth = 90 };
        btnCancel.Click += (_, _) => Close(PasteOversizeChoice.Cancel);

        row.Children.Add(btnFit);
        row.Children.Add(btnKeep);
        row.Children.Add(btnCancel);

        Content = new StackPanel
        {
            Margin = new Thickness(20),
            Children = { msg, dim, row }
        };
    }

    internal static async Task<PasteOversizeChoice> ShowAsync(Window owner, int imageW, int imageH, int canvasW, int canvasH)
    {
        var dlg = new PasteOversizeDialog(imageW, imageH, canvasW, canvasH);
        var r = await dlg.ShowDialog<PasteOversizeChoice?>(owner);
        return r ?? PasteOversizeChoice.Cancel;
    }
}

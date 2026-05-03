using System.Threading.Tasks;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Layout;
using Avalonia.Media;

namespace AiPixelScaler.Desktop.Views;

/// <summary>
/// Dialogo "Nuovo canvas": chiede larghezza, altezza e colore di sfondo.
/// Restituisce <see cref="NewCanvasResult"/> oppure <c>null</c> se l'utente annulla.
/// </summary>
internal sealed class NewCanvasDialog : Window
{
    private readonly TextBox _tbW;
    private readonly TextBox _tbH;
    private readonly ComboBox _cbBg;

    private NewCanvasDialog()
    {
        Title = "Nuovo canvas";
        Width = 360;
        SizeToContent = SizeToContent.Height;
        CanResize = false;
        WindowStartupLocation = WindowStartupLocation.CenterOwner;
        Background = new SolidColorBrush(Color.Parse("#13131c"));

        // --- Larghezza ---
        _tbW = new TextBox { Text = "256", PlaceholderText = "px" };
        var rowW = MakeRow("Larghezza (px)", _tbW);

        // --- Altezza ---
        _tbH = new TextBox { Text = "256", PlaceholderText = "px" };
        var rowH = MakeRow("Altezza (px)", _tbH);

        // --- Sfondo ---
        _cbBg = new ComboBox { HorizontalAlignment = HorizontalAlignment.Stretch, SelectedIndex = 0 };
        _cbBg.Items.Add(new ComboBoxItem { Content = "Trasparente" });
        _cbBg.Items.Add(new ComboBoxItem { Content = "Bianco           (#FFFFFF)" });
        _cbBg.Items.Add(new ComboBoxItem { Content = "Nero             (#000000)" });
        _cbBg.Items.Add(new ComboBoxItem { Content = "Chroma magenta   (#FF00FF)" });
        _cbBg.Items.Add(new ComboBoxItem { Content = "Chroma green screen (#00FF00)" });
        _cbBg.Items.Add(new ComboBoxItem { Content = "Chroma blue screen  (#0000FF)" });
        var rowBg = MakeRow("Sfondo", _cbBg);

        // --- Pulsanti ---
        var btnCrea = new Button { Content = "Crea", MinWidth = 90, HorizontalAlignment = HorizontalAlignment.Left };
        btnCrea.Click += (_, _) => TryAccept();

        var btnCancel = new Button { Content = "Annulla", MinWidth = 90, HorizontalAlignment = HorizontalAlignment.Left };
        btnCancel.Click += (_, _) => Close(null);

        var btnRow = new StackPanel
        {
            Orientation = Orientation.Horizontal,
            Spacing = 10,
            Margin = new Thickness(0, 8, 0, 0),
            Children = { btnCrea, btnCancel }
        };

        var hint = new TextBlock
        {
            Text = "Il canvas viene creato vuoto. Usa «Apri» o il sistema atlas per importare frame.",
            Foreground = new SolidColorBrush(Color.Parse("#7e86a8")),
            FontSize = 10,
            TextWrapping = TextWrapping.Wrap,
            Margin = new Thickness(0, 4, 0, 0)
        };

        Content = new StackPanel
        {
            Margin = new Thickness(20),
            Spacing = 6,
            Children = { rowW, rowH, rowBg, hint, btnRow }
        };

        KeyDown += (_, e) =>
        {
            if (e.Key == Avalonia.Input.Key.Enter)  TryAccept();
            if (e.Key == Avalonia.Input.Key.Escape) Close(null);
        };
    }

    private void TryAccept()
    {
        if (!TryParsePositiveInt(_tbW.Text, out var w) || w < 1 || w > 8192)
        {
            _tbW.Focus();
            return;
        }
        if (!TryParsePositiveInt(_tbH.Text, out var h) || h < 1 || h > 8192)
        {
            _tbH.Focus();
            return;
        }

        var bg = _cbBg.SelectedIndex switch
        {
            1 => "#FFFFFF",
            2 => "#000000",
            3 => "#FF00FF",
            4 => "#00FF00",
            5 => "#0000FF",
            _ => null   // null = trasparente
        };

        Close(new NewCanvasResult(w, h, bg));
    }

    private static bool TryParsePositiveInt(string? text, out int value)
    {
        value = 0;
        return !string.IsNullOrWhiteSpace(text)
               && int.TryParse(text.Trim(), out value)
               && value > 0;
    }

    private static Grid MakeRow(string label, Control ctrl)
    {
        var lbl = new TextBlock
        {
            Text = label,
            Foreground = new SolidColorBrush(Color.Parse("#a0aac4")),
            FontSize = 12,
            VerticalAlignment = VerticalAlignment.Center,
            Width = 110
        };
        var grid = new Grid { ColumnDefinitions = new ColumnDefinitions("110,8,*"), Margin = new Thickness(0, 2) };
        Grid.SetColumn(lbl, 0);
        Grid.SetColumn(ctrl, 2);
        grid.Children.Add(lbl);
        grid.Children.Add(ctrl);
        return grid;
    }

    /// <summary>Mostra il dialogo e restituisce il risultato, o <c>null</c> se annullato.</summary>
    internal static async Task<NewCanvasResult?> ShowAsync(Window owner)
    {
        var dlg = new NewCanvasDialog();
        return await dlg.ShowDialog<NewCanvasResult?>(owner);
    }
}

/// <param name="Width">Larghezza in pixel del nuovo canvas.</param>
/// <param name="Height">Altezza in pixel del nuovo canvas.</param>
/// <param name="BackgroundHex">Colore HEX sfondo (#RRGGBB) oppure <c>null</c> per trasparente.</param>
internal sealed record NewCanvasResult(int Width, int Height, string? BackgroundHex);

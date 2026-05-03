using System.IO;
using System.Threading.Tasks;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Layout;
using Avalonia.Media;
using Avalonia.Platform.Storage;

namespace AiPixelScaler.Desktop.Views;

/// <summary>
/// Dialogo di configurazione per il percorso FFmpeg.
/// Mostrato quando ffmpeg.exe / ffprobe.exe non vengono trovati nel PATH di sistema.
/// Restituisce il percorso della cartella confermato, oppure <c>null</c> se annullato.
/// </summary>
internal sealed class FfmpegConfigDialog : Window
{
    private readonly TextBox  _tbFolder;
    private readonly TextBlock _lblError;

    private FfmpegConfigDialog(string? currentFolder)
    {
        Title  = "Configura FFmpeg";
        Width  = 520;
        SizeToContent = SizeToContent.Height;
        CanResize     = false;
        WindowStartupLocation = WindowStartupLocation.CenterOwner;
        Background = new SolidColorBrush(Color.Parse("#13131c"));

        var lblTitle = new TextBlock
        {
            Text       = "FFmpeg non trovato nel PATH di sistema.",
            Foreground = Brushes.White,
            FontSize   = 13,
            FontWeight = Avalonia.Media.FontWeight.SemiBold,
            Margin     = new Thickness(0, 0, 0, 6),
        };

        var lblHint = new TextBlock
        {
            Text = "Scarica FFmpeg da  https://ffmpeg.org/download.html  (build Windows),\n" +
                   "estrailo e specifica qui la cartella che contiene ffmpeg.exe e ffprobe.exe.",
            Foreground  = new SolidColorBrush(Color.Parse("#7e86a8")),
            FontSize    = 11,
            TextWrapping = TextWrapping.Wrap,
            Margin      = new Thickness(0, 0, 0, 12),
        };

        // Riga cartella
        _tbFolder = new TextBox
        {
            Text        = currentFolder ?? string.Empty,
            PlaceholderText = "Percorso cartella bin/ di FFmpeg…",
        };
        var btnBrowse = new Button { Content = "Sfoglia…", MinWidth = 80, Margin = new Thickness(8, 0, 0, 0) };
        btnBrowse.Click += async (_, _) =>
        {
            var result = await StorageProvider.OpenFolderPickerAsync(new FolderPickerOpenOptions
            {
                Title = "Seleziona la cartella che contiene ffmpeg.exe e ffprobe.exe"
            });
            if (result.Count > 0)
                _tbFolder.Text = result[0].Path.LocalPath;
        };
        var folderRow = new Grid { ColumnDefinitions = new ColumnDefinitions("*,Auto"), Margin = new Thickness(0, 0, 0, 4) };
        Grid.SetColumn(_tbFolder,  0);
        Grid.SetColumn(btnBrowse,  1);
        folderRow.Children.Add(_tbFolder);
        folderRow.Children.Add(btnBrowse);

        _lblError = new TextBlock
        {
            Foreground  = new SolidColorBrush(Color.Parse("#e07070")),
            FontSize    = 11,
            TextWrapping = TextWrapping.Wrap,
            IsVisible   = false,
            Margin      = new Thickness(0, 0, 0, 6),
        };

        // Pulsanti
        var btnSalva   = new Button { Content = "Salva",   MinWidth = 90 };
        var btnAnnulla = new Button { Content = "Annulla", MinWidth = 90, Margin = new Thickness(10, 0, 0, 0) };
        btnSalva.Click   += (_, _) => TryAccept();
        btnAnnulla.Click += (_, _) => Close(null);

        var btnRow = new StackPanel
        {
            Orientation = Orientation.Horizontal,
            Margin      = new Thickness(0, 10, 0, 0),
            Children    = { btnSalva, btnAnnulla },
        };

        Content = new StackPanel
        {
            Margin   = new Thickness(20),
            Spacing  = 0,
            Children = { lblTitle, lblHint, folderRow, _lblError, btnRow },
        };

        KeyDown += (_, e) =>
        {
            if (e.Key == Avalonia.Input.Key.Enter)  TryAccept();
            if (e.Key == Avalonia.Input.Key.Escape) Close(null);
        };
    }

    private void TryAccept()
    {
        var folder = _tbFolder.Text?.Trim() ?? string.Empty;
        var ok = !string.IsNullOrEmpty(folder)
              && File.Exists(Path.Combine(folder, "ffmpeg.exe"))
              && File.Exists(Path.Combine(folder, "ffprobe.exe"));

        if (!ok)
        {
            _lblError.Text      = "ffmpeg.exe e ffprobe.exe non trovati nella cartella specificata. " +
                                  "Verifica il percorso (es: C:\\ffmpeg\\bin).";
            _lblError.IsVisible = true;
            _tbFolder.Focus();
            return;
        }
        Close(folder);
    }

    /// <summary>Mostra il dialogo e restituisce il percorso cartella o <c>null</c> se annullato.</summary>
    internal static async Task<string?> ShowAsync(Window owner, string? currentFolder)
    {
        var dlg = new FfmpegConfigDialog(currentFolder);
        return await dlg.ShowDialog<string?>(owner);
    }
}

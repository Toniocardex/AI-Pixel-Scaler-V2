using System;
using System.IO;
using System.Threading;
using System.Threading.Tasks;
using AiPixelScaler.Desktop.Services;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Layout;
using Avalonia.Media;
using Avalonia.Platform.Storage;

namespace AiPixelScaler.Desktop.Views;

/// <summary>
/// Dialogo unificato per la configurazione di FFmpeg.
/// Offre il download automatico (gyan.dev) con ProgressBar
/// e il fallback con percorso manuale.
/// Restituisce il percorso della cartella configurata o <c>null</c> se annullato.
/// </summary>
internal sealed class FfmpegSetupDialog : Window
{
    // ── Sezione download automatico ──────────────────────────────────────────
    private readonly ProgressBar _progressBar;
    private readonly TextBlock   _lblProgress;
    private readonly Button      _btnDownload;
    private readonly Button      _btnCancelDl;

    // ── Sezione manuale ──────────────────────────────────────────────────────
    private readonly TextBox   _tbFolder;
    private readonly TextBlock _lblError;

    private CancellationTokenSource? _dlCts;

    private FfmpegSetupDialog(string? currentFolder)
    {
        Title  = "Configura FFmpeg";
        Width  = 520;
        SizeToContent = SizeToContent.Height;
        CanResize     = false;
        WindowStartupLocation = WindowStartupLocation.CenterOwner;
        Background = new SolidColorBrush(Color.Parse("#13131c"));

        // ── Header ───────────────────────────────────────────────────────────
        var lblTitle = new TextBlock
        {
            Text       = "FFmpeg non trovato nel PATH di sistema.",
            Foreground = Brushes.White,
            FontSize   = 13,
            FontWeight = FontWeight.SemiBold,
            Margin     = new Thickness(0, 0, 0, 14),
        };

        // ── Sezione download automatico ──────────────────────────────────────
        var autoFolder = FFmpegDownloader.AutoInstallFolder;

        var lblAutoHint = new TextBlock
        {
            Text        = "Scarica automaticamente (build Windows essentials, ~80 MB):",
            Foreground  = new SolidColorBrush(Color.Parse("#a0aac4")),
            FontSize    = 11,
            Margin      = new Thickness(0, 0, 0, 2),
        };

        var lblAutoFolder = new TextBlock
        {
            Text         = autoFolder,
            Foreground   = new SolidColorBrush(Color.Parse("#5a6080")),
            FontSize     = 10,
            TextWrapping = TextWrapping.Wrap,
            Margin       = new Thickness(0, 0, 0, 8),
        };

        _progressBar = new ProgressBar
        {
            Minimum   = 0,
            Maximum   = 100,
            Value     = 0,
            Height    = 12,
            IsVisible = false,
            Margin    = new Thickness(0, 0, 0, 4),
        };

        _lblProgress = new TextBlock
        {
            Text       = string.Empty,
            Foreground = new SolidColorBrush(Color.Parse("#7e86a8")),
            FontSize   = 10,
            IsVisible  = false,
            Margin     = new Thickness(0, 0, 0, 8),
        };

        _btnDownload = new Button
        {
            Content  = "Scarica automaticamente",
            MinWidth = 200,
        };
        _btnCancelDl = new Button
        {
            Content   = "Annulla download",
            MinWidth  = 130,
            Margin    = new Thickness(8, 0, 0, 0),
            IsVisible = false,
        };

        _btnDownload.Click  += async (_, _) => await StartDownloadAsync();
        _btnCancelDl.Click  += (_, _) => _dlCts?.Cancel();

        var dlBtnRow = new StackPanel
        {
            Orientation = Orientation.Horizontal,
            Children    = { _btnDownload, _btnCancelDl },
        };

        var downloadSection = new Border
        {
            BorderBrush     = new SolidColorBrush(Color.Parse("#252840")),
            BorderThickness = new Thickness(1),
            CornerRadius    = new CornerRadius(6),
            Padding         = new Thickness(12, 10, 12, 12),
            Margin          = new Thickness(0, 0, 0, 14),
            Child = new StackPanel
            {
                Spacing  = 0,
                Children = { lblAutoHint, lblAutoFolder, _progressBar, _lblProgress, dlBtnRow },
            },
        };

        // ── Separatore ───────────────────────────────────────────────────────
        var lblOr = new TextBlock
        {
            Text                = "— oppure configura manualmente —",
            Foreground          = new SolidColorBrush(Color.Parse("#3a4060")),
            FontSize            = 10,
            HorizontalAlignment = HorizontalAlignment.Center,
            Margin              = new Thickness(0, 0, 0, 12),
        };

        // ── Sezione manuale ──────────────────────────────────────────────────
        var lblManualHint = new TextBlock
        {
            Text       = "Cartella che contiene ffmpeg.exe e ffprobe.exe:",
            Foreground = new SolidColorBrush(Color.Parse("#7e86a8")),
            FontSize   = 11,
            Margin     = new Thickness(0, 0, 0, 4),
        };

        _tbFolder = new TextBox
        {
            Text            = currentFolder ?? string.Empty,
            PlaceholderText = "Percorso cartella bin/ di FFmpeg…",
        };

        var btnBrowse = new Button
        {
            Content  = "Sfoglia…",
            MinWidth = 80,
            Margin   = new Thickness(8, 0, 0, 0),
        };
        btnBrowse.Click += async (_, _) =>
        {
            var result = await StorageProvider.OpenFolderPickerAsync(new FolderPickerOpenOptions
            {
                Title = "Seleziona la cartella con ffmpeg.exe e ffprobe.exe"
            });
            if (result.Count > 0)
                _tbFolder.Text = result[0].Path.LocalPath;
        };

        var folderRow = new Grid
        {
            ColumnDefinitions = new ColumnDefinitions("*,Auto"),
            Margin = new Thickness(0, 0, 0, 4),
        };
        Grid.SetColumn(_tbFolder,  0);
        Grid.SetColumn(btnBrowse, 1);
        folderRow.Children.Add(_tbFolder);
        folderRow.Children.Add(btnBrowse);

        _lblError = new TextBlock
        {
            Foreground   = new SolidColorBrush(Color.Parse("#e07070")),
            FontSize     = 11,
            TextWrapping = TextWrapping.Wrap,
            IsVisible    = false,
            Margin       = new Thickness(0, 0, 0, 6),
        };

        var btnUseManual = new Button { Content = "Usa percorso manuale", MinWidth = 160 };
        var btnClose     = new Button { Content = "Chiudi",               MinWidth = 80,  Margin = new Thickness(8, 0, 0, 0) };
        btnUseManual.Click += (_, _) => TryAcceptManual();
        btnClose.Click     += (_, _) => Close(null);

        var manualBtnRow = new StackPanel
        {
            Orientation = Orientation.Horizontal,
            Margin      = new Thickness(0, 8, 0, 0),
            Children    = { btnUseManual, btnClose },
        };

        Content = new StackPanel
        {
            Margin   = new Thickness(20),
            Spacing  = 0,
            Children =
            {
                lblTitle,
                downloadSection,
                lblOr,
                lblManualHint,
                folderRow,
                _lblError,
                manualBtnRow,
            },
        };

        // ESC: annulla download in corso oppure chiude la finestra
        KeyDown += (_, e) =>
        {
            if (e.Key != Avalonia.Input.Key.Escape) return;
            if (_dlCts is not null)
                _dlCts.Cancel();
            else
                Close(null);
        };

        // Chiusura tramite X: annulla download in corso
        Closing += (_, _) => _dlCts?.Cancel();
    }

    // ── Download automatico ───────────────────────────────────────────────────

    private async Task StartDownloadAsync()
    {
        _btnDownload.IsEnabled  = false;
        _btnCancelDl.IsVisible  = true;
        _progressBar.IsVisible  = true;
        _lblProgress.IsVisible  = true;
        _lblProgress.Text       = "Connessione in corso…";
        _progressBar.IsIndeterminate = true;

        _dlCts = new CancellationTokenSource();

        // IProgress<T> creato sul thread UI → i Report vengono inviati al thread UI
        var progress = new Progress<(long downloaded, long? total)>(report =>
        {
            var (dl, tot) = report;
            if (tot.HasValue && tot.Value > 0)
            {
                _progressBar.IsIndeterminate = false;
                _progressBar.Value = dl * 100.0 / tot.Value;
                _lblProgress.Text  = $"{dl / 1_048_576.0:F1} MB / {tot.Value / 1_048_576.0:F1} MB";
            }
            else
            {
                _progressBar.IsIndeterminate = true;
                _lblProgress.Text  = $"{dl / 1_048_576.0:F1} MB scaricati…";
            }
        });

        try
        {
            await FFmpegDownloader.DownloadAndInstallAsync(
                FFmpegDownloader.AutoInstallFolder,
                progress,
                _dlCts.Token);

            // Download riuscito: chiude il dialogo restituendo la cartella
            Close(FFmpegDownloader.AutoInstallFolder);
        }
        catch (OperationCanceledException)
        {
            _lblProgress.Text            = "Download annullato.";
            _progressBar.IsIndeterminate = false;
            _progressBar.Value           = 0;
        }
        catch (Exception ex)
        {
            _lblProgress.Text      = $"Errore: {ex.Message}";
            _progressBar.IsVisible = false;
        }
        finally
        {
            _dlCts?.Dispose();
            _dlCts = null;
            _btnDownload.IsEnabled = true;
            _btnCancelDl.IsVisible = false;
        }
    }

    // ── Percorso manuale ──────────────────────────────────────────────────────

    private void TryAcceptManual()
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

    // ── Statico ───────────────────────────────────────────────────────────────

    /// <summary>
    /// Mostra il dialogo e restituisce il percorso della cartella configurata,
    /// oppure <c>null</c> se l'utente ha annullato senza configurare FFmpeg.
    /// </summary>
    internal static async Task<string?> ShowAsync(Window owner, string? currentFolder)
    {
        var dlg = new FfmpegSetupDialog(currentFolder);
        return await dlg.ShowDialog<string?>(owner);
    }
}

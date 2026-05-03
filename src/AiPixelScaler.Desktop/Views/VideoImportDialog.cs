using System;
using System.Globalization;
using System.Threading.Tasks;
using AiPixelScaler.Desktop.Services;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Layout;
using Avalonia.Media;
using Avalonia.Platform.Storage;

namespace AiPixelScaler.Desktop.Views;

/// <summary>
/// Dialogo per l'importazione frame da video MP4 H.264.
/// Permette di scegliere il file, il range temporale e la densità di estrazione.
/// Restituisce <see cref="VideoImportResult"/> oppure <c>null</c> se annullato.
/// </summary>
internal sealed class VideoImportDialog : Window
{
    private readonly string    _ffprobePath;
    private readonly TextBox   _tbFile;
    private readonly TextBlock _lblInfo;
    private readonly TextBox   _tbStart;
    private readonly TextBox   _tbEnd;
    private readonly RadioButton _rbFps;
    private readonly RadioButton _rbEveryN;
    private readonly TextBox   _tbValue;
    private readonly TextBlock _lblEstimate;

    private VideoMetadata? _metadata;

    private VideoImportDialog(string ffmpegPath, string ffprobePath)
    {
        _ffprobePath = ffprobePath;

        Title  = "Importa frame da video";
        Width  = 500;
        SizeToContent = SizeToContent.Height;
        CanResize     = false;
        WindowStartupLocation = WindowStartupLocation.CenterOwner;
        Background = new SolidColorBrush(Color.Parse("#13131c"));

        // ── File video ──────────────────────────────────────────────────────
        _tbFile = new TextBox { IsReadOnly = true, PlaceholderText = "Nessun file selezionato…" };
        var btnBrowse = new Button { Content = "Sfoglia…", MinWidth = 80, Margin = new Thickness(8, 0, 0, 0) };
        btnBrowse.Click += async (_, _) => await BrowseVideoAsync();

        var fileRow = new Grid { ColumnDefinitions = new ColumnDefinitions("*,Auto") };
        Grid.SetColumn(_tbFile,    0);
        Grid.SetColumn(btnBrowse, 1);
        fileRow.Children.Add(_tbFile);
        fileRow.Children.Add(btnBrowse);

        // ── Metadati ────────────────────────────────────────────────────────
        _lblInfo = new TextBlock
        {
            Text       = "—",
            Foreground = new SolidColorBrush(Color.Parse("#7e86a8")),
            FontSize   = 11,
            Margin     = new Thickness(0, 2, 0, 0),
        };

        // ── Range ───────────────────────────────────────────────────────────
        _tbStart = new TextBox { Text = "0",  PlaceholderText = "sec" };
        _tbEnd   = new TextBox { Text = "",   PlaceholderText = "sec (vuoto = tutto)" };
        _tbStart.TextChanged += (_, _) => UpdateEstimate();
        _tbEnd.TextChanged   += (_, _) => UpdateEstimate();

        var rangeGrid = new Grid { ColumnDefinitions = new ColumnDefinitions("100,8,*,16,100,8,*"), Margin = new Thickness(0, 8, 0, 0) };
        rangeGrid.Children.Add(MakeLabel("Inizio (s)",           0));
        rangeGrid.Children.Add(At(_tbStart,                      2));
        rangeGrid.Children.Add(MakeLabel("Fine (s)",             4));
        rangeGrid.Children.Add(At(_tbEnd,                        6));

        // ── Modalità estrazione ─────────────────────────────────────────────
        _rbFps    = new RadioButton { Content = "FPS target",    IsChecked = true,  GroupName = "mode" };
        _rbEveryN = new RadioButton { Content = "Ogni N frame",  IsChecked = false, GroupName = "mode" };
        _tbValue  = new TextBox { Text = "12", Width = 70 };
        _rbFps.IsCheckedChanged    += (_, _) => { UpdateValueHint(); UpdateEstimate(); };
        _rbEveryN.IsCheckedChanged += (_, _) => { UpdateValueHint(); UpdateEstimate(); };
        _tbValue.TextChanged       += (_, _) => UpdateEstimate();

        var modeRow = new StackPanel
        {
            Orientation = Orientation.Horizontal,
            Spacing     = 12,
            Margin      = new Thickness(0, 8, 0, 0),
            Children    = { _rbFps, _rbEveryN, _tbValue },
        };

        // ── Stima ────────────────────────────────────────────────────────────
        _lblEstimate = new TextBlock
        {
            Text       = "Stima: —",
            Foreground = new SolidColorBrush(Color.Parse("#a0aac4")),
            FontSize   = 11,
            Margin     = new Thickness(0, 6, 0, 0),
        };

        // ── Hint ─────────────────────────────────────────────────────────────
        var lblHint = new TextBlock
        {
            Text = "Suggerimento: per sprite sheet pixel art usa FPS 8–15. " +
                   "Frame estratti → atlas automatico in Animation Studio.",
            Foreground  = new SolidColorBrush(Color.Parse("#5a6080")),
            FontSize    = 10,
            TextWrapping = TextWrapping.Wrap,
            Margin      = new Thickness(0, 4, 0, 0),
        };

        // ── Separatore ───────────────────────────────────────────────────────
        var sep = new Border { Height = 1, Background = new SolidColorBrush(Color.Parse("#1a2035")), Margin = new Thickness(0, 10, 0, 0) };

        // ── Pulsanti ─────────────────────────────────────────────────────────
        var btnEstrai  = new Button { Content = "Estrai",   MinWidth = 90 };
        var btnAnnulla = new Button { Content = "Annulla",  MinWidth = 90, Margin = new Thickness(10, 0, 0, 0) };
        btnEstrai.Click  += (_, _) => TryAccept();
        btnAnnulla.Click += (_, _) => Close(null);

        var btnRow = new StackPanel
        {
            Orientation = Orientation.Horizontal,
            Margin      = new Thickness(0, 10, 0, 0),
            Children    = { btnEstrai, btnAnnulla },
        };

        Content = new StackPanel
        {
            Margin   = new Thickness(20),
            Spacing  = 0,
            Children = { fileRow, _lblInfo, rangeGrid, modeRow, _lblEstimate, lblHint, sep, btnRow },
        };

        KeyDown += (_, e) =>
        {
            if (e.Key == Avalonia.Input.Key.Escape) Close(null);
        };
    }

    // ── Interazione ──────────────────────────────────────────────────────────

    private async Task BrowseVideoAsync()
    {
        var files = await StorageProvider.OpenFilePickerAsync(new FilePickerOpenOptions
        {
            Title = "Seleziona video MP4 H.264",
            AllowMultiple = false,
            FileTypeFilter =
            [
                new FilePickerFileType("Video MP4")
                {
                    Patterns = ["*.mp4", "*.MP4", "*.m4v", "*.M4V"]
                }
            ]
        });
        if (files.Count == 0) return;

        var path = files[0].Path.LocalPath;
        _tbFile.Text   = path;
        _lblInfo.Text  = "Lettura metadati…";
        _metadata      = await VideoFrameExtractor.GetMetadataAsync(_ffprobePath, path);
        _lblInfo.Text  = _metadata is null
            ? "Impossibile leggere i metadati. Verifica che il file sia un MP4 H.264 valido."
            : $"Durata: {_metadata.DurationSec:F1}s  ·  FPS sorgente: {_metadata.SourceFps:F2}  ·  " +
              $"Risoluzione: {_metadata.Width}×{_metadata.Height} px";
        UpdateEstimate();
    }

    private void TryAccept()
    {
        var filePath = _tbFile.Text?.Trim() ?? string.Empty;
        if (string.IsNullOrEmpty(filePath))
        {
            _tbFile.Focus();
            return;
        }

        var start = ParseDouble(_tbStart.Text, 0.0);
        var endRaw = _tbEnd.Text?.Trim();
        double? end = string.IsNullOrEmpty(endRaw) ? null : ParseDoubleOpt(endRaw);

        var useFps = _rbFps.IsChecked == true;
        var value  = ParseDouble(_tbValue.Text, useFps ? 12.0 : 2.0);
        if (value <= 0) value = useFps ? 12.0 : 2.0;

        Close(new VideoImportResult(filePath, start, end, useFps, value, _metadata));
    }

    // ── Stima frame ──────────────────────────────────────────────────────────

    private void UpdateEstimate()
    {
        if (_metadata is null) { _lblEstimate.Text = "Stima: —"; return; }

        var start = Math.Max(0, ParseDouble(_tbStart.Text, 0.0));
        var endRaw = _tbEnd.Text?.Trim();
        var end = string.IsNullOrEmpty(endRaw)
            ? _metadata.DurationSec
            : Math.Min(_metadata.DurationSec, ParseDouble(endRaw, _metadata.DurationSec));
        var duration = Math.Max(0, end - start);

        double estimate;
        if (_rbFps.IsChecked == true)
        {
            var fps = ParseDouble(_tbValue.Text, 12.0);
            estimate = fps > 0 ? Math.Ceiling(duration * fps) : 0;
        }
        else
        {
            var n = Math.Max(1, (int)Math.Round(ParseDouble(_tbValue.Text, 2.0)));
            estimate = _metadata.SourceFps > 0
                ? Math.Ceiling(duration * _metadata.SourceFps / n)
                : 0;
        }

        _lblEstimate.Text = $"Stima: ~{(int)estimate} frame";
    }

    private void UpdateValueHint()
    {
        _tbValue.PlaceholderText = _rbFps.IsChecked == true ? "FPS" : "N";
    }

    // ── Helper UI ────────────────────────────────────────────────────────────

    private static TextBlock MakeLabel(string text, int col)
    {
        var tb = new TextBlock
        {
            Text              = text,
            Foreground        = new SolidColorBrush(Color.Parse("#a0aac4")),
            FontSize          = 11,
            VerticalAlignment = VerticalAlignment.Center,
        };
        Grid.SetColumn(tb, col);
        return tb;
    }

    private static T At<T>(T ctrl, int col) where T : Control
    {
        Grid.SetColumn(ctrl, col);
        return ctrl;
    }

    private static double ParseDouble(string? text, double fallback)
    {
        if (string.IsNullOrWhiteSpace(text)) return fallback;
        return double.TryParse(text.Trim(), NumberStyles.Any, CultureInfo.InvariantCulture, out var v)
            ? v : fallback;
    }

    private static double? ParseDoubleOpt(string? text)
    {
        if (string.IsNullOrWhiteSpace(text)) return null;
        return double.TryParse(text.Trim(), NumberStyles.Any, CultureInfo.InvariantCulture, out var v)
            ? v : null;
    }

    // ── Statico ──────────────────────────────────────────────────────────────

    /// <summary>Mostra il dialogo e restituisce il risultato, o <c>null</c> se annullato.</summary>
    internal static async Task<VideoImportResult?> ShowAsync(
        Window owner,
        string ffmpegPath,
        string ffprobePath)
    {
        var dlg = new VideoImportDialog(ffmpegPath, ffprobePath);
        return await dlg.ShowDialog<VideoImportResult?>(owner);
    }
}

// ── Result record ────────────────────────────────────────────────────────────

/// <summary>Dati raccolti dal dialogo di import video.</summary>
internal sealed record VideoImportResult(
    string         FilePath,
    double         StartSec,
    double?        EndSec,
    bool           UseFpsTarget,
    double         FpsOrEveryN,
    VideoMetadata? Metadata);

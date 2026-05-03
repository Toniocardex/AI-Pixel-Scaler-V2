using System;
using System.IO;
using System.IO.Compression;
using System.Net.Http;
using System.Threading;
using System.Threading.Tasks;

namespace AiPixelScaler.Desktop.Services;

/// <summary>
/// Scarica e installa FFmpeg essentials nella cartella locale dell'applicazione.
/// Sorgente: <see href="https://www.gyan.dev/ffmpeg/builds/ffmpeg-release-essentials.zip"/>.
/// </summary>
internal static class FFmpegDownloader
{
    private const string ZipUrl =
        "https://www.gyan.dev/ffmpeg/builds/ffmpeg-release-essentials.zip";

    /// <summary>
    /// Cartella di installazione automatica:
    /// <c>%LOCALAPPDATA%\AiPixelScaler\ffmpeg</c>.
    /// </summary>
    internal static string AutoInstallFolder =>
        Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
            "AiPixelScaler",
            "ffmpeg");

    /// <summary>
    /// Scarica <c>ffmpeg-release-essentials.zip</c> ed estrae
    /// <c>ffmpeg.exe</c> e <c>ffprobe.exe</c> nella
    /// <paramref name="destinationFolder"/> indicata.
    /// </summary>
    /// <param name="destinationFolder">Cartella di destinazione (verrà creata se assente).</param>
    /// <param name="progress">
    ///   Callback di avanzamento: <c>(bytesScaricati, totaleBytes?)</c>.
    ///   Il totale può essere <c>null</c> se il server non fornisce Content-Length.
    /// </param>
    /// <param name="ct">Token di annullamento.</param>
    /// <exception cref="OperationCanceledException">Sollevata se <paramref name="ct"/> viene annullato.</exception>
    internal static async Task DownloadAndInstallAsync(
        string destinationFolder,
        IProgress<(long downloaded, long? total)>? progress = null,
        CancellationToken ct = default)
    {
        Directory.CreateDirectory(destinationFolder);

        using var http = new HttpClient();
        http.Timeout = Timeout.InfiniteTimeSpan;               // gestito via CancellationToken
        http.DefaultRequestHeaders.UserAgent.ParseAdd("AiPixelScaler/1.0");

        // Avvia la richiesta in modalità streaming per abilitare il reporting progress
        using var response = await http.GetAsync(
            ZipUrl,
            HttpCompletionOption.ResponseHeadersRead,
            ct);
        response.EnsureSuccessStatusCode();

        var total   = response.Content.Headers.ContentLength;  // null se server non lo dichiara
        var tempZip = Path.Combine(
            Path.GetTempPath(),
            $"ffmpeg_dl_{Guid.NewGuid():N}.zip");

        try
        {
            // ── Download ──────────────────────────────────────────────────────
            await using (var src = await response.Content.ReadAsStreamAsync(ct))
            await using (var dst = File.Create(tempZip))
            {
                var buf        = new byte[81_920];
                long downloaded = 0;
                int  read;

                while ((read = await src.ReadAsync(buf, ct)) > 0)
                {
                    await dst.WriteAsync(buf.AsMemory(0, read), ct);
                    downloaded += read;
                    progress?.Report((downloaded, total));
                }
            }

            ct.ThrowIfCancellationRequested();

            // ── Estrazione ────────────────────────────────────────────────────
            // Il ZIP di gyan.dev contiene (es.) "ffmpeg-7.1-essentials_build/bin/ffmpeg.exe"
            // Estraiamo solo i due eseguibili necessari.
            using var zip = ZipFile.OpenRead(tempZip);
            foreach (var entry in zip.Entries)
            {
                // Confronto case-insensitive sull'ultimo segmento del percorso
                var name = entry.Name;
                if (!string.Equals(name, "ffmpeg.exe",  StringComparison.OrdinalIgnoreCase) &&
                    !string.Equals(name, "ffprobe.exe", StringComparison.OrdinalIgnoreCase))
                    continue;

                // Verifica che l'entry sia nella cartella bin/ (robustezza per strutture future)
                if (!entry.FullName.Contains("/bin/", StringComparison.OrdinalIgnoreCase))
                    continue;

                var dest = Path.Combine(destinationFolder, name.ToLowerInvariant());
                entry.ExtractToFile(dest, overwrite: true);
            }
        }
        finally
        {
            // Rimuove il file zip temporaneo anche in caso di errore / annullamento
            try { File.Delete(tempZip); } catch { /* ignora */ }
        }
    }
}

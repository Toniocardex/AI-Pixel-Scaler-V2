using System;
using System.Diagnostics;
using System.Globalization;
using System.IO;
using System.Text;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;

namespace AiPixelScaler.Desktop.Services;

/// <summary>
/// Wrapper su ffprobe e ffmpeg per la lettura di metadati video e l'estrazione di frame PNG.
/// Supporto MVP: solo MP4 H.264.
/// </summary>
internal static class VideoFrameExtractor
{
    // ── Metadati ─────────────────────────────────────────────────────────────

    /// <summary>
    /// Legge durata, FPS sorgente e risoluzione del video tramite ffprobe.
    /// Restituisce <c>null</c> se il file non è riconoscibile o ffprobe fallisce.
    /// </summary>
    internal static async Task<VideoMetadata?> GetMetadataAsync(
        string ffprobePath,
        string inputPath)
    {
        try
        {
            var psi = new ProcessStartInfo(ffprobePath)
            {
                RedirectStandardOutput = true,
                RedirectStandardError  = true,
                UseShellExecute        = false,
                CreateNoWindow         = true,
            };
            psi.ArgumentList.Add("-v");
            psi.ArgumentList.Add("quiet");
            psi.ArgumentList.Add("-print_format");
            psi.ArgumentList.Add("json");
            psi.ArgumentList.Add("-show_format");
            psi.ArgumentList.Add("-show_streams");
            psi.ArgumentList.Add(inputPath);

            using var proc = Process.Start(psi) ?? throw new InvalidOperationException("ffprobe non avviato.");
            var output = await proc.StandardOutput.ReadToEndAsync();
            await proc.WaitForExitAsync();

            if (proc.ExitCode != 0) return null;

            using var doc = JsonDocument.Parse(output);
            var root = doc.RootElement;

            // Cerca il primo stream video
            if (!root.TryGetProperty("streams", out var streams)) return null;
            JsonElement? videoStream = null;
            foreach (var s in streams.EnumerateArray())
            {
                if (s.TryGetProperty("codec_type", out var ct) &&
                    ct.GetString() == "video")
                {
                    videoStream = s;
                    break;
                }
            }
            if (videoStream is null) return null;
            var vs = videoStream.Value;

            var width  = vs.TryGetProperty("width",  out var w) ? w.GetInt32() : 0;
            var height = vs.TryGetProperty("height", out var h) ? h.GetInt32() : 0;

            // r_frame_rate es. "30/1" oppure "30000/1001"
            double sourceFps = 0;
            if (vs.TryGetProperty("r_frame_rate", out var fpsEl))
            {
                var fpsStr = fpsEl.GetString() ?? "0/1";
                var slash  = fpsStr.IndexOf('/');
                if (slash > 0 &&
                    double.TryParse(fpsStr[..slash],      NumberStyles.Any, CultureInfo.InvariantCulture, out var num) &&
                    double.TryParse(fpsStr[(slash + 1)..], NumberStyles.Any, CultureInfo.InvariantCulture, out var den) &&
                    den > 0)
                {
                    sourceFps = num / den;
                }
            }

            // Durata dal formato (più affidabile dello stream)
            double duration = 0;
            if (root.TryGetProperty("format", out var fmt) &&
                fmt.TryGetProperty("duration", out var durEl))
            {
                double.TryParse(durEl.GetString(), NumberStyles.Any, CultureInfo.InvariantCulture, out duration);
            }

            return new VideoMetadata(duration, sourceFps, width, height);
        }
        catch
        {
            return null;
        }
    }

    // ── Estrazione frame ─────────────────────────────────────────────────────

    /// <summary>
    /// Estrae frame dal video in <paramref name="outputFolder"/> come PNG sequenziali
    /// (<c>0001.png</c>, <c>0002.png</c>, …).
    /// Restituisce il numero di frame estratti.
    /// </summary>
    /// <exception cref="InvalidOperationException">ffmpeg ha restituito exit code ≠ 0.</exception>
    internal static async Task<int> ExtractFramesAsync(
        string          ffmpegPath,
        ExtractOptions  opts,
        string          outputFolder,
        CancellationToken ct = default)
    {
        var psi = new ProcessStartInfo(ffmpegPath)
        {
            RedirectStandardError = true,
            UseShellExecute       = false,
            CreateNoWindow        = true,
        };

        // Sovrascrivi senza chiedere
        psi.ArgumentList.Add("-y");

        // Seek input (pre-seek: molto veloce)
        if (opts.StartSec > 0)
        {
            psi.ArgumentList.Add("-ss");
            psi.ArgumentList.Add(opts.StartSec.ToString("F3", CultureInfo.InvariantCulture));
        }

        // Fine range
        if (opts.EndSec.HasValue)
        {
            psi.ArgumentList.Add("-to");
            psi.ArgumentList.Add(opts.EndSec.Value.ToString("F3", CultureInfo.InvariantCulture));
        }

        // Input
        psi.ArgumentList.Add("-i");
        psi.ArgumentList.Add(opts.InputPath);

        // Filtro video
        psi.ArgumentList.Add("-vf");
        if (opts.UseFpsTarget)
        {
            var fps = opts.FpsOrEveryN.ToString("F6", CultureInfo.InvariantCulture).TrimEnd('0').TrimEnd('.');
            psi.ArgumentList.Add($"fps={fps}");
        }
        else
        {
            var n = Math.Max(1, (int)Math.Round(opts.FpsOrEveryN));
            psi.ArgumentList.Add($"select='not(mod(n\\,{n}))',setpts={n}*PTS");
        }

        // Nessuna sincronizzazione frame (preserva tutti i frame filtrati)
        psi.ArgumentList.Add("-vsync");
        psi.ArgumentList.Add("0");

        // Qualità PNG massima
        psi.ArgumentList.Add("-q:v");
        psi.ArgumentList.Add("1");

        // Output
        psi.ArgumentList.Add(Path.Combine(outputFolder, "%04d.png"));

        using var proc = Process.Start(psi) ?? throw new InvalidOperationException("ffmpeg non avviato.");

        // Collega la cancellazione all'uccisione del processo
        await using var reg = ct.Register(() =>
        {
            try { proc.Kill(entireProcessTree: true); } catch { }
        });

        var stderr = await proc.StandardError.ReadToEndAsync(ct);
        await proc.WaitForExitAsync(ct);

        ct.ThrowIfCancellationRequested();

        if (proc.ExitCode != 0)
            throw new InvalidOperationException(
                $"ffmpeg ha restituito exit code {proc.ExitCode}.\n{TruncateStderr(stderr)}");

        return Directory.GetFiles(outputFolder, "*.png").Length;
    }

    // ── Privati ──────────────────────────────────────────────────────────────

    private static string TruncateStderr(string stderr)
    {
        const int max = 400;
        if (stderr.Length <= max) return stderr;
        return "…" + stderr[^max..];
    }
}

// ── Record pubblici ──────────────────────────────────────────────────────────

/// <summary>Opzioni per l'estrazione frame da video.</summary>
internal sealed record ExtractOptions(
    string  InputPath,
    double  StartSec,
    double? EndSec,
    bool    UseFpsTarget,   // true = FPS target; false = ogni N frame
    double  FpsOrEveryN);   // valore FPS oppure N

/// <summary>Metadati video restituiti da ffprobe.</summary>
internal sealed record VideoMetadata(
    double DurationSec,
    double SourceFps,
    int    Width,
    int    Height);

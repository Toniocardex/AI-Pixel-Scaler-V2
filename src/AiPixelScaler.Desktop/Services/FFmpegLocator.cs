using System;
using System.Diagnostics;
using System.IO;
using AiPixelScaler.Desktop.Services;

namespace AiPixelScaler.Desktop.Services;

/// <summary>
/// Individua il percorso di <c>ffmpeg.exe</c> e <c>ffprobe.exe</c>.
/// Strategia: 1) PATH di sistema  2) cartella configurata manualmente in <see cref="UiPreferencesService"/>.
/// </summary>
internal static class FFmpegLocator
{
    /// <summary>
    /// Prova a individuare ffmpeg e ffprobe.
    /// Restituisce <c>true</c> se entrambi gli eseguibili sono stati trovati.
    /// </summary>
    internal static bool TryLocate(
        UiPreferencesService prefs,
        out string ffmpegPath,
        out string ffprobePath)
    {
        // 1. Cerca nel PATH di sistema
        var inPathFfmpeg  = FindInSystemPath("ffmpeg.exe");
        var inPathFfprobe = FindInSystemPath("ffprobe.exe");
        if (inPathFfmpeg is not null && inPathFfprobe is not null)
        {
            ffmpegPath  = inPathFfmpeg;
            ffprobePath = inPathFfprobe;
            return true;
        }

        // 2. Cerca nella cartella configurata manualmente
        var folder = prefs.LoadFfmpegFolder();
        if (!string.IsNullOrWhiteSpace(folder))
        {
            var f1 = Path.Combine(folder, "ffmpeg.exe");
            var f2 = Path.Combine(folder, "ffprobe.exe");
            if (File.Exists(f1) && File.Exists(f2))
            {
                ffmpegPath  = f1;
                ffprobePath = f2;
                return true;
            }
        }

        ffmpegPath  = string.Empty;
        ffprobePath = string.Empty;
        return false;
    }

    // ── Privati ──────────────────────────────────────────────────────────────

    /// <summary>
    /// Interroga <c>where</c> (Windows) per trovare un eseguibile nel PATH.
    /// Restituisce il percorso completo o <c>null</c> se non trovato.
    /// </summary>
    private static string? FindInSystemPath(string exeName)
    {
        try
        {
            using var proc = Process.Start(new ProcessStartInfo("where", exeName)
            {
                RedirectStandardOutput = true,
                UseShellExecute        = false,
                CreateNoWindow         = true
            });
            if (proc is null) return null;
            var line = proc.StandardOutput.ReadLine();
            proc.WaitForExit();
            return proc.ExitCode == 0 && !string.IsNullOrWhiteSpace(line)
                ? line.Trim()
                : null;
        }
        catch
        {
            return null;
        }
    }
}

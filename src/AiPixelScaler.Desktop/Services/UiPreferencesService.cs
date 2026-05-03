using System;
using System.IO;
using System.Text.Json;

namespace AiPixelScaler.Desktop.Services;

internal sealed class UiPreferencesService
{
    private sealed record UiPreferences(
        bool    ShowAdvancedTabs = false,
        string? FfmpegFolder     = null);

    private readonly string _path;

    internal UiPreferencesService()
    {
        var root = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "AiPixelScaler");
        Directory.CreateDirectory(root);
        _path = Path.Combine(root, "ui-preferences.json");
    }

    // ── Lettura / scrittura avanzate ─────────────────────────────────────────

    private UiPreferences Load()
    {
        try
        {
            if (!File.Exists(_path)) return new UiPreferences();
            var json = File.ReadAllText(_path);
            return JsonSerializer.Deserialize<UiPreferences>(json) ?? new UiPreferences();
        }
        catch { return new UiPreferences(); }
    }

    private void Save(UiPreferences prefs)
    {
        var json = JsonSerializer.Serialize(prefs, new JsonSerializerOptions { WriteIndented = true });
        File.WriteAllText(_path, json);
    }

    // ── API pubblica ─────────────────────────────────────────────────────────

    internal bool TryLoadShowAdvancedTabs(out bool showAdvanced)
    {
        var p = Load();
        showAdvanced = p.ShowAdvancedTabs;
        return File.Exists(_path);
    }

    internal void SaveShowAdvancedTabs(bool showAdvanced)
        => Save(Load() with { ShowAdvancedTabs = showAdvanced });

    /// <summary>Restituisce la cartella FFmpeg configurata manualmente, oppure <c>null</c>.</summary>
    internal string? LoadFfmpegFolder() => Load().FfmpegFolder;

    /// <summary>Salva la cartella FFmpeg nelle preferenze utente.</summary>
    internal void SaveFfmpegFolder(string folder)
        => Save(Load() with { FfmpegFolder = folder });
}

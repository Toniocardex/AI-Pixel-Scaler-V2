using System;
using System.IO;
using System.Text.Json;

namespace AiPixelScaler.Desktop.Services;

internal sealed class UiPreferencesService
{
    private sealed record UiPreferences(bool ShowAdvancedTabs = false);

    private readonly string _path;

    internal UiPreferencesService()
    {
        var root = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "AiPixelScaler");
        Directory.CreateDirectory(root);
        _path = Path.Combine(root, "ui-preferences.json");
    }

    internal bool TryLoadShowAdvancedTabs(out bool showAdvanced)
    {
        showAdvanced = false;
        try
        {
            if (!File.Exists(_path)) return false;
            var json = File.ReadAllText(_path);
            var model = JsonSerializer.Deserialize<UiPreferences>(json);
            if (model is null) return false;
            showAdvanced = model.ShowAdvancedTabs;
            return true;
        }
        catch
        {
            return false;
        }
    }

    internal void SaveShowAdvancedTabs(bool showAdvanced)
    {
        var json = JsonSerializer.Serialize(new UiPreferences(showAdvanced), new JsonSerializerOptions { WriteIndented = true });
        File.WriteAllText(_path, json);
    }
}

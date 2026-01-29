using System;
using System.Collections.Generic;
using System.IO;
using System.Text.Json;

namespace SPRK.Avalonia.Services;

public class AppSettings
{
    public string Hostname { get; set; } = "127.0.0.1";
    public int Port { get; set; } = 8008;
    public List<string> SavedHosts { get; set; } = [];
    public bool UsingBackground { get; set; } = false;
    public int CameraStreamPort { get; set; } = 8080;
}

public static class SettingsService
{
    private static readonly string SettingsPath = Path.Combine(
        Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData),
        "SPRK.Avalonia",
        "settings.json");

    private static AppSettings? _settings;

    public static AppSettings Default => _settings ??= Load();

    private static AppSettings Load()
    {
        try
        {
            if (File.Exists(SettingsPath))
            {
                var json = File.ReadAllText(SettingsPath);
                return JsonSerializer.Deserialize<AppSettings>(json) ?? new AppSettings();
            }
        }
        catch
        {
            // If loading fails, return defaults
        }
        return new AppSettings();
    }

    public static void Save()
    {
        try
        {
            var directory = Path.GetDirectoryName(SettingsPath);
            if (!string.IsNullOrEmpty(directory) && !Directory.Exists(directory))
            {
                Directory.CreateDirectory(directory);
            }

            var json = JsonSerializer.Serialize(_settings, new JsonSerializerOptions { WriteIndented = true });
            File.WriteAllText(SettingsPath, json);
        }
        catch
        {
            // Silently fail if saving doesn't work
        }
    }
}
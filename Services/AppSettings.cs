using System.IO;
using System.Text.Json;

namespace ZoomItToolbar.Services;

/// <summary>Persisted user preferences: the global hotkey and the last-used pen settings.</summary>
public sealed class AppSettings
{
    public uint HotkeyModifiers { get; set; } = (uint)(GlobalHotkeyService.Modifiers.Control | GlobalHotkeyService.Modifiers.Alt);
    public uint HotkeyVirtualKey { get; set; } = 0x44; // 'D'
    public string LastColor { get; set; } = "#FF4B4B";
    public double LastWidth { get; set; } = 4;

    private static string FilePath => Path.Combine(
        Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData),
        "ZoomItToolbar", "settings.json");

    public static AppSettings Load()
    {
        try
        {
            if (File.Exists(FilePath))
            {
                var json = File.ReadAllText(FilePath);
                var settings = JsonSerializer.Deserialize<AppSettings>(json);
                if (settings != null)
                    return settings;
            }
        }
        catch
        {
            // Missing/corrupt settings file — fall back to defaults below.
        }
        return new AppSettings();
    }

    public void Save()
    {
        try
        {
            Directory.CreateDirectory(Path.GetDirectoryName(FilePath)!);
            File.WriteAllText(FilePath, JsonSerializer.Serialize(this, new JsonSerializerOptions { WriteIndented = true }));
        }
        catch
        {
            // Best effort — failing to persist settings isn't fatal.
        }
    }
}

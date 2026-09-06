using System.IO;
using System.Text.Json;

namespace Markit.Services;

/// <summary>Persisted user preferences: the global hotkey and the last-used pen settings.</summary>
public sealed class AppSettings
{
    public uint HotkeyModifiers { get; set; } = (uint)(GlobalHotkeyService.Modifiers.Control | GlobalHotkeyService.Modifiers.Alt);
    public uint HotkeyVirtualKey { get; set; } = 0x44; // 'D'

    /// <summary>Starts a session restoring the last-saved ink for whichever monitor is under the cursor.</summary>
    public uint ResumeHotkeyModifiers { get; set; } = (uint)(GlobalHotkeyService.Modifiers.Control | GlobalHotkeyService.Modifiers.Alt);
    public uint ResumeHotkeyVirtualKey { get; set; } = 0x45; // 'E'

    public string LastPenColor { get; set; } = "#BF5AF2";
    public double LastPenWidth { get; set; } = 4;
    public string LastHighlighterColor { get; set; } = "#FFD54F";
    public double LastHighlighterWidth { get; set; } = 8;
    public double LastEraserWidth { get; set; } = 16;

    public string SaveFolder { get; set; } =
        Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.MyPictures), "Markit");

    private static string FilePath => Path.Combine(
        Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData),
        "Markit", "settings.json");

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

using System.IO;
using System.Windows.Ink;

namespace Markit.Services;

/// <summary>Persists ink strokes per physical monitor (keyed by
/// <see cref="MonitorSnapshot.DeviceName"/>) so a draw session can be resumed
/// later via the "resume last edits" hotkey.</summary>
public static class InkStore
{
    private static string DirectoryPath => Path.Combine(
        Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData),
        "Markit", "ink");

    private static string PathFor(string deviceName)
    {
        var safeName = string.Concat(deviceName.Select(c => char.IsLetterOrDigit(c) ? c : '_'));
        return Path.Combine(DirectoryPath, safeName + ".isf");
    }

    public static void Save(string deviceName, StrokeCollection strokes)
    {
        try
        {
            Directory.CreateDirectory(DirectoryPath);
            using var stream = File.Create(PathFor(deviceName));
            strokes.Save(stream);
        }
        catch
        {
            // Best effort — failing to persist ink isn't fatal.
        }
    }

    public static StrokeCollection? Load(string deviceName)
    {
        try
        {
            var path = PathFor(deviceName);
            if (!File.Exists(path))
                return null;
            using var stream = File.OpenRead(path);
            return new StrokeCollection(stream);
        }
        catch
        {
            return null; // missing/corrupt — start with a blank canvas instead
        }
    }
}

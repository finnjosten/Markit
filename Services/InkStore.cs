using System.IO;
using System.Windows.Ink;
using System.Windows.Media.Imaging;

namespace Markit.Services;

/// <summary>One saved drawing session for a monitor: when it was made, and where
/// its ink (.isf) and lowres preview thumbnail (.png) live on disk.</summary>
public sealed record HistoryEntry(DateTime Timestamp, string StrokesPath, string ThumbnailPath);

/// <summary>Persists ink strokes + a preview thumbnail per physical monitor (keyed by
/// <see cref="MonitorSnapshot.DeviceName"/>), one timestamped entry per session, so
/// past sessions can be resumed via "resume last edits" or browsed via History.
/// Entries older than <see cref="RetentionPeriod"/> are pruned automatically on save.</summary>
public static class InkStore
{
    private static readonly TimeSpan RetentionPeriod = TimeSpan.FromDays(7);
    private const string TimestampFormat = "yyyy-MM-dd_HH-mm-ss-fff";

    private static string DeviceDirectory(string deviceName)
    {
        var safeName = string.Concat(deviceName.Select(c => char.IsLetterOrDigit(c) ? c : '_'));
        return Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData),
            "Markit", "history", safeName);
    }

    public static void Save(string deviceName, StrokeCollection strokes, BitmapSource thumbnail)
    {
        try
        {
            var dir = DeviceDirectory(deviceName);
            Directory.CreateDirectory(dir);

            var stamp = DateTime.Now.ToString(TimestampFormat);

            using (var strokesStream = File.Create(Path.Combine(dir, stamp + ".isf")))
                strokes.Save(strokesStream);

            var encoder = new PngBitmapEncoder();
            encoder.Frames.Add(BitmapFrame.Create(thumbnail));
            using (var thumbnailStream = File.Create(Path.Combine(dir, stamp + ".png")))
                encoder.Save(thumbnailStream);

            Prune(dir);
        }
        catch
        {
            // Best effort — failing to persist history isn't fatal.
        }
    }

    private static void Prune(string dir)
    {
        var cutoff = DateTime.Now - RetentionPeriod;
        try
        {
            foreach (var file in Directory.GetFiles(dir))
            {
                if (File.GetLastWriteTime(file) < cutoff)
                    File.Delete(file);
            }
        }
        catch
        {
            // Best effort — a leftover old file or two isn't a real problem.
        }
    }

    /// <summary>The most recent entry's ink for this monitor, for "resume last edits". Null if none.</summary>
    public static StrokeCollection? LoadLatest(string deviceName) =>
        List(deviceName).FirstOrDefault() is { } entry ? LoadStrokes(entry) : null;

    public static StrokeCollection? LoadStrokes(HistoryEntry entry)
    {
        try
        {
            using var stream = File.OpenRead(entry.StrokesPath);
            return new StrokeCollection(stream);
        }
        catch
        {
            return null; // missing/corrupt — start with a blank canvas instead
        }
    }

    /// <summary>Entries from the last <see cref="RetentionPeriod"/> for this monitor, newest first.</summary>
    public static List<HistoryEntry> List(string deviceName)
    {
        var dir = DeviceDirectory(deviceName);
        var entries = new List<HistoryEntry>();
        if (!Directory.Exists(dir))
            return entries;

        var cutoff = DateTime.Now - RetentionPeriod;
        foreach (var strokesPath in Directory.GetFiles(dir, "*.isf"))
        {
            var stamp = Path.GetFileNameWithoutExtension(strokesPath);
            var thumbnailPath = Path.Combine(dir, stamp + ".png");
            if (!File.Exists(thumbnailPath))
                continue;
            if (!DateTime.TryParseExact(stamp, TimestampFormat, null,
                    System.Globalization.DateTimeStyles.None, out var timestamp))
                continue;
            if (timestamp < cutoff)
                continue;

            entries.Add(new HistoryEntry(timestamp, strokesPath, thumbnailPath));
        }

        entries.Sort((a, b) => b.Timestamp.CompareTo(a.Timestamp));
        return entries;
    }
}

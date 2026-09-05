using System.Text.Json;
using WWBB.Core;

namespace WindowsWorkspaceBlackBox;

internal static class AppData
{
    internal static readonly string Root = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "WindowsWorkspaceBlackBox");
    private static readonly object LogLock = new();
    internal static void Log(string message)
    {
        try
        {
            lock (LogLock)
            {
                var folder = Path.Combine(Root, "logs");
                Directory.CreateDirectory(folder);
                File.AppendAllText(Path.Combine(folder, $"{DateTime.Now:yyyyMMdd}.log"), $"{DateTime.Now:O} {message}{Environment.NewLine}");
            }
        }
        catch { /* Logging must not terminate recording. */ }
    }
    internal static Settings LoadSettings()
    {
        var path = Path.Combine(Root, "settings.json");
        if (!File.Exists(path)) return new();
        try
        {
            var result = JsonSerializer.Deserialize<Settings>(File.ReadAllText(path), Json.Options) ?? throw new InvalidDataException("Empty settings");
            result.IntervalMinutes = Math.Clamp(result.IntervalMinutes, 1, 60);
            result.Retention = Math.Clamp(result.Retention, 1, 1000);
            result.ExcludedExecutables ??= [];
            result.MonitoredApplications ??= [];
            return result;
        }
        catch (Exception e) { Log($"Settings invalid; automatic restore disabled: {e.Message}"); return new() { AutoRestore = false }; }
    }
    internal static void SaveSettings(Settings settings)
    {
        Directory.CreateDirectory(Root);
        var path = Path.Combine(Root, "settings.json");
        var temporary = path + ".new";
        using (var stream = new FileStream(temporary, FileMode.Create, FileAccess.Write, FileShare.None, 4096, FileOptions.WriteThrough))
        { JsonSerializer.Serialize(stream, settings, Json.Options); stream.Flush(true); }
        File.Move(temporary, path, true);
    }
}

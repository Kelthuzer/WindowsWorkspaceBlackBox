using System.Text.Json;
using System.Text.Json.Serialization;

namespace WWBB.Core;

public record Box(int X, int Y, int Width, int Height);
public record MonitorLayout(string Device, Box Bounds, Box WorkArea, uint Dpi = 96);
public enum WindowState { Normal, Minimized, Maximized }
public enum ApplicationRestoreMode { ObserveOnly, LaunchIfMissing }
public sealed record ApplicationRule
{
    public string ProcessName { get; init; } = "";
    public string ExePath { get; init; } = "";
    public ApplicationRestoreMode Mode { get; init; } = ApplicationRestoreMode.ObserveOnly;
}
public sealed record WindowEntry
{
    public string Id { get; init; } = Guid.NewGuid().ToString("N");
    public long Hwnd { get; init; }
    public int ProcessId { get; init; }
    public string ExePath { get; init; } = "";
    public string ProcessName { get; init; } = "";
    public string? CommandLine { get; init; }
    public string[] Arguments { get; init; } = [];
    public string Title { get; init; } = "";
    public string ClassName { get; init; } = "";
    // Normal bounds are in physical screen coordinates, not workspace coordinates.
    public Box NormalBounds { get; init; } = new(0, 0, 800, 600);
    public WindowState State { get; init; }
    public MonitorLayout Monitor { get; init; } = new("", new(0,0,1920,1080), new(0,0,1920,1040));
    public bool WasForeground { get; init; }
}
public sealed record ExplorerEntry(string Path, long TabHwnd, WindowEntry Window);
public sealed record Snapshot
{
    public int SchemaVersion { get; init; } = 1;
    public DateTimeOffset CapturedAt { get; init; } = DateTimeOffset.UtcNow;
    public string Reason { get; set; } = "manual";
    public List<WindowEntry> Windows { get; init; } = [];
    public List<ExplorerEntry> Explorer { get; init; } = [];
    public List<string> Warnings { get; init; } = [];
    public bool ExplorerComplete { get; set; } = true;
}
public sealed class Settings
{
    public bool AutoRestore { get; set; } = true;
    public bool StartWithWindows { get; set; } = true;
    public int IntervalMinutes { get; set; } = 5;
    public int Retention { get; set; } = 100;
    public string? PendingSnapshot { get; set; }
    // Legacy observe-only rules are disabled by the binary policy.
    public bool ApplicationRulesConfigured { get; set; }
    public bool BinaryRulesConfigured { get; set; }
    public bool RestoreExplorer { get; set; } = true;
    public List<ApplicationRule> MonitoredApplications { get; set; } = [];
    public List<string> ExcludedExecutables { get; set; } = [];
}
public static class ApplicationPolicy
{
    public static ApplicationRule? RuleFor(Settings settings, WindowEntry window)
    {
        if (!settings.BinaryRulesConfigured && new[] { "steam", "steamwebhelper", "Telegram", "fdm" }
            .Contains(window.ProcessName, StringComparer.OrdinalIgnoreCase)) return null;
        if (!settings.ApplicationRulesConfigured) return null;
        return settings.MonitoredApplications.FirstOrDefault(rule =>
            rule.Mode == ApplicationRestoreMode.LaunchIfMissing &&
            ((!string.IsNullOrWhiteSpace(rule.ExePath)
                && rule.ExePath.Equals(window.ExePath, StringComparison.OrdinalIgnoreCase))
            || (string.IsNullOrWhiteSpace(rule.ExePath)
                && rule.ProcessName.Equals(window.ProcessName, StringComparison.OrdinalIgnoreCase))));
    }

}
public static class Json
{
    public static readonly JsonSerializerOptions Options = new()
    {
        WriteIndented = true,
        Converters = { new JsonStringEnumConverter() }
    };
}

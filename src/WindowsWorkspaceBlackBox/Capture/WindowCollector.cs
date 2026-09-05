using System.Diagnostics;
using System.Management;
using WindowsWorkspaceBlackBox.Native;
using WWBB.Core;

namespace WindowsWorkspaceBlackBox.Capture;

internal static class WindowCollector
{
    private static readonly HashSet<string> IgnoredClasses = ["Progman", "WorkerW", "Shell_TrayWnd", "Shell_SecondaryTrayWnd", "tooltips_class32", "DV2ControlHost"];
    internal static List<WindowEntry> Capture(bool fast)
    {
        var windows = new List<WindowEntry>();
        var processes = new Dictionary<int, (string Name, string Exe, string? Line)>();
        var foreground = Win32.GetForegroundWindow();
        Win32.EnumWindows((hwnd, _) =>
        {
            try
            {
                if (!Win32.IsWindowVisible(hwnd) || IgnoredClasses.Contains(Win32.Class(hwnd))) return true;
                var style = Win32.GetWindowLongPtr(hwnd, -20).ToInt64();
                if ((style & 0x80) != 0 || (Win32.GetWindow(hwnd, 4) != 0 && (style & 0x40000) == 0)) return true;
                if (Win32.DwmGetWindowAttribute(hwnd, 14, out var cloaked, sizeof(int)) == 0 && cloaked != 0) return true;
                if (!Win32.GetWindowRect(hwnd, out var rect) || rect.Box.Width <= 0 || rect.Box.Height <= 0) return true;
                Win32.GetWindowThreadProcessId(hwnd, out var pid);
                if (!processes.TryGetValue((int)pid, out var process))
                {
                    using var p = Process.GetProcessById((int)pid);
                    if (p.ProcessName.Equals("WindowsWorkspaceBlackBox", StringComparison.OrdinalIgnoreCase)) return true;
                    string exe = "";
                    try { exe = p.MainModule?.FileName ?? ""; } catch { /* elevated process: still retain geometry */ }
                    string? line = null;
                    if (!fast)
                        try
                        {
                            using var search = new ManagementObjectSearcher($"SELECT CommandLine FROM Win32_Process WHERE ProcessId = {pid}");
                            search.Options.Timeout = TimeSpan.FromSeconds(2);
                            using var results = search.Get();
                            foreach (ManagementObject item in results) { using (item) line = item["CommandLine"] as string; }
                        }
                        catch { /* command line is optional */ }
                    processes[(int)pid] = process = (p.ProcessName, exe, line);
                }
                var placement = Win32.Placement.New();
                if (!Win32.GetWindowPlacement(hwnd, ref placement)) return true;
                var monitor = Win32.Monitor(hwnd);
                var box = placement.NormalPosition.Box;
                // WINDOWPLACEMENT uses workspace coordinates for ordinary top-level windows.
                box = box with { X = box.X + monitor.WorkArea.X - monitor.Bounds.X, Y = box.Y + monitor.WorkArea.Y - monitor.Bounds.Y };
                if (box.Width <= 0 || box.Height <= 0) return true;
                windows.Add(new()
                {
                    Hwnd = hwnd.ToInt64(), ProcessId = (int)pid, ExePath = process.Exe, ProcessName = process.Name,
                    CommandLine = process.Line, Arguments = Win32.Arguments(process.Line), Title = Win32.Title(hwnd), ClassName = Win32.Class(hwnd),
                    NormalBounds = box, Monitor = monitor, WasForeground = hwnd == foreground,
                    State = placement.ShowCmd == 3 ? WindowState.Maximized : placement.ShowCmd == 2 ? WindowState.Minimized : WindowState.Normal
                });
            }
            catch { /* A window can disappear during enumeration. */ }
            return true;
        }, 0);
        return windows;
    }
}

using System.Diagnostics;
using WindowsWorkspaceBlackBox.Capture;
using WindowsWorkspaceBlackBox.Native;
using WWBB.Core;

namespace WindowsWorkspaceBlackBox.Restore;

internal sealed class RestoreReport
{
    internal int Restored { get; set; }
    internal List<string> Failures { get; } = [];
    internal bool Success => Failures.Count == 0;
}

internal sealed class RestoreEngine(Action<string> status)
{
    private readonly HashSet<long> used = [];
    private readonly HashSet<string> usedFolders = [];
    private readonly Dictionary<string, nint> matched = [];
    private readonly HashSet<string> launched = new(StringComparer.OrdinalIgnoreCase);
    private readonly Dictionary<string, int> assignedProcesses = new(StringComparer.OrdinalIgnoreCase);
    private readonly RestoreReport report = new();

    internal async Task<RestoreReport> RunAsync(Snapshot saved, Settings settings, CancellationToken ct)
    {
        AppData.Log($"Restore started: {saved.CapturedAt:O}");
        var live = await CaptureClient.CaptureAsync(false, ct);
        var wanted = saved.Windows.Where(w => !ExplorerCollector.IsExplorer(w)
            && !settings.ExcludedExecutables.Contains(w.ProcessName, StringComparer.OrdinalIgnoreCase)).ToList();
        // Assign strongest matches first so a generic match cannot steal another document's exact title.
        var pairs = (from s in wanted from w in live.Windows let score = Matching.Score(s, w)
                     where score >= 100 orderby score descending select (Saved: s, Live: w)).ToList();
        foreach (var pair in pairs)
            if (!matched.ContainsKey(pair.Saved.Id) && !used.Contains(pair.Live.Hwnd)) Apply(pair.Saved, pair.Live);

        foreach (var window in wanted.Where(w => !matched.ContainsKey(w.Id)))
        {
            ct.ThrowIfCancellationRequested();
            status($"Приложение: {window.ProcessName}");
            try
            {
                var current = Matching.Best(window, live.Windows, used);
                if (current == null)
                {
                    var group = ProcessGroup(window);
                    // A saved multi-window process is launched once. Distinct saved processes
                    // may be reopened independently after existing processes have been assigned.
                    var running = RunningProcessIds(window.ExePath);
                    bool unclaimed = running.Any(pid => !assignedProcesses.Values.Contains(pid));
                    if (!assignedProcesses.ContainsKey(group) && !unclaimed && launched.Add(group)) Launch(window);
                    var deadline = Stopwatch.StartNew();
                    while (deadline.Elapsed < TimeSpan.FromSeconds(30))
                    {
                        await Task.Delay(750, ct);
                        live = await CaptureClient.CaptureAsync(true, ct);
                        current = Matching.Best(window, live.Windows, used);
                        if (current != null) break;
                        // Single-instance tray applications often keep a background process but
                        // create/show their main window only when invoked again.
                        if (unclaimed && deadline.Elapsed >= TimeSpan.FromSeconds(3) && launched.Add(group))
                            Launch(window);
                    }
                }
                if (current == null) Failure($"Не найдено окно: {window.ProcessName} — {window.Title}");
                else Apply(window, current);
            }
            catch (Exception e) when (e is not OperationCanceledException) { Failure($"{window.ProcessName}: {e.Message}"); }
        }

        var retry = new List<ExplorerEntry>();
        foreach (var entry in saved.Explorer)
        {
            ct.ThrowIfCancellationRequested();
            if (!await RestoreFolder(entry, ct)) retry.Add(entry);
        }
        // Retry unresolved UNC folders after 5, 15, 30 and 60 seconds; local folders are not delayed by backoff.
        int previous = 0;
        foreach (var seconds in new[] { 5, 15, 30, 60 })
        {
            if (!retry.Any(e => e.Path.StartsWith("\\\\"))) break;
            status($"Ожидание сетевых папок: {retry.Count}");
            await Task.Delay(TimeSpan.FromSeconds(seconds - previous), ct);
            previous = seconds;
            foreach (var entry in retry.Where(e => e.Path.StartsWith("\\\\")).ToList())
                if (await RestoreFolder(entry, ct)) retry.Remove(entry);
        }
        foreach (var entry in retry) Failure($"Папка недоступна или окно не найдено: {entry.Path}");

        var foreground = saved.Windows.FirstOrDefault(w => w.WasForeground);
        if (foreground != null && matched.TryGetValue(foreground.Id, out var hwnd))
            if (!Win32.SetForegroundWindow(hwnd)) AppData.Log("Foreground activation denied by Windows");
        AppData.Log($"Restore completed: restored={report.Restored}, failures={report.Failures.Count}");
        return report;
    }

    private static List<int> RunningProcessIds(string exe)
    {
        var ids = new List<int>();
        if (string.IsNullOrWhiteSpace(exe)) return ids;
        foreach (var process in Process.GetProcessesByName(Path.GetFileNameWithoutExtension(exe)))
        {
            using (process)
                try { if (string.Equals(process.MainModule?.FileName, exe, StringComparison.OrdinalIgnoreCase)) ids.Add(process.Id); }
                catch { /* Inaccessible process cannot be identified safely. */ }
        }
        return ids;
    }

    private void Launch(WindowEntry window)
    {
        if (string.IsNullOrWhiteSpace(window.ExePath) || !File.Exists(window.ExePath)) throw new FileNotFoundException("EXE недоступен", window.ExePath);
        var start = new ProcessStartInfo(window.ExePath) { UseShellExecute = false, WorkingDirectory = Path.GetDirectoryName(window.ExePath)! };
        // Interpreter command lines can repeat a script instead of reopening its window.
        var interpreters = new[] { "cmd", "powershell", "pwsh", "wscript", "cscript", "mshta", "rundll32", "regsvr32", "conhost", "wt", "WindowsTerminal" };
        if (!interpreters.Contains(window.ProcessName, StringComparer.OrdinalIgnoreCase))
            foreach (var arg in Matching.SafeLaunchArguments(window.Arguments)) start.ArgumentList.Add(arg);
        else AppData.Log($"Interpreter arguments omitted: {window.ProcessName}");
        using var launchedProcess = Process.Start(start);
        AppData.Log($"Launch: {window.ExePath}");
    }

    private async Task<bool> RestoreFolder(ExplorerEntry entry, CancellationToken ct)
    {
        status($"Папка: {entry.Path}");
        try
        {
            var live = await CaptureClient.CaptureAsync(true, ct);
            var current = FindFolder(entry, live);
            if (current == null)
            {
                if (entry.Path.StartsWith("\\\\") && !await ProbePath(entry.Path, ct)) return false;
                string group = $"folder|{entry.Window.Hwnd}|{entry.TabHwnd}|{entry.Path}";
                if (launched.Add(group))
                {
                    var start = new ProcessStartInfo(Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.Windows), "explorer.exe")) { UseShellExecute = false };
                    start.ArgumentList.Add("/n,");
                    start.ArgumentList.Add(entry.Path.StartsWith("::") ? "shell:" + entry.Path : entry.Path);
                    using var process = Process.Start(start);
                    AppData.Log($"Explorer launch: {entry.Path}");
                }
                var wait = Stopwatch.StartNew();
                while (wait.Elapsed < TimeSpan.FromSeconds(20))
                {
                    await Task.Delay(750, ct);
                    live = await CaptureClient.CaptureAsync(true, ct);
                    current = FindFolder(entry, live);
                    if (current != null) break;
                }
            }
            if (current == null) return false;
            usedFolders.Add(FolderKey(current));
            Apply(entry.Window, current.Window);
            return true;
        }
        catch (Exception e) when (e is not OperationCanceledException) { AppData.Log($"Explorer restore: {e.Message}"); return false; }
    }
    private ExplorerEntry? FindFolder(ExplorerEntry entry, Snapshot live) => live.Explorer.FirstOrDefault(e => Matching.SamePath(e.Path, entry.Path) && !usedFolders.Contains(FolderKey(e)));
    private static string FolderKey(ExplorerEntry e) => $"{e.Window.Hwnd}|{e.TabHwnd}|{e.Path.ToUpperInvariant()}";

    private static async Task<bool> ProbePath(string path, CancellationToken ct)
    {
        var start = new ProcessStartInfo(Environment.ProcessPath!) { UseShellExecute = false, CreateNoWindow = true };
        start.ArgumentList.Add("--probe-path"); start.ArgumentList.Add(path);
        using var process = Process.Start(start)!;
        using var limit = CancellationTokenSource.CreateLinkedTokenSource(ct);
        limit.CancelAfter(TimeSpan.FromSeconds(4));
        try { await process.WaitForExitAsync(limit.Token); return process.ExitCode == 0; }
        catch (OperationCanceledException) when (!ct.IsCancellationRequested) { return false; }
        finally { if (!process.HasExited) process.Kill(true); }
    }

    private void Apply(WindowEntry saved, WindowEntry live)
    {
        nint hwnd = (nint)live.Hwnd;
        if (!Win32.IsWindow(hwnd)) { Failure($"Окно исчезло: {saved.Title}"); return; }
        var screen = Screen.AllScreens.FirstOrDefault(s => s.DeviceName == saved.Monitor.Device) ?? Screen.PrimaryScreen!;
        using var dpiWindow = new DpiWindow(screen.Bounds.Location);
        var target = new MonitorLayout(screen.DeviceName, B(screen.Bounds), B(screen.WorkingArea), dpiWindow.Dpi);
        var box = Matching.Place(saved, target);
        Win32.ShowWindowAsync(hwnd, 9);
        if (!Win32.SetWindowPos(hwnd, 0, box.X, box.Y, box.Width, box.Height, 0x14 | 0x4000))
        { Failure($"Не удалось переместить: {saved.Title}"); return; }
        var placement = Win32.Placement.New();
        if (!Win32.GetWindowPlacement(hwnd, ref placement)) { Failure($"GetWindowPlacement: {saved.Title}"); return; }
        placement.NormalPosition = Win32.Rect.From(box with { X = box.X - target.WorkArea.X + target.Bounds.X, Y = box.Y - target.WorkArea.Y + target.Bounds.Y });
        placement.ShowCmd = saved.State == WindowState.Maximized ? 3 : saved.State == WindowState.Minimized ? 2 : 1;
        placement.Flags = 4; // WPF_ASYNCWINDOWPLACEMENT: do not block on another process's UI thread.
        if (!Win32.SetWindowPlacement(hwnd, ref placement)) { Failure($"Не удалось вернуть размер/состояние: {saved.Title}"); return; }
        used.Add(live.Hwnd); matched[saved.Id] = hwnd; assignedProcesses[ProcessGroup(saved)] = live.ProcessId; report.Restored++;
        AppData.Log($"Window matched/restored: {saved.ProcessName}, HWND={live.Hwnd}, monitor={target.Device}");
    }
    private static Box B(Rectangle r) => new(r.X, r.Y, r.Width, r.Height);
    private static string ProcessGroup(WindowEntry window) => $"{window.ExePath}|{window.ProcessId}";
    private void Failure(string message) { report.Failures.Add(message); AppData.Log("Restore failure: " + message); }

    private sealed class DpiWindow : NativeWindow, IDisposable
    {
        internal DpiWindow(Point origin) => CreateHandle(new CreateParams
        { X = origin.X + 1, Y = origin.Y + 1, Width = 1, Height = 1, Style = unchecked((int)0x80000000) });
        internal uint Dpi => Math.Max(96u, Win32.GetDpiForWindow(Handle));
        public void Dispose() => DestroyHandle();
    }
}

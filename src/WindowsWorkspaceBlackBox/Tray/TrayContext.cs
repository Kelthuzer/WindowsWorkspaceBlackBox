using System.Diagnostics;
using Microsoft.Win32;
using WindowsWorkspaceBlackBox.Capture;
using WindowsWorkspaceBlackBox.Native;
using WindowsWorkspaceBlackBox.Restore;
using WWBB.Core;

namespace WindowsWorkspaceBlackBox.Tray;

internal sealed class TrayContext : ApplicationContext
{
    private readonly Icon appIcon = Icon.ExtractAssociatedIcon(Environment.ProcessPath!) ?? (Icon)SystemIcons.Application.Clone();
    private readonly NotifyIcon icon = new() { Visible = true, Text = "Windows Workspace BlackBox" };
    private readonly System.Windows.Forms.Timer timer = new();
    private readonly System.Windows.Forms.Timer foregroundTracker = new() { Interval = 250 };
    private readonly CancellationTokenSource lifetime = new();
    private CancellationTokenSource? operation;
    private readonly Settings settings = AppData.LoadSettings();
    private readonly SnapshotStore store = new(Path.Combine(AppData.Root, "snapshots"), AppData.Log);
    private readonly ToolStripMenuItem state = new("Запуск…") { Enabled = false };
    private readonly ToolStripMenuItem cancel = new("Отменить восстановление") { Enabled = false };
    private readonly List<ToolStripMenuItem> actions = [];
    private bool busy, stopping, locked;
    private long lastUserForeground;

    internal TrayContext(bool startup)
    {
        icon.Icon = appIcon;
        var menu = new ContextMenuStrip();
        menu.Items.Add(new ToolStripMenuItem("Windows Workspace BlackBox · 0.1.0") { Enabled = false });
        menu.Items.Add(state); menu.Items.Add(new ToolStripSeparator());
        Add(menu, "Сохранить состояние сейчас", () => SaveAsync("manual"));
        Add(menu, "Восстановить последний снимок", RestoreLastAsync);
        Add(menu, "Выбрать снимок для восстановления", ChooseAsync);
        menu.Items.Add(cancel); cancel.Click += (_, _) => operation?.Cancel();
        Add(menu, "Продолжить без восстановления", () => { settings.PendingSnapshot = null; AppData.SaveSettings(settings); SetState("Запись", appIcon); return Task.CompletedTask; });
        menu.Items.Add(new ToolStripSeparator());
        Add(menu, "Настройки", EditSettingsAsync);
        menu.Items.Add("Открыть папку данных", null, (_, _) => { Directory.CreateDirectory(AppData.Root); Process.Start(new ProcessStartInfo(AppData.Root) { UseShellExecute = true }); });
        menu.Items.Add("Выход", null, (_, _) => ExitThread());
        icon.ContextMenuStrip = menu;
        icon.DoubleClick += async (_, _) => await GuardAsync(() => SaveAsync("manual"));
        timer.Tick += async (_, _) => { if (!locked) await GuardAsync(() => SaveAsync("timer")); };
        foregroundTracker.Tick += (_, _) => TrackForeground();
        foregroundTracker.Start();
        SystemEvents.SessionEnding += SessionEnding;
        SystemEvents.SessionSwitch += SessionSwitch;
        // Run after the WinForms message loop establishes its synchronization context.
        var first = new System.Windows.Forms.Timer { Interval = 250 };
        first.Tick += async (_, _) => { first.Stop(); first.Dispose(); await GuardAsync(() => InitializeAsync(startup)); };
        first.Start();
    }

    private void Add(ContextMenuStrip menu, string text, Func<Task> action)
    {
        var item = new ToolStripMenuItem(text); actions.Add(item); menu.Items.Add(item);
        item.Click += async (_, _) => await GuardAsync(action);
    }
    private async Task GuardAsync(Func<Task> action)
    {
        if (busy || stopping) return;
        busy = true; timer.Stop(); foreach (var item in actions) item.Enabled = false;
        try { await action(); }
        catch (OperationCanceledException) { SetState("Отменено — исходный снимок сохранён", SystemIcons.Warning); }
        catch (Exception e) { AppData.Log(e.ToString()); SetState("Ошибка — см. журнал", SystemIcons.Error); Balloon(e.Message, ToolTipIcon.Error); }
        finally
        {
            busy = false;
            if (!stopping)
            {
                foreach (var item in actions) item.Enabled = true;
                timer.Interval = settings.IntervalMinutes * 60_000;
                timer.Start(); // Manual saves reset the full interval.
            }
        }
    }

    private async Task InitializeAsync(bool startup)
    {
        Autostart.Set(settings.StartWithWindows);
        if (settings.PendingSnapshot != null && store.Read(settings.PendingSnapshot) == null) settings.PendingSnapshot = null;
        if (settings.PendingSnapshot == null && startup) settings.PendingSnapshot = store.Latest()?.Path;
        AppData.SaveSettings(settings);
        if (startup && settings.AutoRestore && settings.PendingSnapshot != null)
        {
            var wait = Stopwatch.StartNew();
            while (Win32.GetShellWindow() == 0 && wait.Elapsed < TimeSpan.FromSeconds(60)) await Task.Delay(1000, lifetime.Token);
            if (Win32.GetShellWindow() == 0) throw new TimeoutException("Рабочий стол не готов. Исходный снимок сохранён.");
            // Give ordinary HKCU/Startup applications (Steam, Telegram, etc.) time
            // to create their own windows before the restore engine considers launch fallbacks.
            SetState("Ожидание автозапуска приложений…", SystemIcons.Information);
            await Task.Delay(TimeSpan.FromSeconds(15), lifetime.Token);
            await RestoreLastAsync();
        }
        else if (settings.PendingSnapshot != null) { SetState("Запись · доступен снимок до перезапуска", SystemIcons.Information); Balloon("Снимок до перезапуска сохранён. Восстановить его можно из меню."); }
        else SetState("Запись", appIcon);
    }

    private async Task<bool> SaveAsync(string reason)
    {
        if (stopping || locked) return false;
        SetState("Сохранение…", SystemIcons.Information);
        AppData.Log("Snapshot started: " + reason);
        var snapshot = await CaptureClient.CaptureAsync(false, lifetime.Token, lastUserForeground);
        if (stopping || locked) return false;
        foreach (var warning in snapshot.Warnings) AppData.Log(warning);
        if (!snapshot.ExplorerComplete) throw new InvalidDataException("Не все папки Explorer доступны для чтения. Предыдущий снимок сохранён; подробности в журнале.");
        snapshot.Reason = reason;
        var path = await store.SaveAsync(snapshot, lifetime.Token);
        AppData.Log($"Snapshot saved: {Path.GetFileName(path)}, windows={snapshot.Windows.Count}, explorer={snapshot.Explorer.Count}");
        var keep = settings.Retention; var pinned = settings.PendingSnapshot;
        _ = Task.Run(async () => { try { await store.CleanupAsync(keep, pinned); } catch (Exception e) { AppData.Log($"Cleanup: {e}"); } });
        SetState($"Запись · сохранено {DateTime.Now:HH:mm}", appIcon);
        if (reason == "manual") Balloon($"Сохранено: окон — {snapshot.Windows.Count}, папок — {snapshot.Explorer.Count}.");
        return true;
    }

    private Task RestoreLastAsync()
    {
        var path = settings.PendingSnapshot ?? store.Latest()?.Path;
        if (path == null) { Balloon("Сохранённых снимков пока нет."); return Task.CompletedTask; }
        return RestoreAsync(path);
    }
    private async Task ChooseAsync()
    {
        Directory.CreateDirectory(store.DirectoryPath);
        using var picker = new OpenFileDialog { Title = "Выбрать снимок", InitialDirectory = store.DirectoryPath, Filter = "Снимки WWBB (*.json)|*.json", CheckFileExists = true };
        if (picker.ShowDialog() == DialogResult.OK) await RestoreAsync(picker.FileName);
    }
    private async Task RestoreAsync(string path)
    {
        var snapshot = store.Read(path) ?? throw new InvalidDataException("Снимок повреждён или имеет неподдерживаемый формат.");
        settings.PendingSnapshot = path; AppData.SaveSettings(settings);
        operation = CancellationTokenSource.CreateLinkedTokenSource(lifetime.Token); cancel.Enabled = true;
        try
        {
            var report = await new RestoreEngine(s => SetState(s, SystemIcons.Information)).RunAsync(snapshot, settings, operation.Token);
            if (report.Success)
            {
                // Keep source pinned until the control snapshot has actually reached disk.
                if (await SaveAsync("post-restore"))
                { settings.PendingSnapshot = null; AppData.SaveSettings(settings); }
                Balloon($"Восстановлено окон и папок: {report.Restored}.");
            }
            else
            {
                SetState($"Восстановлено частично · ошибок {report.Failures.Count}", SystemIcons.Warning);
                Balloon($"Восстановлено: {report.Restored}. Не удалось: {report.Failures.Count}. Исходный снимок сохранён, повтор доступен из меню.", ToolTipIcon.Warning);
            }
        }
        finally { operation.Dispose(); operation = null; cancel.Enabled = false; }
    }
    private Task EditSettingsAsync()
    {
        var known = store.Latest()?.Value.Windows ?? [];
        using var form = new SettingsForm(settings, known);
        if (form.ShowDialog() == DialogResult.OK)
        {
            form.Apply(settings); Autostart.Set(settings.StartWithWindows); AppData.SaveSettings(settings);
        }
        return Task.CompletedTask;
    }
    private void SetState(string text, Icon glyph)
    {
        if (stopping) return;
        state.Text = text; icon.Icon = glyph;
        icon.Text = ("WWBB · " + text)[..Math.Min(63, 7 + text.Length)];
    }
    private void Balloon(string text, ToolTipIcon type = ToolTipIcon.Info)
    { if (!stopping) icon.ShowBalloonTip(5000, "Windows Workspace BlackBox", text, type); }
    private void TrackForeground()
    {
        var hwnd = Win32.GetForegroundWindow();
        if (hwnd == 0 || !Win32.IsWindowVisible(hwnd)) return;
        var className = Win32.Class(hwnd);
        if (className is "Shell_TrayWnd" or "Shell_SecondaryTrayWnd" or "NotifyIconOverflowWindow" or "#32768") return;
        Win32.GetWindowThreadProcessId(hwnd, out var pid);
        if (pid == Environment.ProcessId) return;
        lastUserForeground = hwnd.ToInt64();
    }
    private void SessionEnding(object? sender, SessionEndingEventArgs e) { stopping = true; lifetime.Cancel(); }
    private void SessionSwitch(object? sender, SessionSwitchEventArgs e)
    {
        if (e.Reason is SessionSwitchReason.SessionLock or SessionSwitchReason.SessionLogoff) locked = true;
        if (e.Reason is SessionSwitchReason.SessionUnlock or SessionSwitchReason.SessionLogon) locked = false;
    }
    protected override void ExitThreadCore()
    {
        stopping = true; lifetime.Cancel(); timer.Stop();
        SystemEvents.SessionEnding -= SessionEnding; SystemEvents.SessionSwitch -= SessionSwitch;
        icon.Visible = false; icon.Dispose(); appIcon.Dispose(); timer.Dispose(); foregroundTracker.Stop(); foregroundTracker.Dispose();
        base.ExitThreadCore();
    }
}

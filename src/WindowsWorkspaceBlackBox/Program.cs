using System.Text.Json;
using WindowsWorkspaceBlackBox.Capture;
using WindowsWorkspaceBlackBox.Tray;
using WWBB.Core;

namespace WindowsWorkspaceBlackBox;

internal static class Program
{
    [STAThread]
    private static int Main(string[] args)
    {
        ApplicationConfiguration.Initialize();
        if (args.Contains("--probe-path"))
        {
            var index = Array.IndexOf(args, "--probe-path");
            return index + 1 < args.Length && Directory.Exists(args[index + 1]) ? 0 : 1;
        }
        if (args.Contains("--capture"))
        {
            try
            {
                var snapshot = new Snapshot { Windows = WindowCollector.Capture(args.Contains("--fast")) };
                ExplorerCollector.Capture(snapshot);
                using var output = Console.OpenStandardOutput();
                JsonSerializer.Serialize(output, snapshot, Json.Options);
                return 0;
            }
            catch (Exception e) { Console.Error.WriteLine(e); return 1; }
        }
        if (args.Contains("--register-startup")) { Autostart.Set(true); return 0; }
        if (args.Contains("--unregister-startup")) { Autostart.Set(false); return 0; }
        using var mutex = new Mutex(true, @"Local\WindowsWorkspaceBlackBox", out var created);
        if (!created) return 0;
        Application.SetUnhandledExceptionMode(UnhandledExceptionMode.CatchException);
        Application.ThreadException += (_, e) => { AppData.Log(e.Exception.ToString()); MessageBox.Show(e.Exception.Message, "Windows Workspace BlackBox"); };
        AppDomain.CurrentDomain.UnhandledException += (_, e) => AppData.Log(e.ExceptionObject.ToString() ?? "Unhandled exception");
        try { Application.Run(new TrayContext(args.Contains("--startup"))); }
        catch (Exception e) { AppData.Log(e.ToString()); MessageBox.Show(e.Message, "Windows Workspace BlackBox"); return 1; }
        finally { mutex.ReleaseMutex(); }
        return 0;
    }
}

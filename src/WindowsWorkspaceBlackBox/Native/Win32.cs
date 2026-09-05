using System.Runtime.InteropServices;
using System.Text;
using WWBB.Core;

namespace WindowsWorkspaceBlackBox.Native;

internal static class Win32
{
    internal delegate bool EnumProc(nint hwnd, nint param);
    [StructLayout(LayoutKind.Sequential)] internal struct Point { public int X, Y; }
    [StructLayout(LayoutKind.Sequential)] internal struct Rect
    {
        public int Left, Top, Right, Bottom;
        public readonly Box Box => new(Left, Top, Right - Left, Bottom - Top);
        public static Rect From(Box b) => new() { Left = b.X, Top = b.Y, Right = b.X + b.Width, Bottom = b.Y + b.Height };
    }
    [StructLayout(LayoutKind.Sequential)] internal struct Placement
    {
        public int Length, Flags, ShowCmd;
        public Point MinPosition, MaxPosition;
        public Rect NormalPosition;
        public static Placement New() => new() { Length = Marshal.SizeOf<Placement>() };
    }
    [StructLayout(LayoutKind.Sequential, CharSet = CharSet.Unicode)] internal struct MonitorInfo
    {
        public int Size;
        public Rect Monitor, Work;
        public uint Flags;
        [MarshalAs(UnmanagedType.ByValTStr, SizeConst = 32)] public string Device;
    }
    [DllImport("user32.dll")] internal static extern bool EnumWindows(EnumProc callback, nint data);
    [DllImport("user32.dll")] internal static extern bool EnumChildWindows(nint parent, EnumProc callback, nint data);
    [DllImport("user32.dll")] internal static extern bool IsWindowVisible(nint hwnd);
    [DllImport("user32.dll")] internal static extern bool IsWindow(nint hwnd);
    [DllImport("user32.dll")] internal static extern nint GetAncestor(nint hwnd, uint flags);
    [DllImport("user32.dll")] internal static extern nint GetWindow(nint hwnd, uint command);
    [DllImport("user32.dll", EntryPoint = "GetWindowLongPtrW")] internal static extern nint GetWindowLongPtr(nint hwnd, int index);
    [DllImport("user32.dll")] internal static extern uint GetWindowThreadProcessId(nint hwnd, out uint pid);
    [DllImport("user32.dll", CharSet = CharSet.Unicode)] internal static extern int GetWindowText(nint hwnd, StringBuilder text, int count);
    [DllImport("user32.dll", CharSet = CharSet.Unicode)] internal static extern int GetClassName(nint hwnd, StringBuilder text, int count);
    [DllImport("user32.dll")] internal static extern bool GetWindowRect(nint hwnd, out Rect rect);
    [DllImport("user32.dll")] internal static extern bool GetWindowPlacement(nint hwnd, ref Placement placement);
    [DllImport("user32.dll")] internal static extern bool SetWindowPlacement(nint hwnd, ref Placement placement);
    [DllImport("user32.dll")] internal static extern bool ShowWindowAsync(nint hwnd, int command);
    [DllImport("user32.dll")] internal static extern bool SetWindowPos(nint hwnd, nint after, int x, int y, int width, int height, uint flags);
    [DllImport("user32.dll")] internal static extern nint GetForegroundWindow();
    [DllImport("user32.dll")] internal static extern bool SetForegroundWindow(nint hwnd);
    [DllImport("user32.dll")] internal static extern nint GetShellWindow();
    [DllImport("user32.dll")] internal static extern nint MonitorFromWindow(nint hwnd, uint flags);
    [DllImport("user32.dll", CharSet = CharSet.Unicode)] internal static extern bool GetMonitorInfo(nint monitor, ref MonitorInfo info);
    [DllImport("user32.dll")] internal static extern uint GetDpiForWindow(nint hwnd);
    [DllImport("dwmapi.dll")] internal static extern int DwmGetWindowAttribute(nint hwnd, int attribute, out int value, int size);
    [DllImport("shell32.dll", CharSet = CharSet.Unicode)] internal static extern nint CommandLineToArgvW(string commandLine, out int argc);
    [DllImport("kernel32.dll")] internal static extern nint LocalFree(nint memory);

    internal static string Class(nint hwnd) { var b = new StringBuilder(256); GetClassName(hwnd, b, b.Capacity); return b.ToString(); }
    internal static string Title(nint hwnd) { var b = new StringBuilder(32768); GetWindowText(hwnd, b, b.Capacity); return b.ToString(); }
    internal static MonitorLayout Monitor(nint hwnd)
    {
        var info = new MonitorInfo { Size = Marshal.SizeOf<MonitorInfo>(), Device = "" };
        if (!GetMonitorInfo(MonitorFromWindow(hwnd, 2), ref info)) throw new InvalidOperationException("GetMonitorInfo failed");
        return new(info.Device, info.Monitor.Box, info.Work.Box, Math.Max(96u, GetDpiForWindow(hwnd)));
    }
    internal static string[] Arguments(string? line)
    {
        if (string.IsNullOrWhiteSpace(line)) return [];
        var ptr = CommandLineToArgvW(line, out var count);
        if (ptr == 0) return [];
        try { return Enumerable.Range(1, Math.Max(0, count - 1)).Select(i => Marshal.PtrToStringUni(Marshal.ReadIntPtr(ptr, i * nint.Size)) ?? "").ToArray(); }
        finally { LocalFree(ptr); }
    }
}

[ComImport, Guid("6D5140C1-7436-11CE-8034-00AA006009FA"), InterfaceType(ComInterfaceType.InterfaceIsIUnknown)]
internal interface IServiceProvider
{
    [PreserveSig] int QueryService(ref Guid service, ref Guid iid, out nint result);
}
// Only the inherited IOleWindow slots are needed to identify each tab's HWND.
[ComImport, Guid("000214E2-0000-0000-C000-000000000046"), InterfaceType(ComInterfaceType.InterfaceIsIUnknown)]
internal interface IShellBrowserWindow
{
    [PreserveSig] int GetWindow(out nint hwnd);
    [PreserveSig] int ContextSensitiveHelp([MarshalAs(UnmanagedType.Bool)] bool enter);
}

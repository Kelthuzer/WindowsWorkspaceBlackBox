using Microsoft.Win32;

namespace WindowsWorkspaceBlackBox;

internal static class Autostart
{
    internal static void Set(bool enabled)
    {
        using var key = Registry.CurrentUser.CreateSubKey(@"Software\Microsoft\Windows\CurrentVersion\Run");
        if (enabled) key.SetValue("WindowsWorkspaceBlackBox", $"\"{Environment.ProcessPath}\" --startup");
        else key.DeleteValue("WindowsWorkspaceBlackBox", false);
    }
}

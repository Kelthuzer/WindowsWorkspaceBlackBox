using System.Runtime.InteropServices;
using WindowsWorkspaceBlackBox.Native;
using WWBB.Core;

namespace WindowsWorkspaceBlackBox.Capture;

internal static class ExplorerCollector
{
    internal static bool IsExplorer(WindowEntry w) => w.ClassName is "CabinetWClass" or "ExploreWClass";

    internal static void Capture(Snapshot snapshot)
    {
        object? collection = null;
        try
        {
            collection = Activator.CreateInstance(Type.GetTypeFromCLSID(new("9BA05972-F6A8-11CF-A442-00A0C90A8F39"), true)!);
            dynamic shellWindows = collection!;
            int count = shellWindows.Count;
            for (int i = 0; i < count; i++)
            {
                object? item = null, document = null, folder = null, self = null;
                try
                {
                    item = shellWindows.Item(i);
                    if (item == null) continue;
                    dynamic browser = item;
                    nint hwnd = (nint)(long)browser.HWND;
                    var root = Win32.GetAncestor(hwnd, 2);
                    var window = snapshot.Windows.FirstOrDefault(w => IsExplorer(w) && (w.Hwnd == root || w.Hwnd == hwnd));
                    if (window == null) continue;
                    document = browser.Document;
                    folder = ((dynamic)document).Folder;
                    self = ((dynamic)folder).Self;
                    string path = ((dynamic)self).Path;
                    if (string.IsNullOrWhiteSpace(path)) throw new InvalidDataException("Shell returned an empty location");
                    long tab = TabHandle(item);
                    // Keep every registered tab; equal paths in distinct tabs/windows are meaningful.
                    if (!snapshot.Explorer.Any(e => e.Window.Hwnd == window.Hwnd && e.TabHwnd == tab && e.Path == path))
                        snapshot.Explorer.Add(new(path, tab, window));
                }
                catch (Exception e)
                {
                    snapshot.ExplorerComplete = false;
                    snapshot.Warnings.Add($"Explorer entry {i}: {e.Message}");
                }
                finally { Release(self); Release(folder); Release(document); Release(item); }
            }
            foreach (var window in snapshot.Windows.Where(IsExplorer))
            {
                var tabs = new HashSet<long>();
                Win32.EnumChildWindows((nint)window.Hwnd, (h, _) => { if (Win32.Class(h) == "ShellTabWindowClass") tabs.Add(h.ToInt64()); return true; }, 0);
                var found = snapshot.Explorer.Where(e => e.Window.Hwnd == window.Hwnd).ToList();
                if (found.Count == 0 || tabs.Count > found.Count)
                {
                    snapshot.ExplorerComplete = false;
                    snapshot.Warnings.Add($"Explorer coverage incomplete: HWND={window.Hwnd}, tab hosts={tabs.Count}, locations={found.Count}");
                }
            }
        }
        catch (Exception e) { snapshot.ExplorerComplete = false; snapshot.Warnings.Add($"Shell collection: {e.Message}"); }
        finally { Release(collection); }
    }

    private static long TabHandle(object item)
    {
        nint pointer = 0;
        object? browser = null;
        try
        {
            var service = new Guid("4C96BE40-915C-11CF-99D3-00AA004AE837");
            var iid = new Guid("000214E2-0000-0000-C000-000000000046");
            if (((Native.IServiceProvider)item).QueryService(ref service, ref iid, out pointer) < 0 || pointer == 0) return 0;
            browser = Marshal.GetObjectForIUnknown(pointer);
            return ((IShellBrowserWindow)browser).GetWindow(out var hwnd) >= 0 ? hwnd.ToInt64() : 0;
        }
        catch { return 0; }
        finally { Release(browser); if (pointer != 0) Marshal.Release(pointer); }
    }
    private static void Release(object? item) { if (item != null && Marshal.IsComObject(item)) Marshal.ReleaseComObject(item); }
}

using System.Diagnostics;
using System.Text.Json;
using WWBB.Core;

namespace WindowsWorkspaceBlackBox.Capture;

internal static class CaptureClient
{
    internal static async Task<Snapshot> CaptureAsync(bool fast, CancellationToken ct)
    {
        var start = new ProcessStartInfo(Environment.ProcessPath!)
        { UseShellExecute = false, RedirectStandardOutput = true, RedirectStandardError = true, CreateNoWindow = true };
        start.ArgumentList.Add("--capture");
        if (fast) start.ArgumentList.Add("--fast");
        using var process = Process.Start(start) ?? throw new IOException("Capture process did not start");
        using var deadline = CancellationTokenSource.CreateLinkedTokenSource(ct);
        deadline.CancelAfter(TimeSpan.FromSeconds(fast ? 12 : 60));
        var output = process.StandardOutput.ReadToEndAsync(deadline.Token);
        var error = process.StandardError.ReadToEndAsync(deadline.Token);
        try
        {
            await process.WaitForExitAsync(deadline.Token);
            if (process.ExitCode != 0) throw new IOException(await error);
            return JsonSerializer.Deserialize<Snapshot>(await output, Json.Options) ?? throw new InvalidDataException("Empty capture");
        }
        catch (OperationCanceledException) when (!ct.IsCancellationRequested) { throw new TimeoutException("Explorer capture timed out; previous snapshot retained."); }
        finally { if (!process.HasExited) process.Kill(true); }
    }
}

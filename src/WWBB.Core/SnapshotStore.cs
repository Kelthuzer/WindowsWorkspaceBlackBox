using System.Text.Json;

namespace WWBB.Core;

public sealed class SnapshotStore(string directory, Action<string>? log = null)
{
    private readonly SemaphoreSlim gate = new(1, 1);
    public string DirectoryPath => directory;

    public async Task<string> SaveAsync(Snapshot snapshot, CancellationToken ct = default)
    {
        if (!IsValid(snapshot)) throw new InvalidDataException("Incomplete or invalid snapshot.");
        await gate.WaitAsync(ct);
        try
        {
            Directory.CreateDirectory(directory);
            var stem = snapshot.CapturedAt.UtcDateTime.ToString("yyyyMMdd_HHmmss_fffffff");
            for (var suffix = 0; ; suffix++)
            {
                var path = Path.Combine(directory, $"{stem}_{suffix:D3}.json");
                FileStream stream;
                try { stream = new FileStream(path, FileMode.CreateNew, FileAccess.Write, FileShare.None, 4096, FileOptions.WriteThrough); }
                catch (IOException) when (File.Exists(path)) { continue; }
                await using (stream)
                {
                    await JsonSerializer.SerializeAsync(stream, snapshot, Json.Options, ct);
                    await stream.FlushAsync(ct);
                    stream.Flush(true);
                }
                return path;
            }
        }
        finally { gate.Release(); }
    }

    public IEnumerable<string> Files() => Directory.Exists(directory)
        ? Directory.EnumerateFiles(directory, "*.json").OrderByDescending(Path.GetFileName, StringComparer.Ordinal)
        : [];

    public Snapshot? Read(string path)
    {
        try
        {
            using var stream = File.OpenRead(path);
            var value = JsonSerializer.Deserialize<Snapshot>(stream, Json.Options);
            return value is not null && IsValid(value) ? value : null;
        }
        catch (Exception e) when (e is IOException or JsonException or UnauthorizedAccessException or NotSupportedException)
        { log?.Invoke($"Skip snapshot {Path.GetFileName(path)}: {e.Message}"); return null; }
    }

    public (string Path, Snapshot Value)? Latest()
    {
        // Payload time handles clock corrections; filenames only break equal-time ties.
        return Files().Select(p => (Path: p, Value: Read(p))).Where(x => x.Value != null)
            .OrderByDescending(x => x.Value!.CapturedAt).Select(x => ((string, Snapshot)?)(x.Path, x.Value!)).FirstOrDefault();
    }

    public async Task CleanupAsync(int keep, string? protectedFile = null)
    {
        await gate.WaitAsync();
        try
        {
            var ordered = Files().Select(p => (Path: p, Value: Read(p)))
                .OrderByDescending(x => x.Value?.CapturedAt ?? DateTimeOffset.MinValue).ToList();
            var retained = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
            if (protectedFile != null && ordered.Any(x => x.Path.Equals(protectedFile, StringComparison.OrdinalIgnoreCase))) retained.Add(protectedFile);
            foreach (var item in ordered)
            {
                if (retained.Count >= Math.Max(1, keep)) break;
                retained.Add(item.Path);
            }
            foreach (var item in ordered.Where(x => !retained.Contains(x.Path)))
                try { File.Delete(item.Path); }
                catch (Exception e) when (e is IOException or UnauthorizedAccessException) { log?.Invoke($"Cleanup: {e.Message}"); }
        }
        finally { gate.Release(); }
    }

    public static bool IsValid(Snapshot s) => s.SchemaVersion == 1 && s.ExplorerComplete
        && s.CapturedAt != default && s.Windows != null && s.Explorer != null
        && s.Windows.All(ValidWindow)
        && s.Explorer.All(e => e != null && !string.IsNullOrWhiteSpace(e.Path) && ValidWindow(e.Window));

    private static bool ValidWindow(WindowEntry? w) => w != null && w.NormalBounds is { Width: > 0, Height: > 0 }
        && w.Monitor?.WorkArea is { Width: > 0, Height: > 0 } && w.Monitor.Bounds is { Width: > 0, Height: > 0 }
        && w.ExePath != null && w.ProcessName != null && w.ClassName != null && w.Title != null && w.Arguments != null;
}

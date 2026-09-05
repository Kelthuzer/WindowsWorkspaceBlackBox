using WWBB.Core;
using Xunit;

namespace WWBB.Tests;

public sealed class RecoveryTests : IDisposable
{
    private readonly string root = Path.Combine(Path.GetTempPath(), "wwbb-tests-" + Guid.NewGuid().ToString("N"));
    public void Dispose() { if (Directory.Exists(root)) Directory.Delete(root, true); }

    [Fact]
    public async Task PowerLossSkipsTruncatedNewestSnapshot()
    {
        var store = new SnapshotStore(root);
        var good = await store.SaveAsync(new Snapshot());
        await File.WriteAllTextAsync(Path.Combine(root, "99999999_999999.json"), "{\"SchemaVersion\":1,");
        Assert.Equal(good, store.Latest()!.Value.Path);
    }
    [Fact]
    public async Task SameTimestampNeverOverwritesSnapshot()
    {
        var store = new SnapshotStore(root);
        var snapshot = new Snapshot();
        var files = await Task.WhenAll(Enumerable.Range(0, 20).Select(_ => store.SaveAsync(snapshot)));
        Assert.Equal(20, files.Distinct().Count());
        Assert.All(files, f => Assert.NotNull(store.Read(f)));
    }
    [Fact]
    public async Task CleanupRemovesEntireOverflowAndKeepsRecoverySource()
    {
        var store = new SnapshotStore(root);
        var date = DateTimeOffset.UtcNow.AddHours(-1);
        var oldest = await store.SaveAsync(new Snapshot { CapturedAt = date });
        for (int i = 1; i < 107; i++) await store.SaveAsync(new Snapshot { CapturedAt = date.AddSeconds(i) });
        await store.CleanupAsync(100, oldest);
        Assert.True(File.Exists(oldest));
        Assert.Equal(100, store.Files().Count());
    }
    [Fact]
    public async Task IncompleteExplorerNeverBecomesLatest()
    {
        var store = new SnapshotStore(root);
        var good = await store.SaveAsync(new Snapshot());
        await Assert.ThrowsAsync<InvalidDataException>(() => store.SaveAsync(new Snapshot { ExplorerComplete = false }));
        Assert.Equal(good, store.Latest()!.Value.Path);
    }
    [Fact]
    public async Task CorruptGeometryAndFutureSchemaAreRejected()
    {
        var store = new SnapshotStore(root);
        await Assert.ThrowsAsync<InvalidDataException>(() => store.SaveAsync(new Snapshot { SchemaVersion = 999 }));
        await Assert.ThrowsAsync<InvalidDataException>(() => store.SaveAsync(new Snapshot { Windows = [new() { NormalBounds = new(0,0,-1,20) }] }));
    }
    [Fact]
    public void ExactDocumentWinsAndHandlesCannotBeReused()
    {
        var saved = new WindowEntry { ExePath = @"C:\Editor\editor.exe", ClassName = "Editor", Title = "second.txt" };
        var first = saved with { Title = "first.txt", Hwnd = 10 };
        var second = saved with { Hwnd = 20 };
        Assert.Equal(20, Matching.Best(saved, [first, second], new HashSet<long>())!.Hwnd);
        Assert.Equal(10, Matching.Best(saved, [first, second], new HashSet<long> {20})!.Hwnd);
        Assert.Null(Matching.Best(saved, [first, second], new HashSet<long> {10,20}));
        Assert.True(Matching.Score(saved, second with { ExePath = @"C:\Other\editor.exe" }) < 0);
    }
    [Fact]
    public void MissingMonitorMapsWindowInsideRemainingWorkArea()
    {
        var saved = new WindowEntry { NormalBounds = new(-2500, 200, 1600, 900), Monitor = new("OLD", new(-2560,0,2560,1440), new(-2560,0,2560,1400), 144) };
        var target = new MonitorLayout("NEW", new(0,0,1280,720), new(0,40,1280,680), 96);
        var placed = Matching.Place(saved, target);
        Assert.InRange(placed.X, 0, 1279); Assert.InRange(placed.Y, 40, 719);
        Assert.True(placed.X + placed.Width <= 1280); Assert.True(placed.Y + placed.Height <= 720);
    }
    [Theory]
    [InlineData(@"\\server\Share\Folder\", @"\\SERVER\share\folder")]
    [InlineData(@"C:\", @"c:\")]
    public void FolderMatchingHandlesUncAndCase(string a, string b) => Assert.True(Matching.SamePath(a,b));

    [Fact]
    public void RestoreDropsBackgroundFlagsButKeepsNormalArguments()
    {
        var result = Matching.SafeLaunchArguments(["--hidden", "--updated", "/min", "document.txt"]).ToArray();
        Assert.Equal(["--updated", "document.txt"], result);
    }
}

using System.Collections.Concurrent;
using System.IO;
using NexusExplorer.Services;

namespace NexusExplorer.Tests;
[Collection("NativePlaybackLogging")]
public class ThumbnailCacheTests
{
    [Fact]
    public async Task RestartAndRenameReuseDiskButChangesAndCorruptionRegenerate()
    {
        using var host = new TestHost(); var path = host.CreateTestFile("a.bin");
        var cache = Path.Combine(host.RootDir, "cache"); var calls = 0;
        using (var first = new ThumbnailService(_ => { calls++; return ThumbnailTests.Bitmap(); }, diskDirectory: cache))
            await first.GetAsync(path, identity: "file:1:created");
        var moved = Path.Combine(host.RootDir, "renamed.bin"); File.Move(path, moved);
        using (var second = new ThumbnailService(_ => { calls++; return ThumbnailTests.Bitmap(); }, diskDirectory: cache))
        {
            var result = await second.GetAsync(moved, identity: "file:1:created");
            Assert.Equal(1, calls); Assert.Equal(10, result.Image!.PixelWidth);
            File.AppendAllText(moved, "changed");
            await second.GetAsync(moved, identity: "file:1:created"); Assert.Equal(2, calls);
        }
        foreach (var png in Directory.GetFiles(cache, "*.png")) File.WriteAllText(png, "broken");
        using var third = new ThumbnailService(_ => { calls++; return ThumbnailTests.Bitmap(); }, diskDirectory: cache);
        Assert.Equal(ThumbnailStatus.Ready, (await third.GetAsync(moved, identity: "file:1:created")).Status);
        Assert.Equal(3, calls);
        File.Delete(moved); Assert.Equal(ThumbnailStatus.Missing, (await third.GetAsync(moved, identity: "file:1:created")).Status);
    }

    [Fact]
    public async Task MemoryAndDiskHitsBypassBlockedDecoder()
    {
        using var host = new TestHost(); var cached = host.CreateTestFile("cached.bin"); var slow = host.CreateTestFile("slow.bin");
        var cache = Path.Combine(host.RootDir, "cache");
        using (var seed = new ThumbnailService(_ => ThumbnailTests.Bitmap(), diskDirectory: cache)) await seed.GetAsync(cached);
        using var entered = new ManualResetEventSlim(); using var release = new ManualResetEventSlim(); var calls = 0;
        using var service = new ThumbnailService(_ => { Interlocked.Increment(ref calls); entered.Set(); release.Wait(TimeSpan.FromSeconds(10)); return ThumbnailTests.Bitmap(); }, diskDirectory: cache);
        var blocked = service.GetAsync(slow); Assert.True(entered.Wait(TimeSpan.FromSeconds(3)));
        try
        {
            var disk = await service.GetAsync(cached).WaitAsync(TimeSpan.FromSeconds(2));
            var memory = await service.GetAsync(cached).WaitAsync(TimeSpan.FromSeconds(2));
            Assert.Same(disk, memory); Assert.Equal(1, calls); Assert.False(blocked.IsCompleted);
        }
        finally { release.Set(); await blocked; }
    }

    [Fact]
    public async Task SharedRequestsKeepIndependentCancellationAndHoverOvertakesQueuedRows()
    {
        using var host = new TestHost(); var a = host.CreateTestFile("a.bin"); var b = host.CreateTestFile("b.bin"); var c = host.CreateTestFile("c.bin");
        using var entered = new ManualResetEventSlim(); using var release = new ManualResetEventSlim(); var order = new ConcurrentQueue<string>();
        using var service = new ThumbnailService(path => { order.Enqueue(path); if (path == a) { entered.Set(); release.Wait(TimeSpan.FromSeconds(10)); } return ThumbnailTests.Bitmap(); });
        var active = service.GetAsync(a); Assert.True(entered.Wait(TimeSpan.FromSeconds(3)));
        using var cancel = new CancellationTokenSource();
        var row = service.GetAsync(b, cancel.Token); var otherRow = service.GetAsync(c);
        try
        {
            await WaitUntil(() => service.PendingCount == 2);
            var hover = service.GetAsync(b, priority: ThumbnailPriority.Hover);
            // Give the independent metadata check time to join/promote the existing job.
            await Task.Delay(100); cancel.Cancel();
            await Assert.ThrowsAnyAsync<OperationCanceledException>(() => row);
            release.Set(); await Task.WhenAll(active, hover, otherRow);
            Assert.Equal(new[] { a, b, c }, order.ToArray());
        }
        finally { release.Set(); await active; }
    }

    [Fact]
    public async Task DiskBudgetEvictsOldEntriesAndReadOnlyCacheFallsBack()
    {
        using var host = new TestHost(); var a = host.CreateTestFile("a.bin"); var b = host.CreateTestFile("b.bin");
        var cache = Path.Combine(host.RootDir, "cache");
        using (var seed = new ThumbnailService(_ => ThumbnailTests.Bitmap(), diskDirectory: cache)) await seed.GetAsync(a);
        var budget = new FileInfo(Directory.GetFiles(cache, "*.png").Single()).Length + 1;
        using (var service = new ThumbnailService(_ => ThumbnailTests.Bitmap(), diskDirectory: cache, diskBudget: budget))
            await service.GetAsync(b);
        Assert.Single(Directory.GetFiles(cache, "*.png"));
        Assert.True(Directory.GetFiles(cache).Sum(p => new FileInfo(p).Length) <= budget);
        using var fallback = new ThumbnailService(_ => ThumbnailTests.Bitmap(), diskDirectory: a); // A file cannot be a cache directory.
        Assert.Equal(ThumbnailStatus.Ready, (await fallback.GetAsync(b)).Status);
    }

    private static async Task WaitUntil(Func<bool> predicate)
    {
        var end = DateTime.UtcNow.AddSeconds(3);
        while (!predicate()) { Assert.True(DateTime.UtcNow < end); await Task.Delay(10); }
    }

    [Fact]
    public async Task FailedExtractionIsRememberedAcrossRestartAndRetriedForChangedFile()
    {
        using var host = new TestHost(); var path = host.CreateTestFile("bad.bin");
        var cache = Path.Combine(host.RootDir, "cache"); var calls = 0;
        using (var service = new ThumbnailService(_ => { calls++; return null; }, diskDirectory: cache))
        {
            Assert.Equal(ThumbnailStatus.Unavailable, (await service.GetAsync(path)).Status);
            await service.GetAsync(path); Assert.Equal(1, calls);
        }
        using var restarted = new ThumbnailService(_ => { calls++; return ThumbnailTests.Bitmap(); }, diskDirectory: cache);
        Assert.Equal(ThumbnailStatus.Unavailable, (await restarted.GetAsync(path)).Status);
        Assert.Equal(1, calls);
        File.AppendAllText(path, "new version");
        Assert.Equal(ThumbnailStatus.Ready, (await restarted.GetAsync(path)).Status); Assert.Equal(2, calls);
    }
}

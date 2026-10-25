using System.Collections.Concurrent;
using System.IO;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using NexusExplorer.Services;
using SixLabors.ImageSharp;
using SixLabors.ImageSharp.PixelFormats;
using SixLabors.ImageSharp.Processing;

namespace NexusExplorer.Tests;

[Collection("NativePlaybackLogging")]
public class ThumbnailTests
{
    [Fact]
    public async Task CacheReusesImagesInvalidatesChangedFilesAndEvictsLeastRecentlyUsed()
    {
        using var host = new TestHost(); var a = host.CreateTestFile("a.bin"); var b = host.CreateTestFile("b.bin"); var c = host.CreateTestFile("c.bin");
        var calls = new ConcurrentDictionary<string, int>();
        using var service = new ThumbnailService(path => { calls.AddOrUpdate(path, 1, (_, n) => n + 1); return Bitmap(); }, cacheBudget: 800);
        var first = await service.GetAsync(a); Assert.True(first.Image!.IsFrozen);
        Assert.Same(first, await service.GetAsync(a));
        await service.GetAsync(b); await service.GetAsync(a); await service.GetAsync(c);
        Assert.Equal(800, service.CacheBytes); Assert.Equal(1, calls[a]);
        await service.GetAsync(b); Assert.Equal(2, calls[b]);
        File.AppendAllText(b, "changed"); await service.GetAsync(b); Assert.Equal(3, calls[b]);
        File.Delete(b); Assert.Equal(ThumbnailStatus.Missing, (await service.GetAsync(b)).Status);
        Assert.InRange(service.CacheBytes, 0, 800);
    }

    [Fact]
    public async Task QueueIsSerialAndCancelsWaitingRequestsWithoutStartingTheirDecoder()
    {
        using var host = new TestHost(); var a = host.CreateTestFile("a.bin"); var b = host.CreateTestFile("b.bin"); var c = host.CreateTestFile("c.bin");
        using var entered = new ManualResetEventSlim(); using var release = new ManualResetEventSlim();
        var paths = new ConcurrentQueue<string>(); var active = 0; var maximum = 0;
        using var service = new ThumbnailService(path =>
        {
            var now = Interlocked.Increment(ref active); maximum = Math.Max(maximum, now); paths.Enqueue(path);
            if (path == a) { entered.Set(); Assert.True(release.Wait(TimeSpan.FromSeconds(5))); }
            Interlocked.Decrement(ref active); return Bitmap();
        });
        var first = service.GetAsync(a); Assert.True(entered.Wait(TimeSpan.FromSeconds(5)));
        using var cancel = new CancellationTokenSource(); var waiting = service.GetAsync(b, cancel.Token); cancel.Cancel();
        try { await Assert.ThrowsAnyAsync<OperationCanceledException>(async () => await waiting.WaitAsync(TimeSpan.FromSeconds(2))); }
        finally { release.Set(); }
        await first; await service.GetAsync(c);
        Assert.Equal(1, maximum); Assert.DoesNotContain(b, paths); Assert.Equal(new[] { a, c }, paths.ToArray());
    }

    [Fact]
    public async Task ImageThumbnailIsSmallCorrectlyColoredAndReleasesSourceFile()
    {
        using var host = new TestHost(); var path = Path.Combine(host.RootDir, "large.png");
        using (var image = new SixLabors.ImageSharp.Image<Rgba32>(1200, 600, new Rgba32(240, 10, 20))) image.SaveAsPng(path);
        using var service = new ThumbnailService(); var result = await service.GetAsync(path);
        Assert.Equal(ThumbnailStatus.Ready, result.Status); Assert.Equal(320, result.Image!.PixelWidth); Assert.Equal(160, result.Image.PixelHeight);
        var pixel = new byte[4]; result.Image.CopyPixels(new System.Windows.Int32Rect(0, 0, 1, 1), pixel, 4, 0);
        Assert.Equal(new byte[] { 20, 10, 240, 255 }, pixel);
        using var exclusive = File.Open(path, FileMode.Open, FileAccess.ReadWrite, FileShare.None);
    }

    [Theory]
    [InlineData("Gif/loop.gif")]
    [InlineData("Apng/finite.apng")]
    public async Task AnimatedImagesProduceOnlyAStaticFirstFrame(string asset)
    {
        var path = Path.Combine(AppContext.BaseDirectory, "Assets", asset); Assert.True(File.Exists(path), asset);
        using var service = new ThumbnailService(); var result = await service.GetAsync(path);
        Assert.Equal(ThumbnailStatus.Ready, result.Status); Assert.True(result.Image!.IsFrozen);
        var original = new byte[result.Image.PixelWidth * result.Image.PixelHeight * 4]; result.Image.CopyPixels(original, result.Image.PixelWidth * 4, 0);
        await Task.Delay(180);
        var later = new byte[original.Length]; result.Image.CopyPixels(later, result.Image.PixelWidth * 4, 0); Assert.Equal(original, later);
        using var decoded = SixLabors.ImageSharp.Image.Load<Rgba32>(new SixLabors.ImageSharp.Formats.DecoderOptions { MaxFrames = 1 }, path);
        var expected = decoded[0, 0]; Assert.Equal(expected.B, original[0]); Assert.Equal(expected.G, original[1]); Assert.Equal(expected.R, original[2]);
    }

    [Fact]
    public async Task WindowsVideoThumbnailReturnsImageOrCleanFallbackWithoutUsingPlayer()
    {
        using var host = new TestHost(); var path = Path.Combine(host.RootDir, "sample.mp4");
        var asset = Path.Combine(AppContext.BaseDirectory, "Assets", "seek-h264-aac.mp4"); Assert.True(File.Exists(asset)); File.Copy(asset, path);
        using var service = new ThumbnailService(); var result = await service.GetAsync(path).WaitAsync(TimeSpan.FromSeconds(20));
        if (result.Image is { } bitmap)
        { Assert.Equal(ThumbnailStatus.Ready, result.Status); Assert.True(bitmap.IsFrozen); Assert.InRange(Math.Max(bitmap.PixelWidth, bitmap.PixelHeight), 1, 320); }
        else Assert.Equal(ThumbnailStatus.Unavailable, result.Status);
        var report = Path.GetFullPath("../../../../../artifacts/hover-preview", AppContext.BaseDirectory); Directory.CreateDirectory(report);
        File.WriteAllText(Path.Combine(report, "windows-video-result.txt"), $"status={result.Status}; size={result.Image?.PixelWidth}x{result.Image?.PixelHeight}");
        if (result.Image is { } frame)
        {
            var encoder = new PngBitmapEncoder(); encoder.Frames.Add(BitmapFrame.Create(frame));
            using var output = File.Create(Path.Combine(report, "windows-video.png")); encoder.Save(output);
        }
        using var exclusive = File.Open(path, FileMode.Open, FileAccess.ReadWrite, FileShare.None);
    }

    internal static BitmapSource Bitmap()
    {
        var pixels = new byte[10 * 10 * 4]; for (var i = 0; i < pixels.Length; i += 4) { pixels[i + 2] = 255; pixels[i + 3] = 255; }
        var bitmap = BitmapSource.Create(10, 10, 96, 96, PixelFormats.Bgra32, null, pixels, 40); bitmap.Freeze(); return bitmap;
    }
}

using System.Diagnostics;
using System.IO;
using System.Windows.Controls;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using System.Windows.Threading;
using NexusExplorer.Models;
using NexusExplorer.Services;
using NexusExplorer.ViewModels;
using NexusExplorer.Views;

namespace NexusExplorer.Tests;

public class ApngAnimationTests
{
    private static string Asset(string name)
    {
        var path = Path.Combine(AppContext.BaseDirectory, "Assets", "Apng", name);
        Assert.True(File.Exists(path), $"APNG 素材缺失：{path}"); return path;
    }
    private static byte[] Pixels(BitmapSource source)
    {
        var converted = new FormatConvertedBitmap(source, PixelFormats.Bgra32, null, 0);
        var pixels = new byte[converted.PixelWidth * converted.PixelHeight * 4];
        converted.CopyPixels(pixels, converted.PixelWidth * 4, 0); return pixels;
    }
    private static async Task WaitUntil(Func<bool> predicate)
    {
        var deadline = DateTime.UtcNow.AddSeconds(4);
        while (!predicate() && DateTime.UtcNow < deadline) await Task.Delay(10);
        Assert.True(predicate(), "APNG 未到达预期帧或状态。");
    }
    [Theory]
    [InlineData(".apng")]
    [InlineData(".PNG")]
    public async Task ActualPreviewAnimatesApngAndPngWithoutKeepingFileOpen(string extension)
    {
        using var host = new TestHost();
        var path = Path.Combine(host.RootDir, "透明动画" + extension); File.Copy(Asset("loop.png"), path);
        await WpfTestHost.RunAsync(async () =>
        {
            var main = new MainViewModel(host.Categories, host.Files, host.Organization, new FakePlaybackEngine());
            var panel = new PlayerPanel(); panel.Initialize(main);
            try
            {
                await main.Player.PlayFileAsync(new FileItem { FileName = Path.GetFileName(path), AbsolutePath = path });
                Assert.Equal(MediaKind.Image, main.Player.Kind);
                var display = (Image)panel.FindName("ImageDisplay");
                var bitmap = Assert.IsType<WriteableBitmap>(display.Source);
                using (File.Open(path, FileMode.Open, FileAccess.ReadWrite, FileShare.None)) { }
                await WaitUntil(() => Pixels(bitmap)[1] == 255);
                await WaitUntil(() => Pixels(bitmap)[0] == 255);
                await WaitUntil(() => Pixels(bitmap)[2] == 255);
                main.Player.ImageScale = 1.5; main.Player.ResetZoom();
                Assert.Same(bitmap, display.Source);
                await main.Player.PlayFileAsync(new FileItem { FileName = "static.png", AbsolutePath = Asset("static.png") });
                var last = Pixels(bitmap);
                Assert.True(main.Player.ImageSource!.IsFrozen);
                await Task.Delay(220); Assert.Equal(last, Pixels(bitmap));
                await main.Player.PlayFileAsync(new FileItem { FileName = Path.GetFileName(path), AbsolutePath = path });
                var stopped = (BitmapSource)main.Player.ImageSource!;
                panel.Detach(); last = Pixels(stopped);
                await Task.Delay(220); Assert.Equal(last, Pixels(stopped));
                Assert.Null(main.Player.ImageSource);
            }
            finally { panel.Detach(); }
        });
    }
    [Theory]
    [InlineData("disposal.apng", 4)]
    [InlineData("blend.apng", 2)]
    public async Task CompositedFramesMatchIndependentReferences(string name, int count)
    {
        await WpfTestHost.RunAsync(async () =>
        {
            var decoded = await ImageAnimation.TryDecodePngAsync(Asset(name), CancellationToken.None);
            Assert.NotNull(decoded);
            using var animation = new ImageAnimation(decoded, Dispatcher.CurrentDispatcher);
            Assert.Equal(count, animation.FrameCount); animation.Start();
            for (var index = 0; index < count; index++)
            {
                await WaitUntil(() => animation.FrameIndex == index);
                var reference = name == "blend.apng" ? index == 0 ? "loop.png" : "blend-reference.png" : $"disposal-{index}.png";
                using var stream = File.OpenRead(Asset(reference));
                var expected = name == "blend.apng" && index == 0
                    ? Enumerable.Range(0, 64).SelectMany(_ => new byte[] { 255, 0, 0, 255 }).ToArray()
                    : Pixels(new PngBitmapDecoder(stream, BitmapCreateOptions.None, BitmapCacheOption.OnLoad).Frames[0]);
                var actual = Pixels(animation.Bitmap);
                for (var pixel = 0; pixel < expected.Length; pixel += 4)
                {
                    Assert.Equal(expected[pixel + 3], actual[pixel + 3]);
                    if (expected[pixel + 3] == 0) continue;
                    for (var channel = 0; channel < 3; channel++)
                        Assert.InRange(Math.Abs(expected[pixel + channel] - actual[pixel + channel]), 0, 1);
                }
            }
        });
    }
    [Fact]
    public async Task SeparatePosterIsExcludedAndFiniteLoopsHonorTiming()
    {
        await WpfTestHost.RunAsync(async () =>
        {
            var decoded = await ImageAnimation.TryDecodePngAsync(Asset("poster.apng"), CancellationToken.None);
            Assert.NotNull(decoded);
            using (var animation = new ImageAnimation(decoded, Dispatcher.CurrentDispatcher))
            {
                Assert.Equal(2, animation.FrameCount);
                Assert.Equal(new byte[] { 0, 0, 255, 255 }, Pixels(animation.Bitmap)[..4]);
                animation.Start(); await WaitUntil(() => animation.FrameIndex == 1);
                Assert.Equal(new byte[] { 0, 255, 0, 255 }, Pixels(animation.Bitmap)[..4]);
            }
            decoded = await ImageAnimation.TryDecodePngAsync(Asset("finite.apng"), CancellationToken.None);
            Assert.NotNull(decoded);
            using var finite = new ImageAnimation(decoded, Dispatcher.CurrentDispatcher);
            Assert.Equal(TimeSpan.FromMilliseconds(60), finite.FrameDelay(0));
            Assert.Equal(TimeSpan.FromMilliseconds(90), finite.FrameDelay(1));
            var elapsed = Stopwatch.StartNew(); finite.Start();
            await WaitUntil(() => !finite.IsRunning);
            Assert.True(elapsed.ElapsedMilliseconds >= 290); Assert.Equal(1, finite.FrameIndex);
        });
    }
    [Fact]
    public async Task StaticPngStaysStaticAndOversizedApngStopsBeforeDecode()
    {
        Assert.Null(await ImageAnimation.TryDecodePngAsync(Asset("static.png"), CancellationToken.None));
        using var host = new TestHost(); var path = Path.Combine(host.RootDir, "huge.apng");
        var bytes = await File.ReadAllBytesAsync(Asset("loop.png"));
        // Only header inspection happens before the guard; CRC/pixels must not be decoded here.
        System.Buffers.Binary.BinaryPrimitives.WriteUInt32BigEndian(bytes.AsSpan(16), 32768);
        System.Buffers.Binary.BinaryPrimitives.WriteUInt32BigEndian(bytes.AsSpan(20), 32768);
        await File.WriteAllBytesAsync(path, bytes);
        var error = await Assert.ThrowsAsync<OperationException>(() => ImageAnimation.TryDecodePngAsync(path, CancellationToken.None));
        Assert.Contains("过多内存", error.Message);
        using (File.Open(path, FileMode.Open, FileAccess.ReadWrite, FileShare.None)) { }
    }
}

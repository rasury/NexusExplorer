using System.IO;
using System.Diagnostics;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using System.Windows.Threading;
using NexusExplorer.Models;
using NexusExplorer.Services;
using NexusExplorer.ViewModels;
using NexusExplorer.Views;

namespace NexusExplorer.Tests;

public class GifAnimationTests
{
    private static string Asset(string name)
    {
        var path = Path.Combine(AppContext.BaseDirectory, "Assets", "Gif", name);
        Assert.True(File.Exists(path), $"GIF 素材缺失：{path}"); return path;
    }
    private static FileItem FileAt(string path) => new() { FileName = Path.GetFileName(path), AbsolutePath = path };
    private static byte[] Pixels(BitmapSource source)
    {
        var converted = new FormatConvertedBitmap(source, PixelFormats.Bgra32, null, 0);
        var result = new byte[converted.PixelWidth * converted.PixelHeight * 4];
        converted.CopyPixels(result, converted.PixelWidth * 4, 0); return result;
    }
    private static async Task WaitUntil(Func<bool> predicate)
    {
        var deadline = DateTime.UtcNow.AddSeconds(4);
        while (DateTime.UtcNow < deadline) { if (predicate()) return; await Task.Delay(10); }
        Assert.True(predicate(), "GIF 未到达预期帧或状态。");
    }

    [Fact]
    public async Task ActualPreviewAnimatesSameBitmapWithoutResettingZoomAndReleasesFile()
    {
        using var host = new TestHost();
        var path = Path.Combine(host.RootDir, "测试动画.GIF"); File.Copy(Asset("loop.gif"), path);
        await WpfTestHost.RunAsync(async () =>
        {
            var main = new MainViewModel(host.Categories, host.Files, host.Organization, new FakePlaybackEngine());
            var panel = new PlayerPanel(); panel.Initialize(main);
            try
            {
                await main.Player.PlayFileAsync(FileAt(path));
                var display = (Image)panel.FindName("ImageDisplay");
                var bitmap = Assert.IsType<WriteableBitmap>(display.Source);
                Assert.Equal(new byte[] { 0, 0, 255, 255 }, Pixels(bitmap)[..4]);
                using (File.Open(path, FileMode.Open, FileAccess.ReadWrite, FileShare.None)) { }
                var buttons = (Panel)panel.FindName("ImageButtonsRow");
                buttons.Children.OfType<Button>().Single(b => Equals(b.Content, "＋")).RaiseEvent(new RoutedEventArgs(Button.ClickEvent));
                var zoom = main.Player.ImageScale; var width = display.Width;
                await WaitUntil(() => Pixels(bitmap)[1] == 255); // green second frame
                await WaitUntil(() => Pixels(bitmap)[0] == 255); // blue third frame
                await WaitUntil(() => Pixels(bitmap)[2] == 255); // repeats to red
                Assert.Same(bitmap, display.Source);
                Assert.Equal(zoom, main.Player.ImageScale); Assert.Equal(width, display.Width);
            }
            finally { panel.Detach(); }
        });
    }

    [Theory]
    [InlineData("finite.gif", 2)]
    [InlineData("single.gif", 1)]
    public async Task FiniteAndSingleFrameGifsStopAtFinalFrame(string name, int frameCount)
    {
        await WpfTestHost.RunAsync(async () =>
        {
            var decoded = await GifAnimation.DecodeAsync(Asset(name), CancellationToken.None);
            using var animation = new GifAnimation(decoded, Dispatcher.CurrentDispatcher);
            Assert.Equal(frameCount, animation.FrameCount);
            if (frameCount == 2)
            {
                Assert.Equal(TimeSpan.FromMilliseconds(50), animation.FrameDelay(0));
                Assert.Equal(TimeSpan.FromMilliseconds(80), animation.FrameDelay(1));
            }
            var elapsed = Stopwatch.StartNew();
            animation.Start();
            await WaitUntil(() => !animation.IsRunning);
            if (frameCount == 2) Assert.True(elapsed.ElapsedMilliseconds >= 250, "两轮动画不应在第一轮结束时停止。");
            Assert.Equal(frameCount - 1, animation.FrameIndex);
            var final = Pixels(animation.Bitmap); await Task.Delay(180);
            Assert.Equal(final, Pixels(animation.Bitmap));
        });
    }

    [Fact]
    public async Task OversizedCanvasIsRejectedBeforePixelDecode()
    {
        using var host = new TestHost();
        var path = Path.Combine(host.RootDir, "huge.gif");
        var bytes = await File.ReadAllBytesAsync(Asset("loop.gif"));
        // A tiny encoded file can declare a huge logical screen. Do not allocate its frame canvases.
        bytes[6] = bytes[8] = 0; bytes[7] = bytes[9] = 128;
        await File.WriteAllBytesAsync(path, bytes);
        var error = await Assert.ThrowsAsync<OperationException>(() => GifAnimation.DecodeAsync(path, CancellationToken.None));
        Assert.Contains("过多内存", error.Message);
        using (File.Open(path, FileMode.Open, FileAccess.ReadWrite, FileShare.None)) { }
    }

    [Fact]
    public async Task TransparentPartialFramesMatchIndependentDisposalReferences()
    {
        await WpfTestHost.RunAsync(async () =>
        {
            var decoded = await GifAnimation.DecodeAsync(Asset("disposal.gif"), CancellationToken.None);
            using var animation = new GifAnimation(decoded, Dispatcher.CurrentDispatcher);
            Assert.Equal(4, animation.FrameCount);
            animation.Start();
            for (var index = 0; index < 4; index++)
            {
                await WaitUntil(() => animation.FrameIndex == index);
                using var stream = File.OpenRead(Asset($"disposal-{index}.png"));
                var reference = new PngBitmapDecoder(stream, BitmapCreateOptions.None, BitmapCacheOption.OnLoad).Frames[0];
                var expected = Pixels(reference); var actual = Pixels(animation.Bitmap);
                for (var pixel = 0; pixel < actual.Length; pixel += 4)
                {
                    Assert.True(expected[pixel + 3] == actual[pixel + 3],
                        $"帧 {index} 像素 {pixel / 4}：期望 BGRA {string.Join(',', expected[pixel..(pixel + 4)])}，实际 {string.Join(',', actual[pixel..(pixel + 4)])}");
                    if (expected[pixel + 3] > 0)
                        Assert.Equal(expected[pixel..(pixel + 3)], actual[pixel..(pixel + 3)]);
                }
            }
        });
    }

    [Fact]
    public async Task SwitchingStoppingAndDetachingStopOldAnimationUpdates()
    {
        using var host = new TestHost();
        await WpfTestHost.RunAsync(async () =>
        {
            var main = new MainViewModel(host.Categories, host.Files, host.Organization, new FakePlaybackEngine());
            var panel = new PlayerPanel(); panel.Initialize(main);
            try
            {
                await main.Player.PlayFileAsync(FileAt(Asset("loop.gif")));
                var old = (BitmapSource)main.Player.ImageSource!;
                await main.Player.PlayFileAsync(FileAt(Asset("disposal-0.png")));
                var stopped = Pixels(old); var staticSource = main.Player.ImageSource;
                await Task.Delay(220);
                Assert.Equal(stopped, Pixels(old)); Assert.Same(staticSource, main.Player.ImageSource);
                Assert.True(staticSource!.IsFrozen);
                await main.Player.PlayFileAsync(FileAt(Asset("loop.gif")));
                await main.Player.StopPlaybackAsync();
                Assert.Null(main.Player.ImageSource);
                await main.Player.PlayFileAsync(FileAt(Asset("loop.gif")));
                var detached = (BitmapSource)main.Player.ImageSource!;
                panel.Detach(); var last = Pixels(detached);
                await Task.Delay(220);
                Assert.Null(main.Player.ImageSource);
                Assert.Null(((Image)panel.FindName("ImageDisplay")).Source);
                Assert.Equal(last, Pixels(detached));
            }
            finally { panel.Detach(); }
        });
    }

    [Fact]
    public async Task RapidSwitchDuringGifDecodeCannotRestoreOldPreview()
    {
        using var host = new TestHost();
        await WpfTestHost.RunAsync(async () =>
        {
            var main = new MainViewModel(host.Categories, host.Files, host.Organization, new FakePlaybackEngine());
            try
            {
                for (var i = 0; i < 5; i++)
                {
                    var previous = main.Player.PlayFileAsync(FileAt(Asset("loop.gif")));
                    var latest = main.Player.PlayFileAsync(FileAt(Asset("disposal-0.png")));
                    await Task.WhenAll(previous, latest);
                    Assert.IsNotType<WriteableBitmap>(main.Player.ImageSource);
                    Assert.EndsWith("disposal-0.png", main.Player.MediaPath);
                }
                var source = main.Player.ImageSource; await Task.Delay(220);
                Assert.Same(source, main.Player.ImageSource);
            }
            finally { main.Player.ReleaseImagePreview(); }
        });
    }
}

using System.IO;
using System.Windows;
using System.Windows.Media.Imaging;
using System.Windows.Threading;
using NexusExplorer.Services;

namespace NexusExplorer.Tests;

/// <summary>
/// 端到端视频播放冒烟测试:真实 LibVLC 播放真实视频,
/// 验证视频回调把帧像素实际写入 WriteableBitmap(黑屏回归测试)。
/// 需要 ffmpeg 生成 testmedia/test-video.mp4;缺失时跳过。
/// </summary>
public class VideoPlaybackSmokeTests
{
    private static readonly string? TestVideo = FindTestVideo();

    private static string? FindTestVideo()
    {
        var dir = new DirectoryInfo(Directory.GetCurrentDirectory());
        while (dir is not null)
        {
            var candidate = Path.Combine(dir.FullName, "testmedia", "test-video.mp4");
            if (File.Exists(candidate))
                return candidate;
            dir = dir.Parent;
        }
        return null;
    }

    [Fact]
    public void PlayVideo_WritesPixelsIntoBitmap()
    {
        if (TestVideo is null)
            return;

        var observedNonBlack = false;
        Exception? threadError = null;

        var thread = new Thread(() =>
        {
            try
            {
                var frame = new DispatcherFrame();
                // 显式指定本 STA 线程的 Dispatcher(全套并行跑时
                // Application.Current 可能是别的测试线程的,消息没有泵)
                using var service = new MediaPlayerService(Dispatcher.CurrentDispatcher);

                // 每秒采样中心像素(帧写入不触发事件,需主动轮询)
                var sampler = new DispatcherTimer(TimeSpan.FromMilliseconds(500), DispatcherPriority.Background,
                    (_, _) =>
                    {
                        var bitmap = service.VideoBitmap;
                        if (bitmap is null || bitmap.PixelWidth == 0)
                            return;

                        var pixel = new byte[4];
                        bitmap.CopyPixels(new Int32Rect(bitmap.PixelWidth / 2, bitmap.PixelHeight / 2, 1, 1), pixel, 4, 0);
                        if (pixel[0] > 16 || pixel[1] > 16 || pixel[2] > 16)
                            observedNonBlack = true;
                    },
                    Dispatcher.CurrentDispatcher);

                // 后台线程驱动播放(VLC Lazy 初始化可能阻塞,不能占 UI 线程消息泵)
                _ = Task.Run(() =>
                {
                    try
                    {
                        service.Play(new[] { TestVideo! });
                    }
                    catch (Exception ex)
                    {
                        threadError = ex;
                    }
                });

                sampler.Start();
                // 15 秒后退出消息泵(期间 UI 线程 pump 所有 BeginInvoke 写帧任务)
                var timer = new DispatcherTimer(TimeSpan.FromSeconds(15), DispatcherPriority.Background,
                    (_, _) => { sampler.Stop(); frame.Continue = false; },
                    Dispatcher.CurrentDispatcher);
                timer.Start();

                Dispatcher.PushFrame(frame);
                service.Stop();
                Console.WriteLine($"[smoke] format={service.FormatCallbackCount} display={service.DisplayCallbackCount} nonBlack={observedNonBlack}");
            }
            catch (Exception ex)
            {
                threadError = ex;
            }
        });
        thread.SetApartmentState(ApartmentState.STA);
        thread.Start();
        thread.Join(35000);

        Assert.Null(threadError);
        Assert.True(observedNonBlack, "15 秒内未观察到非黑像素(黑屏回归)");
    }
}

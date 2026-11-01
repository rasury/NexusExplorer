using System.IO;
using System.Runtime.InteropServices;
using System.Windows;
using System.Windows.Threading;
using NexusExplorer.Infrastructure.Playback.Mpv;
using NexusExplorer.ViewModels;
using NexusExplorer.Views;

namespace NexusExplorer.Tests;

[Collection("NativePlaybackLogging")]
public class VideoSurfaceLifetimeTests
{
    [Fact]
    public async Task StopHidesNativeHostPauseKeepsItAndReplayRestoresSameHandle()
    {
        using var data = new TestHost();
        var category = await data.Categories.CreateAsync("Surface lifecycle", null);
        var file = await data.Files.AddAsync(SyntheticMedia.WriteAvi(Path.Combine(data.RootDir, "sample.avi")), category.Id);
        await WpfTestHost.RunAsync(async () =>
        {
            using var engine = new MpvPlaybackEngine(Dispatcher.CurrentDispatcher);
            var main = new MainViewModel(data.Categories, data.Files, data.Organization, engine);
            var window = new MainWindow(main, data.Categories, data.Files, data.Organization, data.RecycleBin, engine)
                { ShowInTaskbar = false, ShowActivated = false };
            try
            {
                window.Show(); await main.SelectCategoryAsync(category);
                var panel = (PlayerPanel)window.FindName("PlayerArea");
                var video = (MpvVideoHost)panel.FindName("VideoView");
                await Dispatcher.Yield(DispatcherPriority.ApplicationIdle);
                var originalHandle = video.Handle;
                Assert.False(IsWindowVisible(originalHandle));
                await main.SelectFileAsync(file);
                await BoundedDialogTests.Until(() => engine.Snapshot.Position.TotalMilliseconds > 100);
                Assert.True(IsWindowVisible(video.Handle));
                await main.Player.TogglePlayPauseAsync();
                await BoundedDialogTests.Until(() => engine.Snapshot.IsPaused);
                panel.RefreshPlaybackUi();
                Assert.True(main.Player.IsVideoSurfaceRequired);
                Assert.True(IsWindowVisible(video.Handle));
                await main.Player.StopPlaybackAsync();
                await Dispatcher.Yield(DispatcherPriority.ApplicationIdle);
                Assert.Equal(MediaKind.Video, main.Player.Kind);
                Assert.Equal(file.Id, main.CurrentFile?.Id);
                Assert.False(main.Player.IsVideoSurfaceRequired);
                Assert.False(IsWindowVisible(video.Handle));
                window.Width = 1120; window.Height = 760;
                await Dispatcher.Yield(DispatcherPriority.ApplicationIdle);
                Assert.False(IsWindowVisible(video.Handle));
                await main.Player.TogglePlayPauseAsync();
                await BoundedDialogTests.Until(() => engine.Snapshot.IsPlaying && engine.Snapshot.Position.TotalMilliseconds > 100);
                await Dispatcher.Yield(DispatcherPriority.ApplicationIdle);
                Assert.Equal(originalHandle, video.Handle);
                Assert.True(main.Player.IsVideoSurfaceRequired);
                Assert.True(IsWindowVisible(video.Handle));
                await main.Player.StopPlaybackAsync();
                await Dispatcher.Yield(DispatcherPriority.ApplicationIdle);
                Assert.False(IsWindowVisible(video.Handle));
            }
            finally { await engine.ShutdownAsync(); window.PrepareForVerificationExit(); window.Close(); }
        }).WaitAsync(TimeSpan.FromSeconds(40));
    }
    [DllImport("user32.dll")] private static extern bool IsWindowVisible(IntPtr hwnd);
}

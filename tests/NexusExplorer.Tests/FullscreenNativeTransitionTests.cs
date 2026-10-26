using System.IO;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Interop;
using System.Windows.Threading;
using NexusExplorer.Infrastructure.Playback.Mpv;
using NexusExplorer.ViewModels;
using NexusExplorer.Views;

namespace NexusExplorer.Tests;

[Collection("NativePlaybackLogging")]
public class FullscreenNativeTransitionTests
{
    [Fact]
    public async Task NativeGeometryChangesAreCoveredAndPlaybackSessionSurvives()
    {
        using var host = new TestHost();
        var category = await host.Categories.CreateAsync("Fullscreen verification", null);
        var file = await host.Files.AddAsync(SyntheticMedia.WriteAvi(Path.Combine(host.RootDir, "fullscreen.avi")), category.Id);
        await WpfTestHost.RunAsync(async () =>
        {
            using var engine = new MpvPlaybackEngine(Dispatcher.CurrentDispatcher) { HardwareDecoding = false };
            var main = new MainViewModel(host.Categories, host.Files, host.Organization, engine);
            var window = new MainWindow(main, host.Categories, host.Files, host.Organization, host.RecycleBin, engine)
                { ShowInTaskbar = false, ShowActivated = false };
            HwndSource? source = null;
            var geometryChanges = 0;
            var uncoveredChanges = 0;
            IntPtr Observe(IntPtr hwnd, int message, IntPtr wparam, IntPtr lparam, ref bool handled)
            {
                if (message == 0x0047 && window.IsFullscreenTransitioning) // WM_WINDOWPOSCHANGED
                {
                    geometryChanges++;
                    if (!window.OwnedWindows.Cast<Window>().Any(w => w.IsVisible && w.Content is Image { Source: not null }))
                        uncoveredChanges++;
                }
                return IntPtr.Zero;
            }
            try
            {
                window.Show(); await main.SelectCategoryAsync(category); await main.SelectFileAsync(file);
                await BoundedDialogTests.Until(() => engine.Snapshot.Position.TotalMilliseconds > 100);
                var panel = (PlayerPanel)window.FindName("PlayerArea");
                var video = (MpvVideoHost)panel.FindName("VideoView");
                var handle = video.Handle;
                var token = engine.CurrentToken;
                source = HwndSource.FromHwnd(new WindowInteropHelper(window).Handle);
                source.AddHook(Observe);
                var before = engine.Snapshot.Position;
                await window.ToggleFocusPreviewAsync();
                Assert.Equal(handle, video.Handle);
                Assert.Equal(token, engine.CurrentToken);
                Assert.Equal(window.ActualWidth, video.ActualWidth, 1);
                Assert.Equal(window.ActualHeight, video.ActualHeight, 1);
                var fullscreenPosition = engine.Snapshot.Position;
                await BoundedDialogTests.Until(() => engine.Snapshot.Position > fullscreenPosition + TimeSpan.FromMilliseconds(100));
                await window.ToggleFocusPreviewAsync();
                Assert.Equal(handle, video.Handle);
                Assert.Equal(token, engine.CurrentToken);
                Assert.True(engine.Snapshot.IsPlaying);
                Assert.True(engine.Snapshot.Position >= before);
                var restoredPosition = engine.Snapshot.Position;
                await BoundedDialogTests.Until(() => engine.Snapshot.Position > restoredPosition + TimeSpan.FromMilliseconds(100));
                Assert.True(geometryChanges > 0);
                Assert.Equal(0, uncoveredChanges);
                Assert.DoesNotContain(window.OwnedWindows.Cast<Window>(), w => w.Content is Image);
            }
            finally
            {
                source?.RemoveHook(Observe);
                await engine.ShutdownAsync(); window.PrepareForVerificationExit(); window.Close();
            }
        }).WaitAsync(TimeSpan.FromSeconds(30));
    }
}

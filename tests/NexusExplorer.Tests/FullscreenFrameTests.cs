using System.Reflection;
using System.Windows;
using System.Windows.Interop;
using System.Windows.Shell;
using System.Windows.Threading;
using NexusExplorer.ViewModels;
using NexusExplorer.Views;

namespace NexusExplorer.Tests;

[Collection("NativePlaybackLogging")]
public class FullscreenFrameTests
{
    private readonly Xunit.Abstractions.ITestOutputHelper _output;
    public FullscreenFrameTests(Xunit.Abstractions.ITestOutputHelper output) => _output = output;
    [Fact]
    public async Task RepeatedFullscreenRestoresCustomChromeAndWindowState()
    {
        using var host = new TestHost();
        _output.WriteLine("Dispatcher requested");
        await WpfTestHost.RunAsync(async () =>
        {
            _output.WriteLine("Dispatcher ready");
            using var engine = new FakePlaybackEngine();
            var main = new MainViewModel(host.Categories, host.Files, host.Organization, engine);
            _output.WriteLine("Create window");
            var window = new MainWindow(main, host.Categories, host.Files, host.Organization, host.RecycleBin, engine)
                { ShowInTaskbar = false, ShowActivated = false };
            try
            {
                _output.WriteLine("Show"); window.Show(); _output.WriteLine("Shown");
                foreach (var initialState in new[] { WindowState.Normal, WindowState.Maximized })
                {
                window.WindowState = initialState;
                await Dispatcher.Yield(DispatcherPriority.Loaded);
                var originalRestoreBounds = window.RestoreBounds;
                var hwnd = new WindowInteropHelper(window).Handle;
                for (var i = 0; i < 3; i++)
                {
                    _output.WriteLine($"Enter {i}");
                    var entering = window.ToggleFocusPreviewAsync();
                    Assert.True(window.IsFullscreenTransitioning);
                    await window.ToggleFocusPreviewAsync(); // Repeated gesture cannot reverse an unfinished transition.
                    await entering;
                    Assert.False(window.IsFullscreenTransitioning);
                    _output.WriteLine("Entered");
                    await Dispatcher.Yield(DispatcherPriority.Loaded);
                    var fullscreenChrome = WindowChrome.GetWindowChrome(window);
                    _output.WriteLine($"Exit {i}"); await window.ToggleFocusPreviewAsync(); _output.WriteLine("Exited");
                    await Dispatcher.Yield(DispatcherPriority.Loaded);
                    Assert.Equal(hwnd, new WindowInteropHelper(window).Handle);
                    Assert.Equal(initialState, window.WindowState);
                    Assert.Equal(originalRestoreBounds.X, window.RestoreBounds.X, 0);
                    Assert.Equal(originalRestoreBounds.Y, window.RestoreBounds.Y, 0);
                    Assert.Equal(originalRestoreBounds.Width, window.RestoreBounds.Width, 0);
                    Assert.Equal(originalRestoreBounds.Height, window.RestoreBounds.Height, 0);
                    Assert.Equal(WindowStyle.SingleBorderWindow, window.WindowStyle);
                    Assert.Equal(ResizeMode.CanResize, window.ResizeMode);
                    var restoredChrome = WindowChrome.GetWindowChrome(window);
                    Assert.NotSame(fullscreenChrome, restoredChrome);
                    Assert.Equal(64, restoredChrome.CaptionHeight);
                    Assert.Equal(new Thickness(6), restoredChrome.ResizeBorderThickness);
                    Assert.False(restoredChrome.UseAeroCaptionButtons);
                }
                }
            }
            finally { window.PrepareForVerificationExit(); window.Close(); }
        }).WaitAsync(TimeSpan.FromSeconds(30));
    }
}

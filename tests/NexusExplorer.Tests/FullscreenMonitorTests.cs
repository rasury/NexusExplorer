using System.Runtime.InteropServices;
using System.Windows;
using System.Windows.Interop;
using System.Windows.Threading;
using NexusExplorer.ViewModels;
using NexusExplorer.Views;

namespace NexusExplorer.Tests;

[Collection("NativePlaybackLogging")]
public class FullscreenMonitorTests
{
    private readonly Xunit.Abstractions.ITestOutputHelper _output;
    public FullscreenMonitorTests(Xunit.Abstractions.ITestOutputHelper output) => _output = output;

    [Fact]
    public async Task ExitNeverMovesFullscreenSizedWindowOutsideItsMonitor()
    {
        var monitors = new List<NativeRect>();
        MonitorCallback enumerate = (IntPtr monitor, IntPtr dc, ref NativeRect rect, IntPtr data) => { monitors.Add(rect); return true; };
        Assert.True(EnumDisplayMonitors(IntPtr.Zero, IntPtr.Zero, enumerate, IntPtr.Zero));
        Assert.NotEmpty(monitors);
        using var host = new TestHost();
        await WpfTestHost.RunAsync(async () =>
        {
            using var engine = new FakePlaybackEngine();
            var main = new MainViewModel(host.Categories, host.Files, host.Organization, engine);
            var window = new MainWindow(main, host.Categories, host.Files, host.Organization, host.RecycleBin, engine)
                { ShowInTaskbar = false, ShowActivated = false };
            HwndSource? source = null;
            NativeRect monitorBounds = default;
            var exiting = false;
            var escaped = new List<string>();
            IntPtr Observe(IntPtr hwnd, int message, IntPtr wparam, IntPtr lparam, ref bool handled)
            {
                if (exiting && message == 0x0047 && GetWindowRect(hwnd, out var rect))
                {
                    // Allow invisible resize borders, but never a shifted screen-sized client area.
                    if (rect.Left < monitorBounds.Left - 16 || rect.Top < monitorBounds.Top - 16
                        || rect.Right > monitorBounds.Right + 16 || rect.Bottom > monitorBounds.Bottom + 16)
                        escaped.Add($"{rect.Left},{rect.Top}–{rect.Right},{rect.Bottom}");
                }
                return IntPtr.Zero;
            }
            try
            {
                window.Show();
                var hwnd = new WindowInteropHelper(window).Handle;
                source = HwndSource.FromHwnd(hwnd); source.AddHook(Observe);
                foreach (var monitor in monitors)
                foreach (var state in new[] { WindowState.Normal, WindowState.Maximized })
                {
                    monitorBounds = monitor;
                    window.WindowState = WindowState.Normal;
                    Assert.True(SetWindowPos(hwnd, IntPtr.Zero, monitor.Left + 100, monitor.Top + 100,
                        monitor.Right - monitor.Left - 200, monitor.Bottom - monitor.Top - 200, 0x14));
                    window.WindowState = state;
                    await Dispatcher.Yield(DispatcherPriority.Loaded);
                    var originalMonitor = MonitorFromWindow(hwnd, 2);
                    await window.ToggleFocusPreviewAsync();
                    Assert.Equal(originalMonitor, MonitorFromWindow(hwnd, 2));
                    escaped.Clear(); exiting = true;
                    await window.ToggleFocusPreviewAsync();
                    exiting = false;
                    Assert.Equal(state, window.WindowState);
                    Assert.Equal(originalMonitor, MonitorFromWindow(hwnd, 2));
                    _output.WriteLine($"Monitor {monitor.Left},{monitor.Top}–{monitor.Right},{monitor.Bottom}; {state}; escaped={string.Join(";", escaped)}");
                    Assert.Empty(escaped);
                }
            }
            finally { source?.RemoveHook(Observe); window.PrepareForVerificationExit(); window.Close(); }
        }).WaitAsync(TimeSpan.FromSeconds(30));
    }

    [StructLayout(LayoutKind.Sequential)] private struct NativeRect { public int Left, Top, Right, Bottom; }
    private delegate bool MonitorCallback(IntPtr monitor, IntPtr dc, ref NativeRect rect, IntPtr data);
    [DllImport("user32.dll")] private static extern bool EnumDisplayMonitors(IntPtr dc, IntPtr clip, MonitorCallback callback, IntPtr data);
    [DllImport("user32.dll")] private static extern bool GetWindowRect(IntPtr hwnd, out NativeRect rect);
    [DllImport("user32.dll")] private static extern IntPtr MonitorFromWindow(IntPtr hwnd, int flags);
    [DllImport("user32.dll")] private static extern bool SetWindowPos(IntPtr hwnd, IntPtr after, int x, int y, int width, int height, int flags);
}

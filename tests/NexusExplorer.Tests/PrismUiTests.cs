using System.IO;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using System.Windows.Shell;
using System.Windows.Threading;
using System.Windows.Input;
using NexusExplorer.Infrastructure;
using NexusExplorer.Models;
using NexusExplorer.ViewModels;
using NexusExplorer.Views;

namespace NexusExplorer.Tests;

[Collection("NativePlaybackLogging")]
public class PrismUiTests
{
    [Fact]
    public async Task NativeVideoClickPausesResumesAndFullscreenKeepsTheSameSurface()
    {
        using var host = new TestHost();
        var category = await host.Categories.CreateAsync("Video", null);
        var file = await host.Files.AddAsync(SyntheticMedia.WriteAvi(Path.Combine(host.RootDir, "click.avi")), category.Id);
        await WpfTestHost.RunAsync(async () =>
        {
            using var engine = new NexusExplorer.Infrastructure.Playback.Mpv.MpvPlaybackEngine(Dispatcher.CurrentDispatcher) { HardwareDecoding = false };
            var main = new MainViewModel(host.Categories, host.Files, host.Organization, engine);
            var window = new MainWindow(main, host.Categories, host.Files, host.Organization, host.RecycleBin, engine) { ShowInTaskbar = false };
            try
            {
                window.Show(); await main.SelectCategoryAsync(category); await main.SelectFileAsync(file);
                await BoundedDialogTests.Until(() => engine.Snapshot.Position.TotalMilliseconds > 100);
                var player = (PlayerPanel)window.FindName("PlayerArea");
                var video = (MpvVideoHost)player.FindName("VideoView");
                var handle = video.Handle;
                var region = CreateRectRgn(0, 0, 0, 0);
                try
                {
                    Assert.NotEqual(0, GetWindowRgn(handle, region));
                    Assert.False(PtInRegion(region, 0, 0));
                    Assert.True(PtInRegion(region, 30, 30));
                }
                finally { DeleteObject(region); }
                var child = IntPtr.Zero;
                EnumChildWindows(handle, (hwnd, _) => { var name = new System.Text.StringBuilder(128); GetClassName(hwnd, name, 128); if (name.ToString() == "mpv") child = hwnd; return true; }, IntPtr.Zero);
                Assert.NotEqual(IntPtr.Zero, child);
                void Click()
                {
                    SendMessage(child, 0x0201, (IntPtr)1, (IntPtr)((60 << 16) | 80));
                    SendMessage(child, 0x0202, IntPtr.Zero, (IntPtr)((60 << 16) | 80));
                }
                window.Activate();
                var pointer = video.PointToScreen(new Point(video.ActualWidth / 2, video.ActualHeight / 2));
                GetCursorPos(out var savedCursor);
                try
                {
                    SetCursorPos((int)pointer.X, (int)pointer.Y);
                    await Task.Delay(200);
                    mouse_event(2, 0, 0, 0, UIntPtr.Zero); mouse_event(4, 0, 0, 0, UIntPtr.Zero);
                    await BoundedDialogTests.Until(() => !engine.Snapshot.IsPlaying);
                }
                finally { SetCursorPos(savedCursor.X, savedCursor.Y); }
                await Task.Delay(600);
                try
                {
                    SetCursorPos((int)pointer.X, (int)pointer.Y); await Task.Delay(200);
                    mouse_event(2, 0, 0, 0, UIntPtr.Zero); mouse_event(4, 0, 0, 0, UIntPtr.Zero);
                    await BoundedDialogTests.Until(() => engine.Snapshot.IsPlaying);
                }
                finally { SetCursorPos(savedCursor.X, savedCursor.Y); }
                await Task.Delay(600); Click(); Click();
                await BoundedDialogTests.Until(() => ((FrameworkElement)window.FindName("TitleBar")).Visibility == Visibility.Collapsed);
                window.UpdateLayout(); Assert.Equal(handle, video.Handle); Assert.Equal(0, video.CornerRadius);
                Assert.Equal(window.ActualWidth, video.ActualWidth, 1); Assert.Equal(window.ActualHeight, video.ActualHeight, 1);
                SendMessage(child, 0x0100, (IntPtr)27, IntPtr.Zero);
                SendMessage(child, 0x0101, (IntPtr)27, IntPtr.Zero);
                await BoundedDialogTests.Until(() => ((FrameworkElement)window.FindName("TitleBar")).Visibility == Visibility.Visible);
                Assert.Equal(16, video.CornerRadius); Assert.Equal(handle, video.Handle);
            }
            finally { await engine.ShutdownAsync(); window.PrepareForVerificationExit(); window.Close(); }
        });
    }
    [Fact]
    public async Task NativeDoubleClickReachesPreviewGesture()
    {
        using var host = new TestHost();
        var path = SyntheticMedia.WriteAvi(Path.Combine(host.RootDir, "gesture.avi"));
        await WpfTestHost.RunAsync(async () =>
        {
            using var engine = new NexusExplorer.Infrastructure.Playback.Mpv.MpvPlaybackEngine() { HardwareDecoding = false };
            using var video = new MpvVideoHost();
            var window = new Window { Content = video, Width = 500, Height = 350, ShowInTaskbar = false, ShowActivated = false };
            window.Show(); engine.AttachSurface(video.Handle);
            try
            {
                await engine.PlayAsync(path, false);
                await BoundedDialogTests.Until(() => engine.Snapshot.Position.TotalMilliseconds > 100);
                var completed = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
                engine.VideoDoubleClicked += () => completed.TrySetResult();
                var child = IntPtr.Zero;
                EnumChildWindows(video.Handle, (hwnd, _) =>
                {
                    var name = new System.Text.StringBuilder(128); GetClassName(hwnd, name, name.Capacity);
                    if (name.ToString() == "mpv") child = hwnd;
                    return true;
                }, IntPtr.Zero);
                Assert.NotEqual(IntPtr.Zero, child);
                SendMessage(child, 0x0201, (IntPtr)1, (IntPtr)((60 << 16) | 80));
                SendMessage(child, 0x0202, IntPtr.Zero, (IntPtr)((60 << 16) | 80));
                SendMessage(child, 0x0201, (IntPtr)1, (IntPtr)((60 << 16) | 80));
                SendMessage(child, 0x0202, IntPtr.Zero, (IntPtr)((60 << 16) | 80));
                await completed.Task.WaitAsync(TimeSpan.FromSeconds(5));
                Assert.True(engine.Snapshot.IsPlaying);
            }
            finally { await engine.ShutdownAsync(); window.Close(); }
        });
    }
    private delegate bool ChildProc(IntPtr hwnd, IntPtr parameter);
    [System.Runtime.InteropServices.DllImport("gdi32.dll")] private static extern IntPtr CreateRectRgn(int left, int top, int right, int bottom);
    [System.Runtime.InteropServices.StructLayout(System.Runtime.InteropServices.LayoutKind.Sequential)] private struct CursorPoint { public int X, Y; }
    [System.Runtime.InteropServices.DllImport("user32.dll")] private static extern bool GetCursorPos(out CursorPoint point);
    [System.Runtime.InteropServices.DllImport("user32.dll")] private static extern bool SetCursorPos(int x, int y);
    [System.Runtime.InteropServices.DllImport("user32.dll")] private static extern void mouse_event(uint flags, uint x, uint y, uint data, UIntPtr extra);
    [System.Runtime.InteropServices.DllImport("user32.dll")] private static extern int GetWindowRgn(IntPtr window, IntPtr region);
    [System.Runtime.InteropServices.DllImport("gdi32.dll")] private static extern bool PtInRegion(IntPtr region, int x, int y);
    [System.Runtime.InteropServices.DllImport("gdi32.dll")] private static extern bool DeleteObject(IntPtr region);
    [System.Runtime.InteropServices.DllImport("user32.dll")] private static extern bool EnumChildWindows(IntPtr parent, ChildProc callback, IntPtr parameter);
    [System.Runtime.InteropServices.DllImport("user32.dll", CharSet = System.Runtime.InteropServices.CharSet.Unicode)] private static extern int GetClassName(IntPtr hwnd, System.Text.StringBuilder text, int count);
    [System.Runtime.InteropServices.DllImport("user32.dll")] private static extern IntPtr SendMessage(IntPtr hwnd, uint message, IntPtr wparam, IntPtr lparam);
    [Theory]
    [InlineData(UiThemeMode.Light)]
    [InlineData(UiThemeMode.Dark)]
    public async Task PrismShellKeepsFilteringPresentationOnlyAndInspectorOutOfTransport(UiThemeMode theme)
    {
        using var host = new TestHost();
        var category = await host.Categories.CreateAsync("旅行影像", null);
        await host.Categories.CreateAsync("自然与风景", null);
        await host.Categories.CreateAsync("音乐收藏", null);
        await host.Categories.CreateAsync("设计参考", null);
        var first = await host.Files.AddAsync(host.CreateTestFile("湖边的清晨.txt"), category.Id);
        await host.Files.AddAsync(host.CreateTestFile("旅行笔记.txt"), category.Id);
        await WpfTestHost.RunAsync(async () =>
        {
            UiThemeService.Apply(theme);
            using var engine = new FakePlaybackEngine();
            var main = new MainViewModel(host.Categories, host.Files, host.Organization, engine);
            var window = new MainWindow(main, host.Categories, host.Files, host.Organization, host.RecycleBin, engine)
            { ShowActivated = false, ShowInTaskbar = false };
            try
            {
                window.Show(); await main.RefreshTreeAsync(); await main.SelectCategoryAsync(category);
                await main.SelectFileAsync(first);
                window.UpdateLayout(); await Dispatcher.Yield(DispatcherPriority.ApplicationIdle);
                Assert.Equal(WindowStyle.SingleBorderWindow, window.WindowStyle);
                Assert.Equal(64, WindowChrome.GetWindowChrome(window).CaptionHeight);
                var left = (CategoryFilePanel)window.FindName("LeftPanel");
                var list = (ListBox)left.FindName("FileListBox");
                ((TextBox)window.FindName("FileSearch")).Text = "湖边";
                Assert.Single(list.Items.Cast<FileItem>());
                Assert.Equal(2, main.CurrentFiles.Count);
                Assert.Same(first, main.CurrentFile);
                ((TextBox)window.FindName("FileSearch")).Clear();
                Assert.Equal(2, list.Items.Count);
                var player = (PlayerPanel)window.FindName("PlayerArea");
                var preview = (FrameworkElement)player.FindName("PreviewSurface");
                var controls = (FrameworkElement)player.FindName("ControlsBar");
                var beforePreview = preview.ActualWidth; var beforeControls = controls.ActualWidth;
                player.ShowInspector(true);
                await Task.Delay(550); window.UpdateLayout();
                Assert.True(preview.ActualWidth < beforePreview - 100);
                Assert.Equal(beforeControls, controls.ActualWidth, 1);
                var slot = (FrameworkElement)player.FindName("InspectorSlot");
                Assert.Equal(preview.ActualHeight, slot.ActualHeight, 1);
                Assert.Equal(first.FileName, ((TextBlock)player.FindName("DetailName")).Text);
                Save(window, $"prism-{theme}.png");
                window.Width = 960; window.Height = 680; window.UpdateLayout();
                Assert.True(preview.ActualWidth >= 200);
                var grid = (Grid)left.FindName("CategoryFileLayout");
                Assert.True(grid.RowDefinitions[1].ActualHeight >= 120);
                Assert.True(grid.RowDefinitions[3].ActualHeight >= 120);
                Save(window, $"prism-small-{theme}.png");
                window.RaiseEvent(new KeyEventArgs(Keyboard.PrimaryDevice, PresentationSource.FromVisual(window), 0, Key.F11) { RoutedEvent = Keyboard.PreviewKeyDownEvent });
                window.UpdateLayout();
                Assert.Equal(Visibility.Collapsed, ((FrameworkElement)window.FindName("TitleBar")).Visibility);
                Assert.Equal(Visibility.Collapsed, ((FrameworkElement)window.FindName("SearchCard")).Visibility);
                Assert.Same(first, main.CurrentFile);
                Assert.Equal(Visibility.Collapsed, controls.Visibility);
                Assert.Equal(0, ((MpvVideoHost)player.FindName("VideoView")).CornerRadius);
                Assert.Equal(window.ActualWidth, preview.ActualWidth, 1);
                Assert.Equal(window.ActualHeight, preview.ActualHeight, 1);
                window.RaiseEvent(new KeyEventArgs(Keyboard.PrimaryDevice, PresentationSource.FromVisual(window), 0, Key.Escape) { RoutedEvent = Keyboard.PreviewKeyDownEvent });
                Assert.Equal(Visibility.Visible, ((FrameworkElement)window.FindName("TitleBar")).Visibility);
            }
            finally { window.PrepareForVerificationExit(); window.Close(); UiThemeService.Apply(UiThemeMode.Light); }
        });
    }
    private static void Save(Window window, string name)
    {
        var bitmap = new RenderTargetBitmap((int)window.ActualWidth, (int)window.ActualHeight, 96, 96, PixelFormats.Pbgra32);
        bitmap.Render(window);
        var encoder = new PngBitmapEncoder(); encoder.Frames.Add(BitmapFrame.Create(bitmap));
        var folder = Path.Combine(Path.GetTempPath(), "nexus-feature-checks"); Directory.CreateDirectory(folder);
        using var output = File.Create(Path.Combine(folder, name)); encoder.Save(output);
    }
}

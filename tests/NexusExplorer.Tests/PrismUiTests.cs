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
                Assert.Equal(WindowStyle.None, window.WindowStyle);
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

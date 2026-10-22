using System.IO;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Controls.Primitives;
using System.Windows.Interop;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using NexusExplorer.Infrastructure;
using NexusExplorer.Models;
using NexusExplorer.ViewModels;
using NexusExplorer.Views;
using NexusExplorer.Views.Dialogs;

namespace NexusExplorer.Tests;

[Collection("NativePlaybackLogging")]
public class PrismRefinementTests
{
    [Fact]
    public async Task ScopeCancellationDrawerLayoutWallpaperAndWorkAreaRemainConsistent()
    {
        using var host = new TestHost();
        var a = await host.Categories.CreateAsync("A", null);
        var b = await host.Categories.CreateAsync("B", null);
        var first = await host.Files.AddAsync(host.CreateTestFile("first.txt"), a.Id);
        var second = await host.Files.AddAsync(host.CreateTestFile("second.txt"), b.Id);
        await WpfTestHost.RunAsync(async () =>
        {
            using var engine = new FakePlaybackEngine();
            var main = new MainViewModel(host.Categories, host.Files, host.Organization, engine);
            var window = new MainWindow(main, host.Categories, host.Files, host.Organization, host.RecycleBin, engine) { ShowInTaskbar = false };
            try
            {
                window.Show(); await main.RefreshTreeAsync(); await main.SelectCategoryAsync(a);
                await main.SelectFileAsync(first); await Task.Delay(800); window.UpdateLayout();
                var left = (CategoryFilePanel)window.FindName("LeftPanel");
                var list = (ListBox)left.FindName("FileListBox");
                var search = (TextBox)window.FindName("FileSearch");
                var scope = (ComboBox)window.FindName("SearchScope");
                scope.SelectedIndex = 1; search.Text = "second";
                await BoundedDialogTests.Until(() => list.Items.Cast<FileItem>().Any(f => f.Id == second.Id));
                Assert.Single(main.CurrentFiles); Assert.Equal(first.Id, main.CurrentFile!.Id);
                search.Text = "first"; scope.SelectedIndex = 0; search.Clear();
                await Task.Delay(350); Assert.Equal(first.Id, Assert.Single(list.Items.Cast<FileItem>()).Id);
                var player = (PlayerPanel)window.FindName("PlayerArea");
                var preview = (FrameworkElement)player.FindName("PreviewSurface");
                var controls = (FrameworkElement)player.FindName("ControlsBar");
                var before = preview.ActualWidth;
                var toggle = (Button)window.FindName("DrawerToggleButton");
                toggle.RaiseEvent(new RoutedEventArgs(Button.ClickEvent)); await Task.Delay(800); window.UpdateLayout();
                Assert.True(preview.ActualWidth > before + 300);
                Assert.Equal(0, ((FrameworkElement)window.FindName("LibrarySlot")).ActualWidth);
                toggle.RaiseEvent(new RoutedEventArgs(Button.ClickEvent)); await Task.Delay(800); window.UpdateLayout();
                Assert.Equal(before, preview.ActualWidth, 1);
                var controlWidth = controls.ActualWidth;
                player.ShowInspector(true); await Task.Delay(800); window.UpdateLayout();
                Assert.Equal(controlWidth, controls.ActualWidth, 1);
                var pin = (ToggleButton)player.FindName("InspectorPin");
                var slot = (FrameworkElement)player.FindName("InspectorSlot");
                var pinPoint = pin.TranslatePoint(new Point(pin.ActualWidth / 2, pin.ActualHeight / 2), slot);
                Assert.True(new Rect(slot.RenderSize).Contains(pinPoint));
                Assert.True(slot.InputHitTest(pinPoint) is not null);
                var edge = slot.PointToScreen(new Point(slot.ActualWidth, 0));
                var windowEdge = window.PointToScreen(new Point(window.ActualWidth, 0));
                Assert.InRange(Math.Abs(windowEdge.X - edge.X), 0, 2);

                var path = Path.Combine(host.RootDir, "wallpaper.png");
                var bitmap = BitmapSource.Create(2, 2, 96, 96, PixelFormats.Bgra32, null, Enumerable.Repeat((byte)200, 16).ToArray(), 8);
                var encoder = new PngBitmapEncoder(); encoder.Frames.Add(BitmapFrame.Create(bitmap));
                using (var output = File.Create(path)) encoder.Save(output);
                await window.ApplyWallpaperAsync(new AppearanceConfig { BackgroundImagePath = path, BackgroundOpacity = .35 });
                Assert.Equal(.35, ((Image)window.FindName("WallpaperImage")).Opacity);
                using (File.Open(path, FileMode.Open, FileAccess.ReadWrite, FileShare.None)) { }
                var config = new AppConfig { Appearance = new() { BackgroundImagePath = path, BackgroundOpacity = .35 } };
                var settings = Path.Combine(host.RootDir, "settings.json"); config.Save(settings);
                Assert.Equal(.35, AppConfig.LoadOrDefault(settings).Appearance.BackgroundOpacity);

                MaterialDialogService.Notice("已归类到 B");
                await Task.Delay(900);
                Assert.Equal(Visibility.Visible, ((FrameworkElement)window.FindName("NoticeCard")).Visibility);
                await Task.Delay(1000);
                Assert.Equal(Visibility.Collapsed, ((FrameworkElement)window.FindName("NoticeCard")).Visibility);
                window.WindowState = WindowState.Maximized; await Task.Delay(250);
                var hwnd = new WindowInteropHelper(window).Handle;
                GetWindowRect(hwnd, out var rect);
                var info = new MonitorInfo { Size = System.Runtime.InteropServices.Marshal.SizeOf<MonitorInfo>() };
                GetMonitorInfo(MonitorFromWindow(hwnd, 2), ref info);
                Assert.Equal(info.Work.Bottom, rect.Bottom); Assert.Equal(info.Work.Top, rect.Top);
                Assert.Equal(info.Work.Left, rect.Left); Assert.Equal(info.Work.Right, rect.Right);
            }
            finally { window.PrepareForVerificationExit(); window.Close(); }
        });
    }
    [System.Runtime.InteropServices.StructLayout(System.Runtime.InteropServices.LayoutKind.Sequential)] private struct RectInt { public int Left, Top, Right, Bottom; }
    [System.Runtime.InteropServices.StructLayout(System.Runtime.InteropServices.LayoutKind.Sequential)] private struct MonitorInfo { public int Size; public RectInt Monitor, Work; public uint Flags; }
    [System.Runtime.InteropServices.DllImport("user32.dll")] private static extern bool GetWindowRect(IntPtr hwnd, out RectInt rect);
    [System.Runtime.InteropServices.DllImport("user32.dll")] private static extern IntPtr MonitorFromWindow(IntPtr hwnd, int flags);
    [System.Runtime.InteropServices.DllImport("user32.dll", CharSet = System.Runtime.InteropServices.CharSet.Auto)] private static extern bool GetMonitorInfo(IntPtr monitor, ref MonitorInfo info);
}

using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using NexusExplorer.Views;

namespace NexusExplorer.Tests;

[Collection("NativePlaybackLogging")]
public class SharedWallpaperTests
{
    [Fact]
    public async Task DefaultCardsShareCacheAndUseLocalBackdropOnlyWhileSizing() => await WpfTestHost.RunAsync(async () =>
    {
        Window? window = null;
        try
        {
            var scene = new Border { Background = Brushes.Red };
            var host = new WallpaperBlurHost { Source = scene };
            var card = new PrismCard { Width = 160, Height = 120 };
            var root = new Grid(); root.Children.Add(scene); root.Children.Add(host); root.Children.Add(card);
            window = new Window { Content = root, Width = 320, Height = 240, ShowInTaskbar = false };
            NameScope.SetNameScope(window, new NameScope());
            window.RegisterName("WallpaperScene", scene); window.RegisterName("WallpaperBlur", host);
            window.Show(); window.UpdateLayout();
            await System.Windows.Threading.Dispatcher.Yield(System.Windows.Threading.DispatcherPriority.ApplicationIdle);
            var shared = (Border)card.Template.FindName("PART_SharedBackdrop", card);
            var local = (Border)card.Template.FindName("PART_Backdrop", card);
            Assert.Equal(Visibility.Visible, shared.Visibility);
            Assert.Equal(Visibility.Collapsed, local.Visibility);
            Assert.Same(host.SharedBrush, shared.Background);
            host.SetResizing(true);
            Assert.Equal(Visibility.Collapsed, shared.Visibility);
            Assert.Equal(Visibility.Visible, local.Visibility);
            Assert.IsType<VisualBrush>(local.Background);
            host.SetResizing(false);
            Assert.Equal(Visibility.Visible, shared.Visibility);
            Assert.Equal(Visibility.Collapsed, local.Visibility);
            Assert.Same(host.SharedBrush, shared.Background);
        }
        finally { window?.Close(); }
    });

    [Fact]
    public async Task MappingCancelsAncestorScaleAndTranslation() => await WpfTestHost.RunAsync(() =>
    {
        var root = new Grid();
        var scene = new Border(); root.Children.Add(scene);
        var card = new Grid { Margin = new Thickness(31, 19, 0, 0), RenderTransformOrigin = new Point(.5, .5) };
        root.Children.Add(card);
        var window = new Window { Content = root, Width = 600, Height = 400, ShowInTaskbar = false };
        try
        {
            window.Show(); window.UpdateLayout();
            foreach (var scale in new[] { 1d, .78, 1.03 })
            {
                var transforms = new TransformGroup();
                transforms.Children.Add(new ScaleTransform(scale, scale));
                transforms.Children.Add(new TranslateTransform(160 * (1 - scale), 7));
                card.RenderTransform = transforms;
                var matrix = WallpaperBlurHost.MapUnitSurface(scene, card, scene.RenderSize);
                foreach (var p in new[] { new Point(0, 0), new Point(.3, .7), new Point(1, 1) })
                {
                    var actual = card.TransformToVisual(scene).Transform(matrix.Transform(p));
                    Assert.Equal(p.X * scene.ActualWidth, actual.X, 5);
                    Assert.Equal(p.Y * scene.ActualHeight, actual.Y, 5);
                }
            }
        }
        finally { window.Close(); }
    });

    [Fact]
    public async Task CachedWallpaperUpdatesAndHasOpaqueEdges() => await WpfTestHost.RunAsync(async () =>
    {
        var scene = new Border { Background = Brushes.Red };
        var host = new WallpaperBlurHost { Source = scene };
        var sample = new Border { Background = host.SharedBrush };
        var root = new Grid(); root.Children.Add(scene); root.Children.Add(host); root.Children.Add(sample);
        var window = new Window { Content = root, Width = 240, Height = 160, ShowInTaskbar = false };
        try
        {
            window.Show(); window.UpdateLayout();
            await System.Windows.Threading.Dispatcher.Yield(System.Windows.Threading.DispatcherPriority.ApplicationIdle);
            Assert.Equal(1, host.SharedBrush.BitmapCache.RenderAtScale);
            var first = Capture(sample);
            Assert.Equal(255, first[3]); Assert.True(first[2] > 240, "Initial: " + string.Join(",", first));
            scene.Background = Brushes.Blue;
            await System.Windows.Threading.Dispatcher.Yield(System.Windows.Threading.DispatcherPriority.ApplicationIdle);
            var next = Capture(sample);
            Assert.Equal(255, next[3]); Assert.True(next[0] > 240 && next[2] < 10, "Updated: " + string.Join(",", next));
            window.Width = 350; window.UpdateLayout();
            await System.Windows.Threading.Dispatcher.Yield(System.Windows.Threading.DispatcherPriority.ApplicationIdle);
            Assert.True(Capture(sample)[0] > 240);
            host.SetResizing(true);
            Assert.True(host.IsResizing);
            scene.Background = Brushes.Green;
            await System.Windows.Threading.Dispatcher.Yield(System.Windows.Threading.DispatcherPriority.ApplicationIdle);
            Assert.Equal(0, Capture(sample)[3]); // detached source does not keep rendering while sizing
            host.SetResizing(false);
            await System.Windows.Threading.Dispatcher.Yield(System.Windows.Threading.DispatcherPriority.ApplicationIdle);
            var restored = Capture(sample);
            Assert.Equal(255, restored[3]); Assert.True(restored[1] > 100 && restored[0] < 10);
        }
        finally { window.Close(); }
    });
    private static byte[] Capture(FrameworkElement element)
    {
        var bitmap = new RenderTargetBitmap((int)element.ActualWidth, (int)element.ActualHeight, 96, 96, PixelFormats.Pbgra32);
        bitmap.Render(element);
        var pixel = new byte[4]; bitmap.CopyPixels(new Int32Rect(0, 0, 1, 1), pixel, 4, 0);
        return pixel;
    }
}

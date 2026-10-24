using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using NexusExplorer.Views;

namespace NexusExplorer.Tests;

[Collection("NativePlaybackLogging")]
public class ThemeTransitionTests
{
    [Fact]
    public async Task RevealTravelsDownwardRejectsReentryAndCleansUp()
    {
        await WpfTestHost.RunAsync(async () =>
        {
            var root = new Grid { Background = Brushes.White };
            var window = new Window { Width = 400, Height = 400, Content = root, ShowInTaskbar = false };
            try
            {
                window.Show(); window.UpdateLayout(); await Task.Delay(50);
                var task = ThemeTransition.RunAsync(root, null, () => root.Background = Brushes.Black);
                Assert.True(ThemeTransition.IsRunning);
                var overlay = Assert.Single(root.Children.OfType<Image>());
                var ignored = false;
                Assert.Same(task, ThemeTransition.RunAsync(root, null, () => ignored = true));
                Assert.False(ignored);
                var mask = (LinearGradientBrush)overlay.OpacityMask;
                await BoundedDialogTests.Until(() => mask.GradientStops[1].Offset >= .45 || task.IsCompleted);
                Assert.False(task.IsCompleted);
                var bitmap = new RenderTargetBitmap((int)root.ActualWidth, (int)root.ActualHeight, 96, 96, PixelFormats.Pbgra32);
                bitmap.Render(root);
                byte Pixel(double y)
                {
                    var bytes = new byte[4]; bitmap.CopyPixels(new Int32Rect(10, (int)(bitmap.PixelHeight * y), 1, 1), bytes, 4, 0); return bytes[0];
                }
                Assert.True(Pixel(.1) < Pixel(.9), "The top must reveal the new dark theme before the bottom.");
                await task.WaitAsync(TimeSpan.FromSeconds(3));
                Assert.False(ThemeTransition.IsRunning); Assert.Empty(root.Children);
                await Assert.ThrowsAsync<InvalidOperationException>(() => ThemeTransition.RunAsync(root, null,
                    () => throw new InvalidOperationException("Theme application failed")));
                await ThemeTransition.Completion;
                Assert.False(ThemeTransition.IsRunning); Assert.Empty(root.Children);
                var reverse = ThemeTransition.RunAsync(root, null, () => root.Background = Brushes.White);
                window.Close();
                await reverse.WaitAsync(TimeSpan.FromSeconds(3));
                Assert.False(ThemeTransition.IsRunning); Assert.Empty(root.Children);
            }
            finally { window.Close(); }
        });
    }
}

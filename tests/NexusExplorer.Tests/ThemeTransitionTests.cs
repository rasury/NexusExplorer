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
    public void ThemeCurveIsSlowFastSlowWithoutOvershoot()
    {
        var segment = new MotionSegment(0, 1, 0, MotionProfiles.Theme);
        var duration = MotionProfiles.Theme.Seconds;
        Assert.Equal(0, segment.Sample(0).Velocity);
        Assert.Equal(0, segment.Sample(duration).Velocity);
        Assert.Equal(.2, segment.Sample(duration * .4).Value, 8);
        Assert.Equal(.8, segment.Sample(duration * .6).Value, 8);
        foreach (var turn in new[] { .4, .6 })
            Assert.InRange(Math.Abs(segment.Sample(duration * (turn - .000001)).Velocity - segment.Sample(duration * (turn + .000001)).Velocity), 0, .001);
        Assert.True(segment.Sample(duration * .5).Velocity > 10 * segment.Sample(duration * .1).Velocity);
        Assert.True(segment.Sample(duration * .5).Velocity > segment.Sample(duration * .1).Velocity);
        Assert.True(segment.Sample(duration * .5).Velocity > segment.Sample(duration * .9).Velocity);
        for (var i = 0; i <= 100; i++) Assert.InRange(segment.Sample(duration * i / 100).Value, 0, 1);
    }
    [Fact]
    public async Task RenderedRevealShowsSlowEdgesAndFastMiddle()
    {
        await WpfTestHost.RunAsync(async () =>
        {
            var root = new Grid { Background = Brushes.White };
            var window = new Window { Width = 400, Height = 400, Content = root, ShowInTaskbar = false };
            Task? transition = null;
            try
            {
                window.Show(); window.UpdateLayout();
                transition = ThemeTransition.RunAsync(root, null, () => root.Background = Brushes.Black);
                var image = Assert.Single(root.Children.OfType<Image>());
                var mask = (LinearGradientBrush)image.OpacityMask;
                var clear = mask.GradientStops[0]; var opaque = mask.GradientStops[1];
                PrismMotion.StopAll(clear); PrismMotion.StopAll(opaque);
                var startClear = clear.Offset; var startOpaque = opaque.Offset;
                var segment = new MotionSegment(0, 1, 0, MotionProfiles.Theme);
                var measured = new List<double>();
                var montage = new DrawingVisual();
                using (var drawing = montage.RenderOpen())
                {
                    foreach (var phase in new[] { .2, .4, .5, .6, .8 })
                    {
                        var position = segment.Sample(phase * MotionProfiles.Theme.Seconds).Value;
                        clear.Offset = startClear + position * (1 + MotionProfiles.ThemeFeather);
                        opaque.Offset = startOpaque + position * (1 + MotionProfiles.ThemeFeather);
                        root.UpdateLayout();
                        var bitmap = new RenderTargetBitmap((int)root.ActualWidth, (int)root.ActualHeight, 96, 96, PixelFormats.Pbgra32);
                        bitmap.Render(root);
                        var pixels = new byte[bitmap.PixelHeight * 4];
                        bitmap.CopyPixels(new Int32Rect(10, 0, 1, bitmap.PixelHeight), pixels, 4, 0);
                        var front = 0;
                        while (front < bitmap.PixelHeight && pixels[front * 4] < 128) front++;
                        measured.Add((double)front / bitmap.PixelHeight);
                        drawing.DrawImage(bitmap, new Rect((measured.Count - 1) * 200, 0, 200, 200));
                    }
                }
                Assert.InRange(measured[0], .02, .06);
                Assert.InRange(measured[1], .17, .21);
                Assert.InRange(measured[2], .48, .52);
                Assert.InRange(measured[3], .79, .83);
                Assert.InRange(measured[4], .94, .98);
                // Equal time spans: the middle visibly travels much farther than the edges.
                Assert.True(measured[3] - measured[1] > 3 * (measured[1] - measured[0]));
                var output = new RenderTargetBitmap(1000, 200, 96, 96, PixelFormats.Pbgra32); output.Render(montage);
                var encoder = new PngBitmapEncoder(); encoder.Frames.Add(BitmapFrame.Create(output));
                using var file = System.IO.File.Create(System.IO.Path.Combine(System.IO.Path.GetTempPath(), "theme-visible-stages.png")); encoder.Save(file);
            }
            finally { window.Close(); if (transition is not null) await transition.WaitAsync(TimeSpan.FromSeconds(3)); }
        });
    }
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

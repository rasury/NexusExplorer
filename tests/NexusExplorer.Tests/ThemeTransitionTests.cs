using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using NexusExplorer.Views;

namespace NexusExplorer.Tests;

[Collection("NativePlaybackLogging")]
public class ThemeTransitionTests
{
    [Theory]
    [InlineData(0, 1)]
    [InlineData(8, 1)]
    [InlineData(12.5, 1.5)]
    public async Task LocalSnapshotExcludesParentOffsetAndPreservesAllFourCorners(double inset, double scale)
    {
        await WpfTestHost.RunAsync(() =>
        {
            var target = new Grid { Margin = new Thickness(inset) };
            target.RowDefinitions.Add(new RowDefinition()); target.RowDefinitions.Add(new RowDefinition());
            target.ColumnDefinitions.Add(new ColumnDefinition()); target.ColumnDefinitions.Add(new ColumnDefinition());
            var colors = new[] { Colors.Red, Colors.Lime, Colors.Blue, Colors.Yellow };
            for (var i = 0; i < colors.Length; i++)
            {
                var block = new Border { Background = new SolidColorBrush(colors[i]) };
                Grid.SetRow(block, i / 2); Grid.SetColumn(block, i % 2); target.Children.Add(block);
            }
            var parent = new Grid { LayoutTransform = new ScaleTransform(scale, scale), RenderTransform = new TranslateTransform(3, 5) };
            parent.Children.Add(target);
            parent.Measure(new Size(1600.5, 900.5));
            parent.Arrange(new Rect(0, 0, 1600.5, 900.5)); parent.UpdateLayout();
            var before = target.Margin;
            var bitmap = ThemeTransition.CaptureLocal(target);
            Assert.True(bitmap.IsFrozen);
            Assert.InRange(bitmap.PixelWidth, 1, 721);
            Assert.InRange(Math.Abs(target.ActualWidth - bitmap.Width), 0, .001);
            Assert.InRange(Math.Abs(target.ActualHeight - bitmap.Height), 0, .001);
            var points = new[] { new Point(0, 0), new Point(bitmap.PixelWidth - 1, 0),
                new Point(0, bitmap.PixelHeight - 1), new Point(bitmap.PixelWidth - 1, bitmap.PixelHeight - 1) };
            for (var i = 0; i < points.Length; i++)
            {
                var pixel = new byte[4];
                bitmap.CopyPixels(new Int32Rect((int)points[i].X, (int)points[i].Y, 1, 1), pixel, 4, 0);
                Assert.Equal(new byte[] { colors[i].B, colors[i].G, colors[i].R, 255 }, pixel);
            }
            Assert.Equal(before, target.Margin);
            Assert.Equal(4, target.Children.Count);
        });
    }

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
    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task MainAndDialogSnapshotsStayAlignedWithInsets(bool maximized)
    {
        await WpfTestHost.RunAsync(async () =>
        {
            var root = new Grid { Background = Brushes.White, Margin = new Thickness(8) };
            var window = new Window { Content = root, Width = 700, Height = 500, ShowInTaskbar = false };
            var dialog = new Grid { Background = Brushes.White, Margin = new Thickness(24) };
            var shell = new Window { Content = dialog, Width = 400, Height = 300, ShowInTaskbar = false };
            Task? transition = null;
            try
            {
                window.Show();
                shell.Owner = window;
                if (maximized) window.WindowState = WindowState.Maximized;
                shell.Show(); window.UpdateLayout(); shell.UpdateLayout();
                transition = ThemeTransition.RunAsync(root, dialog, () =>
                    { root.Background = Brushes.Black; dialog.Background = Brushes.Black; });
                window.UpdateLayout(); shell.UpdateLayout();
                foreach (var target in new[] { root, dialog })
                {
                    var overlay = Assert.Single(target.Children.OfType<Image>());
                    Assert.Equal(new Point(), overlay.TranslatePoint(new Point(), target));
                    Assert.Equal(target.ActualWidth, overlay.ActualWidth, 3);
                    Assert.Equal(target.ActualHeight, overlay.ActualHeight, 3);
                    var bitmap = Assert.IsAssignableFrom<BitmapSource>(overlay.Source);
                    var pixel = new byte[4];
                    bitmap.CopyPixels(new Int32Rect(0, 0, 1, 1), pixel, 4, 0);
                    Assert.Equal(new byte[] { 255, 255, 255, 255 }, pixel);
                }
                await transition;
                Assert.Empty(root.Children); Assert.Empty(dialog.Children);
                Assert.Equal(new Thickness(8), root.Margin);
                Assert.Equal(new Thickness(24), dialog.Margin);
            }
            finally
            {
                shell.Close(); window.Close();
                if (transition is not null) await transition.WaitAsync(TimeSpan.FromSeconds(3));
            }
        });
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

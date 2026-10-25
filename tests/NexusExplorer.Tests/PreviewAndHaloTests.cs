using System.Windows;
using System.Windows.Controls;
using System.Windows.Controls.Primitives;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using System.Windows.Shapes;
using NexusExplorer.Infrastructure;
using NexusExplorer.Models;
using NexusExplorer.Services;
using NexusExplorer.Views;

namespace NexusExplorer.Tests;

public class PreviewAndHaloTests
{
    [Fact]
    public async Task ProgressHaloShrinksOutsideExtensionAndHitAreaWithoutChangingGrip()
    {
        await WpfTestHost.RunAsync(() =>
        {
            var original = new Slider { Width = 300, Height = 20, Value = .5, Maximum = 1, Margin = new Thickness(0, 30, 0, 30) };
            var compact = new Slider { Width = 300, Height = 20, Value = .5, Maximum = 1, Margin = new Thickness(0, 30, 0, 30) };
            CompactSliderHalo.SetIsEnabled(compact, true);
            var stack = new StackPanel { Background = Brushes.White }; stack.Children.Add(original); stack.Children.Add(compact);
            var window = WindowFor(stack);
            try
            {
                window.Show(); window.UpdateLayout();
                var before = ((Track)original.Template.FindName("PART_Track", original)).Thumb;
                var after = ((Track)compact.Template.FindName("PART_Track", compact)).Thumb;
                var originalGrip = (Ellipse)before.Template.FindName("grip", before);
                var compactGrip = (Ellipse)after.Template.FindName("grip", after);
                var originalHalo = (Ellipse)before.Template.FindName("halo", before);
                var compactHalo = (Ellipse)after.Template.FindName("halo", after);
                Assert.Equal(originalGrip.ActualWidth, compactGrip.ActualWidth);
                Assert.Equal(originalGrip.ActualHeight, compactGrip.ActualHeight);
                Assert.InRange(Math.Abs((compactHalo.Width - compactGrip.ActualWidth) / (originalHalo.Width - originalGrip.ActualWidth) - .4), 0, .001);
                Assert.Equal(compactHalo.Width, ((Ellipse)after.Template.FindName("focusedHalo", after)).Width);
                Assert.Same(before, HitThumb(window, before, -20));
                Assert.NotSame(after, HitThumb(window, after, -20));
                Assert.Same(after, HitThumb(window, after, -14));
                Assert.Same(after, HitThumb(window, after, 0));
                Assert.Equal(.5, compact.Value);
            }
            finally { window.Close(); }
        });
    }
    private static Thumb? HitThumb(Window window, Thumb thumb, double yOffset)
    {
        var center = thumb.TranslatePoint(new Point(thumb.ActualWidth / 2, thumb.ActualHeight / 2 + yOffset), window);
        return CategoryFilePanel.FindAncestor<Thumb>(window.InputHitTest(center) as DependencyObject);
    }
    [Theory]
    [InlineData(UiThemeMode.Light, 64, 32)]
    [InlineData(UiThemeMode.Dark, 64, 32)]
    [InlineData(UiThemeMode.Light, 32, 64)]
    [InlineData(UiThemeMode.Dark, 32, 64)]
    public async Task PreviewBackgroundFitsImageAndFallbackIsOpaque(UiThemeMode theme, int width, int height)
    {
        await WpfTestHost.RunAsync(() =>
        {
            UiThemeService.Apply(theme);
            var view = new FileThumbnailPreview(); var window = WindowFor(view);
            try
            {
                window.Show();
                var bitmap = BitmapSource.Create(width, height, 96, 96, PixelFormats.Bgra32, null, new byte[width * height * 4], width * 4);
                bitmap.Freeze();
                view.ShowResult(new(bitmap, ThumbnailStatus.Ready)); window.UpdateLayout();
                var surface = (Border)view.FindName("PreviewSurface"); var image = (Image)view.FindName("PreviewImage");
                Assert.Equal(image.ActualWidth, surface.ActualWidth);
                Assert.Equal(image.ActualHeight, surface.ActualHeight);
                Assert.InRange(Math.Abs(surface.ActualWidth / surface.ActualHeight - (double)width / height), 0, .001);
                Assert.Equal(Visibility.Collapsed, ((FrameworkElement)view.FindName("FallbackPanel")).Visibility);
                view.ShowLoading(new FileItem { FileName = "file.mp3" }); window.UpdateLayout();
                AssertFallback(view);
                view.ShowResult(new(null, ThumbnailStatus.Unavailable)); window.UpdateLayout(); AssertFallback(view);
                var rendered = new RenderTargetBitmap((int)surface.ActualWidth, (int)surface.ActualHeight, 96, 96, PixelFormats.Pbgra32);
                rendered.Render(surface); var corner = new byte[4]; rendered.CopyPixels(new Int32Rect(0, 0, 1, 1), corner, 4, 0);
                Assert.Equal(255, corner[3]);
            }
            finally { window.Close(); UiThemeService.Apply(UiThemeMode.Light); }
        });
    }
    private static void AssertFallback(FileThumbnailPreview view)
    {
        var surface = (Border)view.FindName("PreviewSurface");
        Assert.Equal(240, surface.ActualWidth); Assert.True(surface.ActualHeight >= 128);
        Assert.Equal(Visibility.Visible, ((FrameworkElement)view.FindName("FallbackPanel")).Visibility);
        Assert.Equal(Visibility.Collapsed, ((Image)view.FindName("PreviewImage")).Visibility);
        Assert.NotEqual(0, ((SolidColorBrush)surface.Background).Color.A);
    }
    private static Window WindowFor(UIElement content) => new()
    { Content = content, SizeToContent = SizeToContent.WidthAndHeight, ShowActivated = false, ShowInTaskbar = false, WindowStartupLocation = WindowStartupLocation.Manual, Left = -5000, Top = -5000 };
}

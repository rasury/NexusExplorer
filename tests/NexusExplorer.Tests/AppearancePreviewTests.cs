using System.IO;
using NexusExplorer.Infrastructure;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Controls.Primitives;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using MaterialDesignThemes.Wpf;
using NexusExplorer.ViewModels;
using NexusExplorer.Views;
using NexusExplorer.Views.Dialogs;

namespace NexusExplorer.Tests;
[Collection("NativePlaybackLogging")]
public class AppearancePreviewTests
{
    [Theory]
    [InlineData(UiThemeMode.Light)]
    [InlineData(UiThemeMode.Dark)]
    public async Task AppearanceOpensWithPreparedBackdropPreviewsTransparencyAndCancels(UiThemeMode theme)
    {
        using var host = new TestHost();
        await WpfTestHost.RunAsync(async () =>
        {
            using var engine = new FakePlaybackEngine();
            var main = new MainViewModel(host.Categories, host.Files, host.Organization, engine);
            var window = new MainWindow(main, host.Categories, host.Files, host.Organization, host.RecycleBin, engine) { ShowInTaskbar = false };
            try
            {
                window.Show(); await Task.Delay(700); window.UpdateLayout();
                var wallpaper = (Image)window.FindName("WallpaperImage"); var original = wallpaper.Opacity;
                var button = Descendants<Button>(window).Single(b => Equals(b.ToolTip, "外观设置"));
                button.RaiseEvent(new RoutedEventArgs(Button.ClickEvent));
                var dialog = (DialogHost)window.FindName("RootDialog");
                await BoundedDialogTests.Until(() => dialog.DialogContent is PrismCard { IsLoaded: true });
                UiThemeService.Apply(theme);
                var glass = (PrismCard)dialog.DialogContent!;
                var surface = (DialogSurface)glass.Content; glass.UpdateLayout();
                var backdrop = (Border)glass.Template.FindName("PART_Backdrop", glass);
                Assert.IsType<VisualBrush>(backdrop.Background);
                Assert.Same(window.FindName("WallpaperScene"), ((VisualBrush)backdrop.Background).Visual);
                await Task.Delay(400); glass.UpdateLayout();
                var actions = (Panel)surface.Children[1];
                var buttons = actions.Children.OfType<Button>().ToArray();
                Assert.Equal(2, buttons.Length);
                var first = buttons[0].TranslatePoint(new Point(), surface);
                var last = buttons[1].TranslatePoint(new Point(), surface);
                Assert.Equal(first.Y, last.Y, 1);
                Assert.Equal(8, last.X - first.X - buttons[0].ActualWidth, 1);
                foreach (var action in buttons)
                {
                    Assert.Equal(40, action.ActualHeight);
                    var text = Descendants<TextBlock>(action).Single();
                    var center = text.TranslatePoint(new Point(text.ActualWidth / 2, text.ActualHeight / 2), action);
                    Assert.InRange(Math.Abs(center.Y - action.ActualHeight / 2), 0, 1);
                    Assert.InRange(Math.Abs(center.X - action.ActualWidth / 2), 0, 1);
                    Assert.IsType<LinearGradientBrush>(action.Background);
                }
                var slider = Descendants<Slider>(surface).Single();
                var top = slider.TranslatePoint(new Point(), surface);
                Assert.True(top.X >= 12, $"left={top.X}"); Assert.True(top.X + slider.ActualWidth + 12 <= surface.ActualWidth, $"left={top.X}; slider={slider.ActualWidth}; surface={surface.ActualWidth}");
                slider.Value = 0;
                Assert.Equal(0, wallpaper.Opacity); Assert.Equal(0, ((Border)window.FindName("WallpaperShade")).Opacity);
                Assert.Equal(Colors.Black, ((SolidColorBrush)((Grid)window.FindName("WallpaperScene")).Background).Color);
                slider.Value = 45; Assert.Equal(.45, wallpaper.Opacity);
                await Task.Delay(150); glass.UpdateLayout();
                var bitmap = new RenderTargetBitmap((int)glass.ActualWidth, (int)glass.ActualHeight, 96, 96, PixelFormats.Pbgra32); bitmap.Render(glass);
                var encoder = new PngBitmapEncoder(); encoder.Frames.Add(BitmapFrame.Create(bitmap));
                var folder = Path.Combine(Path.GetTempPath(), "nexus-feature-checks"); Directory.CreateDirectory(folder);
                using (var stream = File.Create(Path.Combine(folder, $"appearance-preview-{theme}.png"))) encoder.Save(stream);
                var popup = (Popup)dialog.Template.FindName("PART_Popup", dialog);
                var card = (Card)dialog.Template.FindName("PART_PopupContentElement", dialog);
                var width = card.ActualWidth; var height = card.ActualHeight;
                surface.Complete(null);
                Assert.False(dialog.IsOpen);
                Assert.Same(glass, dialog.DialogContent);
                // Logical close must not collapse the popup during its exit animation.
                var observedExit = false;
                var deadline = DateTime.UtcNow.AddSeconds(5);
                while (popup.IsOpen)
                {
                    Assert.True(DateTime.UtcNow < deadline);
                    observedExit = true; card.UpdateLayout();
                    Assert.Same(glass, dialog.DialogContent);
                    Assert.Equal(width, card.ActualWidth, 1);
                    Assert.Equal(height, card.ActualHeight, 1);
                    await Task.Delay(15);
                }
                Assert.True(observedExit);
                await BoundedDialogTests.Until(() => dialog.DialogContent is null);
                await BoundedDialogTests.Until(() => !dialog.IsOpen && Math.Abs(wallpaper.Opacity - original) < .001);
            }
            finally { MaterialDialogService.CancelAll(); window.PrepareForVerificationExit(); window.Close(); }
        });
    }
    private static IEnumerable<T> Descendants<T>(DependencyObject root) where T : DependencyObject
    {
        for (var i = 0; i < VisualTreeHelper.GetChildrenCount(root); i++)
        {
            var child = VisualTreeHelper.GetChild(root, i);
            if (child is T typed) yield return typed;
            foreach (var item in Descendants<T>(child)) yield return item;
        }
    }
}

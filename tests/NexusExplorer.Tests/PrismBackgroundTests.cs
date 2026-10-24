using System.IO;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using NexusExplorer.Views;

namespace NexusExplorer.Tests;

public class PrismBackgroundTests
{
    [Fact]
    public async Task BackgroundScalesChangesPaletteAndFadesToBlack()
    {
        await WpfTestHost.RunAsync(() =>
        {
            var background = new PrismBackground();
            var scene = new Grid { Background = Brushes.Black, Children = { background } };
            BitmapSource Render(int width, int height)
            {
                scene.Measure(new Size(width, height)); scene.Arrange(new Rect(0, 0, width, height)); scene.UpdateLayout();
                var bitmap = new RenderTargetBitmap(width, height, 96, 96, PixelFormats.Pbgra32); bitmap.Render(scene); return bitmap;
            }
            int Brightness(BitmapSource bitmap)
            {
                var pixel = new byte[4]; bitmap.CopyPixels(new Int32Rect(bitmap.PixelWidth / 2, bitmap.PixelHeight / 2, 1, 1), pixel, 4, 0);
                return pixel[0] + pixel[1] + pixel[2];
            }
            var folder = Path.Combine(Path.GetTempPath(), "nexus-feature-checks"); Directory.CreateDirectory(folder);
            foreach (var dark in new[] { false, true })
            {
                background.IsDark = dark;
                var bitmap = Render(1360, 900);
                var png = new PngBitmapEncoder(); png.Frames.Add(BitmapFrame.Create(bitmap));
                using var stream = File.Create(Path.Combine(folder, $"gradient-{dark}.png")); png.Save(stream);
            }
            var darkLevel = Brightness(Render(960, 680));
            background.IsDark = false;
            Assert.True(Brightness(Render(960, 680)) > darkLevel);
            Assert.Equal(new Size(960, 680), background.RenderSize);
            Assert.False(background.IsHitTestVisible);
            background.Opacity = 0;
            Assert.Equal(0, Brightness(Render(960, 680)));
            return Task.CompletedTask;
        });
    }
}

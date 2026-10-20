using System.IO;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using System.Windows.Threading;
using NexusExplorer.Infrastructure;
using NexusExplorer.Views;

namespace NexusExplorer.Tests;

[Collection("NativePlaybackLogging")]
public sealed class CategoryFileSplitterTests
{
    [Theory]
    [InlineData(UiThemeMode.Light)]
    [InlineData(UiThemeMode.Dark)]
    public async Task NativeSplitterResizesBothListsAndHonorsLimitsWhenWindowShrinks(UiThemeMode theme)
    {
        await WpfTestHost.RunAsync(async () =>
        {
            UiThemeService.Apply(theme);
            var panel = new CategoryFilePanel();
            var window = new Window { Content = panel, Width = 360, Height = 600, ShowInTaskbar = false };
            window.Show(); window.UpdateLayout();
            try
            {
                var grid = (Grid)panel.FindName("CategoryFileLayout");
                var splitter = (GridSplitter)panel.FindName("CategoryFileSplitter");
                var tree = (TreeView)panel.FindName("CategoryTree");
                var files = (ListBox)panel.FindName("FileListBox");
                Assert.True(CategoryFilePanel.IsControlChrome(splitter));
                Assert.True(splitter.ShowsPreview);
                var before = grid.RowDefinitions[1].ActualHeight;
                splitter.Focus(); await Dispatcher.Yield(DispatcherPriority.Input);
                void Move(Key key)
                {
                    splitter.RaiseEvent(new KeyEventArgs(Keyboard.PrimaryDevice, PresentationSource.FromVisual(splitter), 0, key)
                    { RoutedEvent = Keyboard.KeyDownEvent });
                    window.UpdateLayout();
                }
                Move(Key.Down);
                Assert.True(grid.RowDefinitions[1].ActualHeight > before);
                for (var i = 0; i < 80; i++) Move(Key.Down);
                Assert.InRange(grid.RowDefinitions[3].ActualHeight, 120, 120.5);
                Assert.True(tree.ActualHeight > files.ActualHeight);
                for (var i = 0; i < 80; i++) Move(Key.Up);
                Assert.InRange(grid.RowDefinitions[1].ActualHeight, 120, 120.5);
                Assert.True(files.ActualHeight > tree.ActualHeight);
                window.Height = 380; window.UpdateLayout();
                Assert.True(grid.RowDefinitions[1].ActualHeight >= 120);
                Assert.True(grid.RowDefinitions[3].ActualHeight >= 120);
                var boundary = splitter.TranslatePoint(new Point(0, splitter.ActualHeight), panel).Y;
                Assert.True(files.TranslatePoint(new Point(), panel).Y >= boundary);
                Assert.True(files.TranslatePoint(new Point(0, files.ActualHeight), panel).Y <= panel.ActualHeight + 1);
                var bitmap = new RenderTargetBitmap((int)panel.ActualWidth, (int)panel.ActualHeight, 96, 96, PixelFormats.Pbgra32);
                bitmap.Render(panel);
                var encoder = new PngBitmapEncoder(); encoder.Frames.Add(BitmapFrame.Create(bitmap));
                var folder = Path.Combine(Path.GetTempPath(), "nexus-feature-checks"); Directory.CreateDirectory(folder);
                using var output = File.Create(Path.Combine(folder, $"category-file-splitter-{theme}.png")); encoder.Save(output);
            }
            finally { window.Close(); UiThemeService.Apply(UiThemeMode.Light); }
        });
    }
}

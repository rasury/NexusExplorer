using System.Diagnostics;
using System.IO;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using NexusExplorer.Infrastructure;
using NexusExplorer.Models;
using NexusExplorer.Services;
using NexusExplorer.Views;

namespace NexusExplorer.Tests;

[Collection("NativePlaybackLogging")]
public class FileHoverPreviewTests
{
    [Theory]
    [InlineData(UiThemeMode.Light)]
    [InlineData(UiThemeMode.Dark)]
    public async Task HoverWaitsFiveHundredMillisecondsAndQuickExitDoesNotLoad(UiThemeMode theme)
    {
        await WpfTestHost.RunAsync(async () =>
        {
            UiThemeService.Apply(theme);
            var file = new FileItem { Id = 1, FileName = "悬停缩略图.png", AbsolutePath = "unused.png" };
            var list = new ListBox { ItemsSource = new[] { file }, Height = 200 };
            var window = WindowFor(list); var calls = 0;
            using var preview = new FileHoverPreview(list, (_, _) => { calls++; return Task.FromResult(new ThumbnailResult(ThumbnailTests.Bitmap(), ThumbnailStatus.Ready)); }, _ => true);
            try
            {
                window.Show(); window.UpdateLayout(); var row = (ListBoxItem)list.ItemContainerGenerator.ContainerFromIndex(0);
                preview.Hover(row); await Task.Delay(250); Assert.Equal(0, calls); Assert.False(preview.IsOpen);
                list.RaiseEvent(new MouseEventArgs(Mouse.PrimaryDevice, Environment.TickCount) { RoutedEvent = UIElement.MouseLeaveEvent });
                await Task.Delay(350); Assert.Equal(0, calls);
                var watch = Stopwatch.StartNew(); preview.Hover(row);
                await BoundedDialogTests.Until(() => preview.IsOpen && preview.View!.IsLoaded);
                Assert.True(watch.ElapsedMilliseconds >= 450); Assert.Equal(1, calls);
                Assert.Equal("悬停缩略图.png", ((TextBlock)preview.View!.FindName("FileTitle")).Text);
                Assert.NotNull(((Image)preview.View.FindName("PreviewImage")).Source); Assert.Null(list.SelectedItem);
                // Let the framework tooltip fade-in finish before visual verification.
                await Task.Delay(180); Save(preview.View, $"preview-{theme}");
                list.RaiseEvent(new MouseWheelEventArgs(Mouse.PrimaryDevice, Environment.TickCount, -120) { RoutedEvent = UIElement.PreviewMouseWheelEvent });
                Assert.False(preview.IsOpen);
            }
            finally { window.Close(); UiThemeService.Apply(UiThemeMode.Light); }
        });
    }

    [Fact]
    public async Task RecycledRowsClosePreviewAndLateResultsCannotReplaceNextFile()
    {
        await WpfTestHost.RunAsync(async () =>
        {
            var a = new FileItem { Id = 1, FileName = "A.png", AbsolutePath = "A.png" };
            var b = new FileItem { Id = 2, FileName = "B.png", AbsolutePath = "B.png" };
            var list = new ListBox { ItemsSource = new[] { a }, Height = 200 }; var window = WindowFor(list);
            var oldResult = new TaskCompletionSource<ThumbnailResult>(TaskCreationOptions.RunContinuationsAsynchronously);
            var nextImage = ThumbnailTests.Bitmap(); var oldToken = default(CancellationToken);
            using var preview = new FileHoverPreview(list, (path, token) =>
            { if (path == a.AbsolutePath) { oldToken = token; return oldResult.Task; } return Task.FromResult(new ThumbnailResult(nextImage, ThumbnailStatus.Ready)); }, _ => true);
            try
            {
                window.Show(); window.UpdateLayout(); var row = (ListBoxItem)list.ItemContainerGenerator.ContainerFromIndex(0);
                preview.Hover(row); await BoundedDialogTests.Until(() => preview.IsOpen);
                row.DataContext = b; Assert.False(preview.IsOpen); Assert.True(oldToken.IsCancellationRequested);
                preview.Hover(row); await BoundedDialogTests.Until(() => preview.IsOpen && ((TextBlock)preview.View!.FindName("FileTitle")).Text == "B.png");
                oldResult.SetResult(new(ThumbnailTests.Bitmap(), ThumbnailStatus.Ready)); await Task.Delay(30);
                Assert.Same(nextImage, ((Image)preview.View!.FindName("PreviewImage")).Source);
                list.IsEnabled = false; Assert.False(preview.IsOpen);
            }
            finally { oldResult.TrySetCanceled(); window.Close(); }
        });
    }

    [Theory]
    [InlineData(ThumbnailStatus.Missing, "文件已失效")]
    [InlineData(ThumbnailStatus.Unavailable, "无可用缩略图")]
    internal async Task MissingOrUnsupportedFilesShowMaterialIconWithoutAnErrorDialog(ThumbnailStatus status, string text)
    {
        await WpfTestHost.RunAsync(async () =>
        {
            var file = new FileItem { FileName = "文件.mp3", AbsolutePath = "unused.mp3" };
            var list = new ListBox { ItemsSource = new[] { file }, Height = 200 }; var window = WindowFor(list);
            using var preview = new FileHoverPreview(list, (_, _) => Task.FromResult(new ThumbnailResult(null, status)), _ => true);
            try
            {
                window.Show(); window.UpdateLayout(); preview.Hover((ListBoxItem)list.ItemContainerGenerator.ContainerFromIndex(0));
                await BoundedDialogTests.Until(() => preview.IsOpen);
                Assert.Equal(text, ((TextBlock)preview.View!.FindName("PreviewStatus")).Text);
                Assert.Equal(Visibility.Visible, ((FrameworkElement)preview.View.FindName("FallbackIcon")).Visibility);
                Assert.Equal(Visibility.Collapsed, ((Image)preview.View.FindName("PreviewImage")).Visibility);
            }
            finally { window.Close(); }
        });
    }

    private static Window WindowFor(UIElement content) => new() { Content = content, Width = 400, Height = 300, ShowActivated = false, ShowInTaskbar = false, Left = -5000, Top = -5000 };
    private static void Save(FrameworkElement view, string name)
    {
        var directory = Path.GetFullPath("../../../../../artifacts/hover-preview", AppContext.BaseDirectory); Directory.CreateDirectory(directory);
        var surface = CategoryFilePanel.FindAncestor<ToolTip>(view)!;
        surface.UpdateLayout(); var visual = new DrawingVisual();
        using (var drawing = visual.RenderOpen()) drawing.DrawRectangle(new VisualBrush(surface), null, new Rect(0, 0, surface.ActualWidth, surface.ActualHeight));
        var bitmap = new RenderTargetBitmap((int)Math.Ceiling(surface.ActualWidth), (int)Math.Ceiling(surface.ActualHeight), 96, 96, PixelFormats.Pbgra32); bitmap.Render(visual);
        var encoder = new PngBitmapEncoder(); encoder.Frames.Add(BitmapFrame.Create(bitmap)); using var file = File.Create(Path.Combine(directory, name + ".png")); encoder.Save(file);
    }
}

using System.Windows;
using System.Windows.Controls;
using System.Windows.Interop;
using System.Windows.Media;
using System.Windows.Threading;
using MaterialDesignThemes.Wpf;
using NexusExplorer.Views.Dialogs;

namespace NexusExplorer.Tests;

[Collection("NativePlaybackLogging")]
public sealed class InputWindowTests
{
    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task InputRefocusUsesTheSameOwnedWindowAndKeepsTheCaret(bool accept)
    {
        await WpfTestHost.RunAsync(async () =>
        {
            var host = new DialogHost { Identifier = Guid.NewGuid().ToString() };
            var owner = new Window { Content = host, Width = 800, Height = 600, ShowInTaskbar = false };
            owner.Show(); await Dispatcher.Yield(DispatcherPriority.Loaded); MaterialDialogService.Register(host);
            try
            {
                var result = InputDialog.ShowAsync("新建分类", "分类名称", "中文分类");
                Window? shell = null;
                await BoundedDialogTests.Until(() => (shell = System.Windows.Application.Current.Windows.OfType<Window>().FirstOrDefault(w => ReferenceEquals(w.Owner, owner)))?.Content is DialogHost { IsOpen: true, DialogContent: DialogSurface { IsLoaded: true } });
                var surface = (DialogSurface)((DialogHost)shell!.Content).DialogContent!;
                var body = (StackPanel)((ScrollViewer)surface.Children[0]).Content;
                var input = body.Children.OfType<TextBox>().Single();
                await BoundedDialogTests.Until(() => input.SelectionLength == input.Text.Length);
                Assert.False(owner.IsEnabled);
                Assert.Equal(WindowStyle.None, shell.WindowStyle);
                Assert.Equal(ResizeMode.NoResize, shell.ResizeMode);
                shell.UpdateLayout();
                await Task.Delay(350); shell.UpdateLayout();
                Assert.True(input.TranslatePoint(new Point(input.ActualWidth, 0), surface).X <= surface.ActualWidth + .1);
                var hwnd = new WindowInteropHelper(shell).Handle;
                Assert.Equal(hwnd, ((HwndSource)PresentationSource.FromVisual(input)).Handle);
                var actions = ((Panel)surface.Children[1]).Children.OfType<Button>().ToList();
                foreach (var button in actions)
                {
                    var right = button.TranslatePoint(new Point(button.ActualWidth, button.ActualHeight), surface);
                    Assert.True(right.X <= surface.ActualWidth + .1);
                    Assert.True(right.Y <= surface.ActualHeight + .1);
                }
                var bitmap = new System.Windows.Media.Imaging.RenderTargetBitmap((int)shell.ActualWidth, (int)shell.ActualHeight, 96, 96, PixelFormats.Pbgra32);
                bitmap.Render((Visual)shell.Content);
                var encoder = new System.Windows.Media.Imaging.PngBitmapEncoder(); encoder.Frames.Add(System.Windows.Media.Imaging.BitmapFrame.Create(bitmap));
                var folder = System.IO.Path.Combine(System.IO.Path.GetTempPath(), "nexus-feature-checks"); System.IO.Directory.CreateDirectory(folder);
                using (var image = System.IO.File.Create(System.IO.Path.Combine(folder, "input-card.png"))) encoder.Save(image);
                actions[0].Focus(); input.Focus(); input.CaretIndex = 2;
                await Dispatcher.Yield(DispatcherPriority.Input);
                Assert.True(input.IsKeyboardFocused); Assert.Equal(2, input.CaretIndex);
                Assert.Equal(hwnd, ((HwndSource)PresentationSource.FromVisual(input)).Handle);
                actions.Single(b => Equals(b.Content, accept ? "确定" : "取消")).RaiseEvent(new RoutedEventArgs(Button.ClickEvent));
                Assert.Equal(accept ? "中文分类" : null, await result);
                Assert.True(owner.IsEnabled); Assert.False(shell.IsVisible);
            }
            finally { MaterialDialogService.CancelAll(); MaterialDialogService.Unregister(host); owner.Close(); }
        });
    }
}

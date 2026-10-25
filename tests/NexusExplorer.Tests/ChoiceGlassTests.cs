using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using MaterialDesignThemes.Wpf;
using NexusExplorer.Views;
using NexusExplorer.Views.Dialogs;
using NexusExplorer.Infrastructure;

namespace NexusExplorer.Tests;
[Collection("NativePlaybackLogging")]
public class ChoiceGlassTests
{
    [Theory]
    [InlineData(UiThemeMode.Light)]
    [InlineData(UiThemeMode.Dark)]
    public async Task TrackRowsUseRoundedSelectionAndKeepExplicitApply(UiThemeMode theme)
    {
        await WpfTestHost.RunAsync(async () =>
        {
            var host = new DialogHost { Identifier = Guid.NewGuid().ToString() };
            var window = new Window { Content = host, Width = 800, Height = 600, ShowInTaskbar = false };
            try
            {
                window.Show(); UiThemeService.Apply(theme); MaterialDialogService.Register(host);
                var task = ChoiceDialog.ShowAsync("选择音轨", "音轨", new[] { new DialogChoice<long>("禁用音轨", -1), new DialogChoice<long>("音轨 1", 1) }, 1L);
                await BoundedDialogTests.Until(() => host.DialogContent is PrismCard { IsLoaded: true });
                var glass = (PrismCard)host.DialogContent!;
                var surface = (DialogSurface)glass.Content;
                var body = (StackPanel)((ScrollViewer)surface.Children[0]).Content;
                var list = body.Children.OfType<ListBox>().Single();
                list.UpdateLayout();
                Assert.Equal(1L, ((DialogChoice<long>)list.SelectedItem).Value);
                var item = (ListBoxItem)list.ItemContainerGenerator.ContainerFromIndex(0);
                var row = (Border)item.Template.FindName("Row", item);
                Assert.True(row.CornerRadius.TopLeft > 0);
                Assert.True(item.Margin.Top > 0);
                list.SelectedIndex = 0; Assert.False(task.IsCompleted);
                await Task.Delay(250);
                ((Panel)surface.Children[1]).Children.OfType<Button>().Single(b => Equals(b.Content, "应用")).RaiseEvent(new RoutedEventArgs(Button.ClickEvent));
                Assert.True(host.IsOpen); Assert.Same(glass, host.DialogContent);
                var result = await task;
                Assert.True(result.Confirmed); Assert.Equal(-1L, result.Value);
                Assert.Null(host.DialogContent);
            }
            finally { MaterialDialogService.CancelAll(); MaterialDialogService.Unregister(host); window.Close(); UiThemeService.Apply(UiThemeMode.Light); }
        });
    }
}
